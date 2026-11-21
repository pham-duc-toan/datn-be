"""Diem vao ml-svc (kien truc 3.19): nhung cau cho quality-svc cham cau tra loi mo theo nghia.

Ranh gioi (VD-A-04): ml-svc chi TINH vector, khong bao gio quyet dinh nhan / ket qua; sap thi
quality-svc cho roi tu ha xuong so chu — luong lam bai, cham, tra tien khong phu thuoc ml-svc.

Chay dev:  .venv/Scripts/python -m app.main     (cong 8202)
"""

import asyncio
import logging
from app.log_json import cau_hinh_log
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI

from app import db
from app.api import router
from app.config import settings
from app.embedder import MoHinhFastEmbed

cau_hinh_log()
log = logging.getLogger("ml-svc")


@asynccontextmanager
async def lifespan(app: FastAPI):
    cfg = settings()
    await db.chay_migration()
    app.state.kho = db.KhoPostgres()
    app.state.mo_hinh = MoHinhFastEmbed(cfg.model_name, cfg.model_cache_dir)
    # Nap mo hinh NGAY (nen) de yeu cau dau tien khong phai cho ~20 giay.
    nap = asyncio.create_task(asyncio.to_thread(app.state.mo_hinh.nap), name="nap-mo-hinh")
    hoc_dem = asyncio.create_task(_hoc_goi_y_moi_dem(), name="hoc-goi-y-moi-dem")
    log.info("ml-svc san sang tren cong %s (mo hinh %s)", cfg.port, cfg.model_name)
    try:
        yield
    finally:
        nap.cancel()
        hoc_dem.cancel()
        await db.dong()


async def _hoc_goi_y_moi_dem() -> None:
    """Moi ngay mot lan vao ML_RECO_TRAIN_HOUR_UTC: hoc mo hinh goi y (kien truc 3.39). Loi → log, dem sau thu lai."""
    from datetime import datetime, timedelta, timezone

    from app import reco_job

    cfg = settings()
    if cfg.reco_train_hour_utc < 0 or not cfg.reco_clickhouse_url:
        return
    while True:
        bay_gio = datetime.now(timezone.utc)
        ke = bay_gio.replace(hour=cfg.reco_train_hour_utc, minute=0, second=0, microsecond=0)
        if ke <= bay_gio:
            ke = ke + timedelta(days=1)
        await asyncio.sleep((ke - bay_gio).total_seconds())
        try:
            await asyncio.to_thread(reco_job.chay_mot_lan, cfg)
        except Exception:  # noqa: BLE001 — viec nen, khong duoc lam chet ml-svc
            log.exception("Hoc mo hinh goi y loi — dem sau thu lai")


app = FastAPI(title="ml-svc", lifespan=lifespan)
app.include_router(router)


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=settings().port, log_config=None)
