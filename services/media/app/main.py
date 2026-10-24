"""Diem vao media-svc: phat file mau qua token + watermark chim. KHONG co database.

Chay dev:  .venv/Scripts/python -m app.main     (cong 8109)
"""

import asyncio
import logging
from contextlib import asynccontextmanager

import aio_pika
import uvicorn
from fastapi import FastAPI

from app import settings_listener, settings_store
from app.ban_chuan import ChuanHoaSan
from app.api import router
from app.config import settings
from app.errors import LoiApi, xu_ly_loi_api
from app.grant import doc_khoa

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
log = logging.getLogger("media-svc")


async def noi_rabbitmq(cfg) -> aio_pika.abc.AbstractRobustConnection:
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
    # Khoa sai thi dung ngay luc khoi dong, khong doi toi request dau tien.
    doc_khoa(cfg.grant_key)
    doc_khoa(cfg.watermark_key)

    # Setting (bat / tat, do manh watermark): queue tam, giu trong bo nho. Chua co thi dung mac dinh catalog.
    ket_noi = await noi_rabbitmq(cfg)
    await settings_listener.bat_dau(ket_noi)
    # Video vua nap: chuan hoa san (ma hoa lai ca video), xong phat video.prepared cho project-svc.
    await ChuanHoaSan().bat_dau(ket_noi)
    log.info("media-svc san sang tren cong %s", cfg.port)
    try:
        yield
    finally:
        await ket_noi.close()


app = FastAPI(title="media-svc", lifespan=lifespan)
app.add_exception_handler(LoiApi, xu_ly_loi_api)
app.include_router(router)


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=settings().port, log_config=None)
