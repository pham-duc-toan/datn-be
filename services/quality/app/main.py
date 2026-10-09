"""Diem vao quality-svc: FastAPI + consumer RabbitMQ + outbox + lo Dawid-Skene.

Chay dev:  .venv/Scripts/python -m app.main     (cong 8201)
"""

import asyncio
import logging
from contextlib import asynccontextmanager

import aio_pika
import uvicorn
from fastapi import FastAPI

from app import db, ds_batch, processors, seeding, settings_store, settings_sync
from app.api import router
from app.config import settings
from app.contracts import (
    AnnotationSubmitted, GoldAnswered, ProjectPublished, SettingChanged, SettingsSnapshot, TaskRedundancyReached,
)
from app.errors import LoiApi, xu_ly_loi_api
from app.messaging.consumer import Consumer
from app.messaging.outbox import Dispatcher

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
log = logging.getLogger("quality-svc")

# Ten queue theo quy uoc "<service>.<event>" giong cac service C#.
CONSUMERS = [
    Consumer("quality-svc.project-published", ProjectPublished, processors.project_published),
    Consumer("quality-svc.annotation-submitted", AnnotationSubmitted, processors.annotation_submitted),
    Consumer("quality-svc.task-redundancy-reached", TaskRedundancyReached, processors.task_redundancy_reached),
    Consumer("quality-svc.gold-answered", GoldAnswered, processors.gold_answered),
]

# Setting dong (admin-svc) — bat dau TRUOC cac consumer nghiep vu.
CONSUMERS_SETTING = [
    Consumer("quality-svc.setting-changed", SettingChanged, settings_sync.setting_changed),
    Consumer("quality-svc.settings-snapshot", SettingsSnapshot, settings_sync.settings_snapshot),
]


async def noi_rabbitmq(cfg) -> aio_pika.abc.AbstractRobustConnection:
    """Lan noi DAU TIEN: connect_robust chi tu noi lai SAU khi da noi duoc, nen broker chua
    san sang luc khoi dong (vua tao lai container) thi phai tu thu lai — giong EventConsumer C#."""
    while True:
        try:
            return await aio_pika.connect_robust(
                host=cfg.rabbit_host, port=cfg.rabbit_port, login=cfg.rabbit_user, password=cfg.rabbit_password,
                virtualhost=cfg.rabbit_vhost, client_properties={"connection_name": cfg.service_name})
        except (aio_pika.exceptions.AMQPConnectionError, OSError) as ex:
            cho = settings_store.giay(settings_store.CONSUMERS_RECONNECT_DELAY)
            log.warning("Chua noi duoc RabbitMQ (%s), thu lai sau %ss", ex, cho)
            await asyncio.sleep(cho)


@asynccontextmanager
async def lifespan(app: FastAPI):
    cfg = settings()
    await db.chay_migration()

    # Setting: nap ban sao trong DB truoc moi viec khac (prefetch, chu ky DS... doc tu day),
    # roi xin admin-svc phat lai toan bo — lo mat event luc tat van dong bo lai.
    so = await settings_sync.nap_tu_db()
    log.info("Nap %s setting tu settings_replica", so)
    await settings_sync.xin_snapshot()

    await seeding.chay()

    ket_noi = await noi_rabbitmq(cfg)
    for c in CONSUMERS_SETTING + CONSUMERS:
        await c.bat_dau(ket_noi)

    dung = asyncio.Event()
    viec_nen = [
        asyncio.create_task(Dispatcher().chay(dung), name="outbox"),
        asyncio.create_task(ds_batch.vong_lap(dung), name="dawid-skene"),
        asyncio.create_task(settings_sync.vong_lap_nap_lai(dung), name="settings-reload"),
    ]
    log.info("quality-svc san sang tren cong %s", cfg.port)
    try:
        yield
    finally:
        dung.set()
        await asyncio.gather(*viec_nen, return_exceptions=True)
        await ket_noi.close()
        await db.dong()


app = FastAPI(title="quality-svc", lifespan=lifespan)
app.add_exception_handler(LoiApi, xu_ly_loi_api)
app.include_router(router)


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=settings().port, log_config=None)
