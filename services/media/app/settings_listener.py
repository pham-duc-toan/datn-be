"""Dong bo setting KHONG database — ban Python cua Crowd.Settings.SettingsInMemoryListener (gateway).

Nghe setting.changed / settings.snapshot qua mot queue TAM cua rieng tien trinh nay (broker dat ten,
tu xoa khi ngat ket noi), ghi thang vao bo nho (settings_store). Luc noi thi xin admin-svc phat lai
toan bo; mat message nay thi lan snapshot dinh ky cua admin-svc bu lai. Khong can chong xu ly trung:
settings_store chi ghi khi version moi hon.
"""

import json
import logging
import uuid

import aio_pika

from app import settings_store
from app.config import settings
from app.contracts import SettingChanged, SettingsSnapshot, SettingsSnapshotRequested
from app.messaging import envelope as env_mod
from app.messaging.envelope import LoiHopDong

log = logging.getLogger(__name__)


def _ap_dung(key: str, gia_tri: object, version: int) -> None:
    # Snapshot chua moi setting he thong; chi ghi log khoa media-svc thuc su dung.
    if settings_store.ap_dung(key, json.dumps(gia_tri), version) and key.startswith("media."):
        log.info("Setting %s = %s (v%s)", key, json.dumps(gia_tri), version)


async def _nhan(message: aio_pika.abc.AbstractIncomingMessage) -> None:
    async with message.process(ignore_processed=True):
        try:
            if message.routing_key == SettingChanged.EVENT_TYPE:
                p = env_mod.doc(message.body, SettingChanged).payload
                _ap_dung(p.key, p.value, p.setting_version)
            elif message.routing_key == SettingsSnapshot.EVENT_TYPE:
                for i in env_mod.doc(message.body, SettingsSnapshot).payload.items:
                    _ap_dung(i.key, i.value, i.setting_version)
        except LoiHopDong as ex:
            log.error("Message setting sai hop dong — bo qua: %s", ex)


async def bat_dau(ket_noi: aio_pika.abc.AbstractRobustConnection) -> None:
    cfg = settings()
    kenh = await ket_noi.channel()
    exchange = await kenh.declare_exchange(cfg.exchange, aio_pika.ExchangeType.TOPIC, durable=True)
    queue = await kenh.declare_queue("", exclusive=True, auto_delete=True)
    await queue.bind(exchange, routing_key=SettingChanged.EVENT_TYPE)
    await queue.bind(exchange, routing_key=SettingsSnapshot.EVENT_TYPE)
    await queue.consume(_nhan)

    xin = env_mod.tao(SettingsSnapshotRequested(service=cfg.service_name), cfg.service_name, uuid.uuid4(), None)
    await exchange.publish(
        aio_pika.Message(body=xin.to_json().encode("utf-8"), content_type="application/json",
                         type=SettingsSnapshotRequested.EVENT_TYPE),
        routing_key=SettingsSnapshotRequested.EVENT_TYPE)
    log.info("Dang nghe setting (khong DB), da xin admin-svc phat lai")
