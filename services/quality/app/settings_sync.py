"""Setting dong cua admin-svc — ban Python cua SettingsSource / SettingsWarmup / SettingsReloadWorker (C#).

Kien truc 3.32: doc THANG view v_settings cua admin_db (role settings_reader — chi SELECT duoc view nay) vao bo nho
(settings_store). Khong con bang settings_replica, khong nghe setting.changed / settings.snapshot.
  - warm_up(): goi luc khoi dong, TRUOC consumer / worker — admin_db chua tra loi thi thu lai mai, quality chua nhan
    viec nao (khong chay bang gia tri khoi tao co the sai).
  - vong_lap_nap_lai(): doc lai moi settings.reload_interval (moi instance tu doc).
"""

import asyncio
import json
import logging

import asyncpg

from app import settings_store
from app.config import settings

log = logging.getLogger(__name__)

CHO_THU_LAI_GIAY = 2

_pool: asyncpg.Pool | None = None


async def _lay_pool() -> asyncpg.Pool:
    global _pool
    if _pool is None:
        _pool = await asyncpg.create_pool(settings().settings_database_url, min_size=1, max_size=2)
    return _pool


async def nap_tu_admin() -> int:
    """Nap moi setting (version moi hon moi ghi). Tra so dong doc duoc."""
    pool = await _lay_pool()
    async with pool.acquire() as conn:
        dong = await conn.fetch("SELECT key, value::text AS value, version FROM v_settings")
    for d in dong:
        if settings_store.kiem_gia_tri(d["key"], json.loads(d["value"])) is not None:
            log.warning("Bo qua setting %s = %s: khong hop le voi catalog cua quality-svc", d["key"], d["value"])
            continue
        if settings_store.ap_dung(d["key"], d["value"], d["version"]):
            log.info("Setting %s = %s (v%s)", d["key"], d["value"], d["version"])
    return len(dong)


async def warm_up() -> None:
    lan = 0
    while True:
        lan += 1
        try:
            so = await nap_tu_admin()
            log.info("Nap %s setting tu admin-svc (lan thu %s)", so, lan)
            return
        except Exception as ex:  # noqa: BLE001 — admin_db chua san sang: chua nhan viec, thu lai
            global _pool
            if _pool is not None:
                await _pool.close()
                _pool = None
            log.warning("Chua doc duoc setting tu admin-svc (lan %s): %s — thu lai sau %ss", lan, ex, CHO_THU_LAI_GIAY)
            await asyncio.sleep(CHO_THU_LAI_GIAY)


async def vong_lap_nap_lai(dung: asyncio.Event) -> None:
    while not dung.is_set():
        await settings_store.cho_theo_setting(settings_store.SETTINGS_RELOAD_INTERVAL, dung)
        if dung.is_set():
            break
        try:
            await nap_tu_admin()
        except Exception:  # noqa: BLE001
            log.exception("Doc lai setting tu admin-svc that bai — giu gia tri dang co, thu lai vong sau")


async def dong() -> None:
    global _pool
    if _pool is not None:
        await _pool.close()
        _pool = None
