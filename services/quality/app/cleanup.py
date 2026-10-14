"""Don du lieu qua han — ban Python cua EventStoreCleanupWorker (C#), them phan rieng cua quality.

  - outbox: event DA GUI qua outbox.retention (event chua gui khong bao gio xoa);
  - processed_events: dau vet chong xu ly trung qua consumers.processed_retention;
  - fraud_submissions: luot nop dung xet gian lan qua quality.fraud_submission_retention
    (thuat toan chi can vai chuc luot gan nhat va cua so chung IP);
  - du an da dong qua project.closed_data_retention: chup bao cao roi xoa nhan tho (archive.py).

Xoa theo LO voi FOR UPDATE SKIP LOCKED: khong giu khoa lau, khong tranh voi dispatcher / consumer
(hai ben dung vao dong MOI), chay nhieu ban sao khong xoa trung. Moi vong toi da LO_MOI_VONG lo.
"""

import asyncio
import logging
from datetime import datetime, timedelta, timezone
from typing import Any

from sqlalchemy import text

from app import settings_store
from app.db import session_factory

log = logging.getLogger(__name__)

KICH_THUOC_LO = 2000
LO_MOI_VONG = 25

# (ten bang, khoa chinh de chon lo, dieu kien qua han, setting thoi gian giu)
BANG = [
    ("outbox", "id", "published_at IS NOT NULL AND published_at < :han", settings_store.OUTBOX_RETENTION),
    ("processed_events", "event_id, handler", "processed_at < :han", settings_store.CONSUMERS_PROCESSED_RETENTION),
    ("fraud_submissions", "assignment_id", "submitted_at < :han", settings_store.FRAUD_SUBMISSION_RETENTION),
]


async def don_mot_bang(bang: str, khoa: str, dieu_kien: str, tham_so: dict[str, Any]) -> tuple[int, bool]:
    """Xoa cac dong khop dieu_kien, toi da LO_MOI_VONG lo. Tra ve (so dong da xoa, da het chua)."""
    tong = 0
    for _ in range(LO_MOI_VONG):
        async with session_factory()() as s, s.begin():
            so = (await s.execute(
                text(f"DELETE FROM {bang} WHERE ({khoa}) IN ("
                     f"SELECT {khoa} FROM {bang} WHERE {dieu_kien} LIMIT :n FOR UPDATE SKIP LOCKED)"),
                dict(tham_so, n=KICH_THUOC_LO),
            )).rowcount
        tong += so
        if so < KICH_THUOC_LO:
            return tong, True
    return tong, False


async def don_mot_vong() -> dict[str, int]:
    bay_gio = datetime.now(timezone.utc)
    kq = {}
    for bang, khoa, dieu_kien, setting in BANG:
        han = bay_gio - timedelta(seconds=settings_store.giay(setting))
        so, _ = await don_mot_bang(bang, khoa, dieu_kien, {"han": han})
        if so:
            kq[bang] = so
    return kq


async def vong_lap(dung: asyncio.Event) -> None:
    # Nhap o day (khong o dau file): archive dung don_mot_bang cua module nay.
    from app import archive

    while not dung.is_set():
        try:
            kq = await don_mot_vong()
            if kq:
                log.info("Don du lieu qua han: %s", kq)
        except Exception:  # noqa: BLE001 — don rac hong khong duoc lam chet service
            log.exception("Don du lieu qua han that bai, thu lai vong sau")
        try:
            so = await archive.don_mot_vong()
            if so:
                log.info("Luu tru %s du an da dong qua han", so)
        except Exception:  # noqa: BLE001
            log.exception("Luu tru du an da dong that bai, thu lai vong sau")
        if await settings_store.cho_theo_setting(settings_store.OUTBOX_CLEANUP_INTERVAL, dung):
            break
