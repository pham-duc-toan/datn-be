"""Consumer RabbitMQ — ban Python cua EventConsumer + IdempotencyGuard ben C#.

Topology GIONG HET C# (de cung van hanh mot kieu):
  - exchange datn.events (topic) va datn.dlx (direct);
  - queue quorum "<service>.<ten>" bind routing key = eventType, x-delivery-limit,
    vuot nguong thi broker tu day sang DLX → queue "<ten>.dlq";
  - sai hop dong → reject khong requeue (vao DLQ ngay);
  - loi xu ly → nack requeue (broker dem lan giao, qua tran thi vao DLQ).

Idempotency: trong MOT transaction — kiem processed_events, chay xu ly, ghi
processed_events, commit. Event giao lai lan hai thi bo qua.
"""

import logging
from collections.abc import Awaitable, Callable
from typing import Generic, TypeVar

import aio_pika
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app import settings_store
from app.config import settings
from app.db import session_factory
from app.messaging import envelope as env_mod
from app.messaging.envelope import Envelope, LoiHopDong, Payload

log = logging.getLogger(__name__)

P = TypeVar("P", bound=Payload)

# Xu ly mot event trong session da mo transaction. KHONG commit — consumer lo.
XuLy = Callable[[AsyncSession, Envelope[P]], Awaitable[None]]


class Consumer(Generic[P]):
    def __init__(self, ten_queue: str, loai: type[P], xu_ly: XuLy) -> None:
        self.ten_queue = ten_queue
        self.loai = loai
        self.xu_ly = xu_ly
        # Ten handler trong processed_events — cung quy uoc C#: ten queue.
        self.handler = ten_queue

    async def bat_dau(self, ket_noi: aio_pika.abc.AbstractRobustConnection) -> None:
        cfg = settings()
        kenh = await ket_noi.channel()
        # Setting consumers.prefetch_count — co tac dung khi service khoi dong lai.
        await kenh.set_qos(prefetch_count=settings_store.so_nguyen(settings_store.CONSUMERS_PREFETCH_COUNT))

        exchange = await kenh.declare_exchange(cfg.exchange, aio_pika.ExchangeType.TOPIC, durable=True)
        dlx = await kenh.declare_exchange(cfg.dead_letter_exchange, aio_pika.ExchangeType.DIRECT, durable=True)

        dlq = await kenh.declare_queue(self.ten_queue + ".dlq", durable=True)
        await dlq.bind(dlx, routing_key=self.ten_queue)

        queue = await kenh.declare_queue(
            self.ten_queue,
            durable=True,
            arguments={
                "x-queue-type": "quorum",
                "x-delivery-limit": cfg.delivery_limit,
                "x-dead-letter-exchange": cfg.dead_letter_exchange,
                "x-dead-letter-routing-key": self.ten_queue,
            },
        )
        await queue.bind(exchange, routing_key=self.loai.EVENT_TYPE)
        await queue.consume(self._nhan)
        log.info("Consumer %s dang nghe %s", self.ten_queue, self.loai.EVENT_TYPE)

    async def _nhan(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            env = env_mod.doc(message.body, self.loai)
        except LoiHopDong as ex:
            log.error("Queue %s: message sai hop dong → DLQ: %s", self.ten_queue, ex)
            await message.reject(requeue=False)
            return

        try:
            await self._xu_ly_mot_lan(env)
        except Exception:  # noqa: BLE001
            log.exception("Queue %s: xu ly event %s that bai, tra lai hang doi", self.ten_queue, env.event_id)
            await message.nack(requeue=True)
            return

        await message.ack()

    async def _xu_ly_mot_lan(self, env: Envelope[P]) -> None:
        async with session_factory()() as session:
            async with session.begin():
                da_xu_ly = (await session.execute(
                    text("SELECT 1 FROM processed_events WHERE event_id = :e AND handler = :h"),
                    {"e": env.event_id, "h": self.handler},
                )).first()
                if da_xu_ly is not None:
                    log.info("Bo qua event %s cho %s: da xu ly tu truoc", env.event_id, self.handler)
                    return

                await self.xu_ly(session, env)

                await session.execute(
                    text("INSERT INTO processed_events (event_id, handler, processed_at) VALUES (:e, :h, now())"),
                    {"e": env.event_id, "h": self.handler},
                )
        # IntegrityError o day (hai ban sao cung xu ly mot event) se roi vao nhanh
        # nack → lan giao sau thay processed_events va bo qua. Khong nuot: neu loi
        # la rang buoc KHAC thi phai hien ra (bai hoc cua IdempotencyGuard ben C#).


__all__ = ["Consumer", "IntegrityError"]
