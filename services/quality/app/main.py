"""Diem vao quality-svc: FastAPI + consumer RabbitMQ + outbox + worker viec cho (gRPC project-svc: gian lan, cham
lai, Dawid-Skene sau khi cham, uy tin) + gRPC LabelAggregation (project-svc nho gop nhan, 3.24).

Chay dev:  .venv/Scripts/python -m app.main     (cong 8201 REST, 9201 gRPC)
"""

import asyncio
import logging
from app.log_json import cau_hinh_log
from contextlib import asynccontextmanager

import aio_pika
import uvicorn
from fastapi import FastAPI

from app import (
    cleanup, db, grpc_server, phan_tich_mo, processors, project_client, seeding, settings_store, settings_sync,
    viec_cho,
)
from app.api import router
from app.config import settings
from app.contracts import (
    AttemptSubmitted, ProjectCancelled, ProjectCollectionFinished, ProjectCompleted,
)
from app.errors import LoiApi, xu_ly_khong_goi_duoc, xu_ly_loi_api
from app.messaging.consumer import Consumer
from app.messaging.outbox import Dispatcher

cau_hinh_log()
log = logging.getLogger("quality-svc")

# Ten queue theo quy uoc "<service>.<event>" giong cac service C#. Khong nghe project.published nua: de bai
# doc qua gRPC tu project-svc luc can (3.22) — queue cu quality-svc.project-published phai xoa tren broker.
CONSUMERS = [
    # Moc dong du an — sau project.closed_data_retention thi chup bao cao roi xoa ket qua trung gian (archive.py).
    Consumer("quality-svc.project-completed", ProjectCompleted, processors.project_dong),
    Consumer("quality-svc.project-cancelled", ProjectCancelled, processors.project_dong),
    # Du an lam trung tam (docs 3.18): bai nop → hang cho (xet gian lan FM-07, cau mo), dong thu bai → cham.
    Consumer("quality-svc.attempt-submitted", AttemptSubmitted, processors.attempt_submitted),
    Consumer("quality-svc.collection-finished", ProjectCollectionFinished, processors.collection_finished),
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
    # Role chi doc view cho service khac (identity_reader doc v_reputations — kien truc 3.32).
    await db.dam_bao_role_doc()

    # Setting (kien truc 3.32): WARM-UP — doc view v_settings cua admin_db TRUOC moi viec khac (prefetch, chu ky
    # viec cho... doc tu day). admin_db chua tra loi thi cho, chua nhan viec nao.
    await settings_sync.warm_up()

    await seeding.chay()

    ket_noi = await noi_rabbitmq(cfg)
    for c in CONSUMERS:
        await c.bat_dau(ket_noi)

    dung = asyncio.Event()
    viec_nen = [
        asyncio.create_task(Dispatcher().chay(dung), name="outbox"),
        asyncio.create_task(settings_sync.vong_lap_nap_lai(dung), name="settings-reload"),
        asyncio.create_task(cleanup.vong_lap(dung), name="don-du-lieu-qua-han"),
        # Cau tra loi mo (3.19): nhung cau qua ml-svc.
        asyncio.create_task(phan_tich_mo.vong_lap(dung), name="phan-tich-cau-mo"),
        # Viec can doc project-svc qua gRPC (3.22): xet gian lan, cham lai du an chua cham xong, Dawid-Skene
        # (mot lan sau khi cham), uy tin.
        asyncio.create_task(viec_cho.vong_lap(dung), name="viec-cho"),
    ]
    may_grpc = await grpc_server.bat_dau()
    log.info("quality-svc san sang tren cong %s", cfg.port)
    try:
        yield
    finally:
        if may_grpc is not None:
            await may_grpc.stop(grace=5)
        dung.set()
        await asyncio.gather(*viec_nen, return_exceptions=True)
        await ket_noi.close()
        await project_client.dong()
        await settings_sync.dong()
        await db.dong()


app = FastAPI(title="quality-svc", lifespan=lifespan)
app.add_exception_handler(LoiApi, xu_ly_loi_api)
app.add_exception_handler(project_client.KhongGoiDuoc, xu_ly_khong_goi_duoc)
app.include_router(router)


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=settings().port, log_config=None)
