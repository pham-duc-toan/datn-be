"""Ban chuan cua video (watermark_av.chuan_hoa_video): tao SAN luc doanh nghiep nap du lieu.

Moi nguoi xem chi nhung watermark vao khung dau + khung cuoi (vai giay), nhung ban chuan phai ma hoa lai CA
video mot lan (video 8 phut 720p ~30 giay). Truoc day lam o lan mo dau tien → nguoi dau tien cho lau. Nay:
  - nghe dataset.ingested: moi file video → MOT viec trong queue ben "media-svc.chuan-hoa" (khoi dong lai khong
    mat); worker lam lan luot tung viec: chuan hoa, ghi len MinIO canh file goc
    ("<projectId>/_wm/chuan-v2/<phan con lai>.mp4" — xoa du an xoa theo tien to "<projectId>/" la xoa luon),
    roi phat video.prepared (thanh cong / file hong) → project-svc moi cho publish / duyet;
  - luc phat: bo dem dia → MinIO → (video nap truoc ban nay) chuan hoa tai cho roi ghi len MinIO.
Khong co DB: viec giao lai thay da co tren MinIO → chi phat lai video.prepared (project-svc ghi de, vo hai).
"""

import asyncio
import json
import logging
import os
import tempfile
import uuid
from pathlib import Path, PurePosixPath

import aio_pika

from app import watermark_av
from app.config import settings
from app.contracts import DatasetIngested, VideoPrepared
from app.messaging import envelope as env_mod
from app.messaging.envelope import LoiHopDong
from app.storage import KhongCoFile, kho

log = logging.getLogger(__name__)

PHIEN_BAN = "chuan-v2"
TEN_QUEUE = "media-svc.dataset-ingested"
TEN_VIEC = "media-svc.chuan-hoa"
# Mot viec chuan hoa video dai co the lau hon han ack mac dinh cua RabbitMQ (30 phut).
HAN_VIEC_MS = 4 * 3600 * 1000


def khoa_ban_chuan(storage_key: str) -> str:
    """Khoa MinIO cua ban chuan — cung tien to du an voi file goc."""
    du_an, _, con_lai = storage_key.partition("/")
    if not con_lai:
        return f"_wm/{PHIEN_BAN}/{storage_key}.mp4"
    return f"{du_an}/_wm/{PHIEN_BAN}/{con_lai}.mp4"


def _tam(duoi: str) -> str:
    fd, duong = tempfile.mkstemp(prefix="media-chuan-", suffix=duoi)
    os.close(fd)
    return duong


def chuan_hoa_va_luu(storage_key: str, dich: Path) -> None:
    """Tai file goc → chuan hoa vao `dich` → ghi len MinIO."""
    goc = _tam(PurePosixPath(storage_key).suffix)
    try:
        kho().tai_ve(storage_key, goc)
        watermark_av.chuan_hoa_video(Path(goc), dich)
    finally:
        os.unlink(goc)
    kho().tai_len(khoa_ban_chuan(storage_key), str(dich), "video/mp4")


def tao_ban_chuan(storage_key: str, dich: Path) -> None:
    """Cho bo dem dia: lay ban da co tren MinIO; chua co thi chuan hoa tai cho."""
    try:
        kho().tai_ve(khoa_ban_chuan(storage_key), str(dich))
        return
    except KhongCoFile:
        pass
    log.info("Chua co ban chuan cua %s tren MinIO — chuan hoa tai cho", storage_key)
    chuan_hoa_va_luu(storage_key, dich)


def chuan_hoa_san(storage_key: str) -> None:
    """Da co tren MinIO thi thoi. Nem LoiMedia (file hong), KhongCoFile (file goc da xoa)."""
    try:
        kho().thong_tin(khoa_ban_chuan(storage_key))
        return
    except KhongCoFile:
        pass
    dich = Path(_tam(".mp4"))
    try:
        chuan_hoa_va_luu(storage_key, dich)
        log.info("Da chuan hoa san video %s", storage_key)
    finally:
        dich.unlink(missing_ok=True)


def cac_viec(p: DatasetIngested) -> list[str]:
    """Moi FILE video mot viec (cac doan cat tu mot file dung chung khoa)."""
    ra: list[str] = []
    for s in p.samples:
        if s.modality == "video" and s.storage_key and s.storage_key not in ra:
            ra.append(s.storage_key)
    return ra


class ChuanHoaSan:
    """Consumer dataset.ingested → queue viec ben → worker chuan hoa lan luot (khong chiem het CPU)."""

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
        await nhan.bind(self._exchange, routing_key=DatasetIngested.EVENT_TYPE)
        await kenh.declare_queue(TEN_VIEC, durable=True, arguments={
            **dlx_args, "x-dead-letter-routing-key": TEN_VIEC, "x-consumer-timeout": HAN_VIEC_MS})
        await nhan.consume(self._nhan)

        # Kenh rieng cho worker: moi lan MOT viec.
        kenh_viec = await ket_noi.channel()
        await kenh_viec.set_qos(prefetch_count=1)
        viec = await kenh_viec.get_queue(TEN_VIEC)
        await viec.consume(self._lam)
        log.info("Consumer %s dang nghe %s; worker %s san sang", TEN_QUEUE, DatasetIngested.EVENT_TYPE, TEN_VIEC)

    async def _nhan(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            env = env_mod.doc(message.body, DatasetIngested)
        except LoiHopDong as ex:
            log.error("Queue %s: message sai hop dong → DLQ: %s", TEN_QUEUE, ex)
            await message.reject(requeue=False)
            return
        try:
            assert self._kenh_nhan is not None
            for key in cac_viec(env.payload):
                than = json.dumps({"projectId": str(env.payload.project_id), "storageKey": key,
                                   "correlationId": str(env.correlation_id), "causationId": str(env.event_id)})
                await self._kenh_nhan.default_exchange.publish(
                    aio_pika.Message(than.encode(), content_type="application/json",
                                     delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
                    routing_key=TEN_VIEC)
        except Exception:  # noqa: BLE001
            log.exception("Queue %s: khong xep duoc viec chuan hoa, tra lai hang doi", TEN_QUEUE)
            await message.nack(requeue=True)
            return
        await message.ack()

    async def _lam(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            v = json.loads(message.body)
            du_an, key = uuid.UUID(v["projectId"]), str(v["storageKey"])
            tuong_quan, nguyen_nhan = uuid.UUID(v["correlationId"]), uuid.UUID(v["causationId"])
        except (ValueError, KeyError, TypeError) as ex:
            log.error("Viec chuan hoa sai dinh dang → DLQ: %s", ex)
            await message.reject(requeue=False)
            return

        loi: str | None = None
        try:
            await asyncio.to_thread(chuan_hoa_san, key)
        except KhongCoFile:
            log.info("File %s khong con (du an da xoa?) — bo qua", key)
            await message.ack()
            return
        except watermark_av.LoiMedia as ex:
            log.warning("Video %s hong, khong chuan hoa duoc: %s", key, ex)
            loi = str(ex)[:500]
        except Exception:  # noqa: BLE001 — MinIO / dia: thu lai (qua x-delivery-limit thi vao DLQ)
            log.exception("Chuan hoa video %s that bai, tra lai hang doi", key)
            await message.nack(requeue=True)
            return

        try:
            await self._phat(VideoPrepared(project_id=du_an, storage_key=key, succeeded=loi is None, error=loi),
                             tuong_quan, nguyen_nhan)
        except Exception:  # noqa: BLE001
            log.exception("Khong phat duoc video.prepared cho %s, tra lai hang doi", key)
            await message.nack(requeue=True)
            return
        await message.ack()

    async def _phat(self, payload: VideoPrepared, tuong_quan: uuid.UUID, nguyen_nhan: uuid.UUID) -> None:
        assert self._exchange is not None
        env = env_mod.tao(payload, settings().service_name, tuong_quan, nguyen_nhan)
        await self._exchange.publish(
            aio_pika.Message(env.to_json().encode("utf-8"), content_type="application/json", type=VideoPrepared.EVENT_TYPE,
                             message_id=str(env.event_id), delivery_mode=aio_pika.DeliveryMode.PERSISTENT),
            routing_key=VideoPrepared.EVENT_TYPE)
