"""Diem vao ml-svc (kien truc 3.19): nhung cau cho quality-svc cham cau tra loi mo theo nghia.

Ranh gioi (VD-A-04): ml-svc chi TINH vector, khong bao gio quyet dinh nhan / ket qua; sap thi
quality-svc cho roi tu ha xuong so chu — luong lam bai, cham, tra tien khong phu thuoc ml-svc.

Chay dev:  .venv/Scripts/python -m app.main     (cong 8202)
"""

import asyncio
import logging
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI

from app import db
from app.api import router
from app.config import settings
from app.embedder import MoHinhFastEmbed

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
log = logging.getLogger("ml-svc")


@asynccontextmanager
async def lifespan(app: FastAPI):
    cfg = settings()
    await db.chay_migration()
    app.state.kho = db.KhoPostgres()
    app.state.mo_hinh = MoHinhFastEmbed(cfg.model_name, cfg.model_cache_dir)
    # Nap mo hinh NGAY (nen) de yeu cau dau tien khong phai cho ~20 giay.
    nap = asyncio.create_task(asyncio.to_thread(app.state.mo_hinh.nap), name="nap-mo-hinh")
    log.info("ml-svc san sang tren cong %s (mo hinh %s)", cfg.port, cfg.model_name)
    try:
        yield
    finally:
        nap.cancel()
        await db.dong()


app = FastAPI(title="ml-svc", lifespan=lifespan)
app.include_router(router)


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=settings().port, log_config=None)
