"""Transactional outbox — ban Python cua Crowd.BuildingBlocks.Persistence.Outbox.

Event duoc GHI vao bang outbox trong CUNG transaction voi du lieu nghiep vu
(ghi()), roi vong lap Dispatcher day sang RabbitMQ. Khong bao gio commit roi
moi publish: service chet o giua la mat event.
"""

import asyncio
import json
import logging
import uuid
from datetime import datetime, timedelta, timezone

import aio_pika
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import settings_store
from app.config import settings
from app.db import session_factory
from app.messaging import envelope as env_mod
from app.messaging.envelope import Payload

log = logging.getLogger(__name__)

# Giong OutboxMessage.MarkFailed: cho 2^lan giay, tran = setting outbox.retry_max_delay.
DO_DAI_LOI_TOI_DA = 2000


async def ghi(session: AsyncSession, payload: Payload, correlation_id: uuid.UUID, causation_id: uuid.UUID | None) -> uuid.UUID:
    """Xep event vao outbox trong transaction hien tai cua session. Tra ve eventId."""
    env = env_mod.tao(payload, settings().service_name, correlation_id, causation_id)
    await session.execute(
        text(
            "INSERT INTO outbox (id, event_type, version, correlation_id, occurred_at, envelope_json, attempt_count, next_attempt_at) "
            "VALUES (:id, :event_type, :version, :correlation_id, :occurred_at, CAST(:envelope AS jsonb), 0, :next_attempt_at)"
        ),
        {
            "id": env.event_id,
            "event_type": env.event_type,
            "version": env.version,
            "correlation_id": env.correlation_id,
            "occurred_at": env.occurred_at,
            "envelope": env.to_json(),
            "next_attempt_at": env.occurred_at,
        },
    )
    return env.event_id


class Dispatcher:
    """Vong lap: lay mot lo outbox (FOR UPDATE SKIP LOCKED), publish co xac nhan, danh dau da gui."""

    def __init__(self) -> None:
        self._ket_noi: aio_pika.abc.AbstractRobustConnection | None = None
        self._kenh: aio_pika.abc.AbstractChannel | None = None
        self._exchange: aio_pika.abc.AbstractExchange | None = None

    async def _lay_exchange(self) -> aio_pika.abc.AbstractExchange:
        if self._exchange is not None and self._kenh is not None and not self._kenh.is_closed:
            return self._exchange

        cfg = settings()
        self._ket_noi = await aio_pika.connect_robust(
            host=cfg.rabbit_host, port=cfg.rabbit_port, login=cfg.rabbit_user,
            password=cfg.rabbit_password, virtualhost=cfg.rabbit_vhost,
            client_properties={"connection_name": cfg.service_name + ".outbox"})
        # publisher_confirms: publish chi tra ve KHI broker da ghi nhan message.
        self._kenh = await self._ket_noi.channel(publisher_confirms=True)
        self._exchange = await self._kenh.declare_exchange(cfg.exchange, aio_pika.ExchangeType.TOPIC, durable=True)
        log.info("Outbox da noi RabbitMQ %s:%s, exchange %s", cfg.rabbit_host, cfg.rabbit_port, cfg.exchange)
        return self._exchange

    async def chay(self, dung: asyncio.Event) -> None:
        while not dung.is_set():
            try:
                so = await self._mot_lo()
            except Exception:  # noqa: BLE001 — vong lap khong duoc chet im lang
                log.exception("Lo outbox that bai, thu lai sau %ss", settings_store.giay(settings_store.OUTBOX_POLL_INTERVAL))
                so = 0

            if so >= settings_store.so_nguyen(settings_store.OUTBOX_BATCH_SIZE):
                continue
            try:
                await asyncio.wait_for(dung.wait(), timeout=settings_store.giay(settings_store.OUTBOX_POLL_INTERVAL))
            except asyncio.TimeoutError:
                pass

        if self._ket_noi is not None:
            await self._ket_noi.close()

    async def _mot_lo(self) -> int:
        async with session_factory()() as session, session.begin():
            dong = (await session.execute(
                text(
                    "SELECT id, event_type, correlation_id, envelope_json, attempt_count FROM outbox "
                    "WHERE published_at IS NULL AND next_attempt_at <= now() "
                    "ORDER BY occurred_at, id LIMIT :n FOR UPDATE SKIP LOCKED"
                ),
                {"n": settings_store.so_nguyen(settings_store.OUTBOX_BATCH_SIZE)},
            )).all()

            if not dong:
                return 0

            exchange = await self._lay_exchange()
            bay_gio = datetime.now(timezone.utc)

            for d in dong:
                envelope_json = d.envelope_json if isinstance(d.envelope_json, str) else json.dumps(d.envelope_json)
                try:
                    message = aio_pika.Message(
                        body=envelope_json.encode("utf-8"),
                        content_type="application/json",
                        delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                        message_id=str(d.id),
                        correlation_id=str(d.correlation_id),
                        type=d.event_type,
                    )
                    await asyncio.wait_for(
                        exchange.publish(message, routing_key=d.event_type),
                        timeout=settings_store.giay(settings_store.OUTBOX_PUBLISH_TIMEOUT))
                    await session.execute(text("UPDATE outbox SET published_at = :t WHERE id = :id"), {"t": bay_gio, "id": d.id})
                except Exception as ex:  # noqa: BLE001
                    lan = d.attempt_count + 1
                    cho = min(2 ** min(lan, 20), settings_store.giay(settings_store.OUTBOX_RETRY_MAX_DELAY))
                    await session.execute(
                        text("UPDATE outbox SET attempt_count = :lan, last_error = :loi, next_attempt_at = :tiep WHERE id = :id"),
                        {"lan": lan, "loi": str(ex)[:DO_DAI_LOI_TOI_DA], "tiep": bay_gio + timedelta(seconds=cho), "id": d.id},
                    )
                    log.warning("Gui %s %s that bai lan %s, thu lai sau %ss: %s", d.event_type, d.id, lan, cho, ex)
                    # Mat ket noi thi noi lai o lo sau.
                    self._exchange = None

            return len(dong)
