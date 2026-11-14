"""Cat san HLS cho video vua nap (hls.py) — MOT lan moi doan mau, truoc khi du an publish.

  - nghe samples.ingested: moi FILE video → MOT viec trong queue ben "media-svc.chuan-hoa" (khoi dong lai khong
    mat), kem cac doan mau cua file (metadata segmentStart / segmentEnd, khong cat = ca file) va chu watermark
    dung chung project-svc dung san;
  - worker lam lan luot tung viec: tai file goc MOT lan, cat tung doan chua co, ghi len MinIO canh file goc
    ("<projectId>/_wm/hls-v1/…"), roi phat video.prepared (thanh cong / file hong) → project-svc moi cho publish / duyet;
  - luc phat: doan nao chua co (video nap truoc ban nay) thi views.py cat tai cho (hls.dam_bao_doan).
Khong co DB: viec giao lai thay doan da co tren MinIO → bo qua, chi phat lai video.prepared (project-svc ghi de, vo hai).
"""

import asyncio
import json
import logging
import uuid
from typing import Any

import aio_pika

from app import hls, settings_store, watermark_av
from app.config import settings
from app.contracts import SamplesIngested, VideoPrepared
from app.messaging import envelope as env_mod
from app.messaging import thu_lai
from app.messaging.envelope import LoiHopDong
from app.storage import KhongCoFile

log = logging.getLogger(__name__)

TEN_QUEUE = "media-svc.samples-ingested"
TEN_VIEC = "media-svc.chuan-hoa"
# Mot viec cat video dai co the lau hon han ack mac dinh cua RabbitMQ (30 phut).
HAN_VIEC_MS = 4 * 3600 * 1000


def doan_cua_mau(metadata: Any) -> tuple[float, float | None]:
    """[a, b] cua mau trong file (giay). b None = het file (metadata khong co thoi luong)."""
    md = metadata if isinstance(metadata, dict) else {}
    a = md.get("segmentStart")
    b = md.get("segmentEnd")
    if b is None:
        b = md.get("durationSec")
    return float(a or 0), None if b is None else float(b)


def cac_viec(p: SamplesIngested) -> list[dict[str, Any]]:
    """Moi FILE video mot viec (cac doan cat tu mot file dung chung khoa) kem moi doan mau cua file."""
    theo_file: dict[str, list[list[float | None]]] = {}
    for s in p.samples:
        if s.modality != "video" or not s.storage_key:
            continue
        a, b = doan_cua_mau(s.metadata)
        ds = theo_file.setdefault(s.storage_key, [])
        if [a, b] not in ds:
            ds.append([a, b])
    return [{"storageKey": k, "doan": v, "chu": p.watermark_text} for k, v in theo_file.items()]


async def tra_lai(message: aio_pika.abc.AbstractIncomingMessage, mo_ta: str) -> None:
    """Loai 3 (kien truc 3.29, loi ky thuat): cho gian cach roi tra lai hang doi; lan cuoi (sap vao DLQ) log ERROR.
    Goi trong khoi except — log kem traceback."""
    da = thu_lai.so_lan_da_that_bai(message.headers)
    if thu_lai.la_lan_cuoi(da, settings().delivery_limit):
        log.exception("%s — that bai %d lan, chuyen vao DLQ, can dev xu ly", mo_ta, da + 1)
    else:
        cho = thu_lai.cho_truoc_khi_thu_lai(da, settings_store.giay(settings_store.CONSUMERS_RETRY_MAX_DELAY))
        log.warning("%s (lan %d), thu lai sau %.0fs", mo_ta, da + 1, cho, exc_info=True)
        await thu_lai.ngu(cho)
    await message.nack(requeue=True)


class ChuanHoaSan:
    """Consumer samples.ingested → queue viec ben → worker cat HLS lan luot (khong chiem het CPU)."""

    def __init__(self) -> None:
        self._kenh_nhan: aio_pika.abc.AbstractChannel | None = None
        self._exchange: aio_pika.abc.AbstractExchange | None = None

    async def bat_dau(self, ket_noi: aio_pika.abc.AbstractRobustConnection) -> None:
        cfg = settings()
        dlx_args = {"x-queue-type": "quorum", "x-delivery-limit": cfg.delivery_limit,
                    "x-dead-letter-exchange": cfg.dead_letter_exchange}

        kenh = await ket_noi.channel()
        self._kenh_nhan = kenh
        await kenh.set_qos(prefetch_count=10)
        self._exchange = await kenh.declare_exchange(cfg.exchange, aio_pika.ExchangeType.TOPIC, durable=True)
        dlx = await kenh.declare_exchange(cfg.dead_letter_exchange, aio_pika.ExchangeType.DIRECT, durable=True)
        for ten in (TEN_QUEUE, TEN_VIEC):
            dlq = await kenh.declare_queue(ten + ".dlq", durable=True)
            await dlq.bind(dlx, routing_key=ten)
        nhan = await kenh.declare_queue(TEN_QUEUE, durable=True, arguments={**dlx_args, "x-dead-letter-routing-key": TEN_QUEUE})
        await nhan.bind(self._exchange, routing_key=SamplesIngested.EVENT_TYPE)
        await kenh.declare_queue(TEN_VIEC, durable=True, arguments={
            **dlx_args, "x-dead-letter-routing-key": TEN_VIEC, "x-consumer-timeout": HAN_VIEC_MS})
        await nhan.consume(self._nhan)

        # Kenh rieng cho worker: moi lan MOT viec.
        kenh_viec = await ket_noi.channel()
        await kenh_viec.set_qos(prefetch_count=1)
        viec = await kenh_viec.get_queue(TEN_VIEC)
        await viec.consume(self._lam)
        log.info("Consumer %s dang nghe %s; worker %s san sang", TEN_QUEUE, SamplesIngested.EVENT_TYPE, TEN_VIEC)

    async def _nhan(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            env = env_mod.doc(message.body, SamplesIngested)
        except LoiHopDong as ex:
            log.error("Queue %s: message sai hop dong → DLQ: %s", TEN_QUEUE, ex)
            await message.reject(requeue=False)
            return
        try:
            assert self._kenh_nhan is not None
            for viec in cac_viec(env.payload):
                than = json.dumps({**viec, "projectId": str(env.payload.project_id),
                                   "correlationId": str(env.correlation_id), "causationId": str(env.event_id)})
                await self._kenh_nhan.default_exchange.publish(
                    aio_pika.Message(than.encode(), content_type="application/json",
                                     delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
                    routing_key=TEN_VIEC)
        except Exception:  # noqa: BLE001
            await tra_lai(message, f"Queue {TEN_QUEUE}: khong xep duoc viec cat HLS")
            return
        await message.ack()

    async def _lam(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            v = json.loads(message.body)
            du_an, key = uuid.UUID(v["projectId"]), str(v["storageKey"])
            doan = [(float(a), None if b is None else float(b)) for a, b in v["doan"]]
            chu = str(v["chu"])
            tuong_quan, nguyen_nhan = uuid.UUID(v["correlationId"]), uuid.UUID(v["causationId"])
        except (ValueError, KeyError, TypeError) as ex:
            log.error("Viec cat HLS sai dinh dang → DLQ: %s", ex)
            await message.reject(requeue=False)
            return

        loi: str | None = None
        try:
            so = await asyncio.to_thread(hls.cat_cac_doan, key, doan, chu)
            log.info("Da cat HLS video %s: %d doan moi / %d doan", key, so, len(doan))
        except KhongCoFile:
            log.info("File %s khong con (du an da xoa?) — bo qua", key)
            await message.ack()
            return
        except watermark_av.LoiMedia as ex:
            log.warning("Video %s hong, khong cat duoc HLS: %s", key, ex)
            loi = str(ex)[:500]
        except Exception:  # noqa: BLE001 — MinIO / dia: thu lai (qua x-delivery-limit thi vao DLQ)
            await tra_lai(message, f"Cat HLS video {key} that bai")
            return

        try:
            await self._phat(VideoPrepared(project_id=du_an, storage_key=key, succeeded=loi is None, error=loi),
                             tuong_quan, nguyen_nhan)
        except Exception:  # noqa: BLE001
            await tra_lai(message, f"Khong phat duoc video.prepared cho {key}")
            return
        await message.ack()

    async def _phat(self, payload: VideoPrepared, tuong_quan: uuid.UUID, nguyen_nhan: uuid.UUID) -> None:
        assert self._exchange is not None
        env = env_mod.tao(payload, settings().service_name, tuong_quan, nguyen_nhan)
        await self._exchange.publish(
            aio_pika.Message(env.to_json().encode("utf-8"), content_type="application/json", type=VideoPrepared.EVENT_TYPE,
                             message_id=str(env.event_id), delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
            routing_key=VideoPrepared.EVENT_TYPE)
