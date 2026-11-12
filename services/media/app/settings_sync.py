"""Setting dong cua admin-svc cho media-svc (khong co DB rieng) — ban Python cua SettingsSource / SettingsWarmup (C#).

Kien truc 3.32: doc THANG view v_settings cua admin_db (role settings_reader — chi SELECT duoc view nay) vao bo nho
(settings_store), thay cho queue tam nghe setting.changed / settings.snapshot.
  - warm_up(): luc khoi dong, TRUOC khi nhan viec — admin_db chua tra loi thi thu lai mai.
  - vong_lap_nap_lai(): doc lai moi settings.reload_interval.
"""

import asyncio
import json
import logging

import asyncpg

from app import settings_store
from app.config import settings

log = logging.getLogger(__name__)

CHO_THU_LAI_GIAY = 2
CHU_KY = "settings.reload_interval"

_pool: asyncpg.Pool | None = None


async def _lay_pool() -> asyncpg.Pool:
    global _pool
    if _pool is None:
        _pool = await asyncpg.create_pool(settings().settings_database_url, min_size=1, max_size=2)
    return _pool


async def nap_tu_admin() -> int:
    pool = await _lay_pool()
    async with pool.acquire() as conn:
        dong = await conn.fetch("SELECT key, value::text AS value, version FROM v_settings")
    for d in dong:
        if settings_store.kiem_gia_tri(d["key"], json.loads(d["value"])) is not None:
            continue
        # Moi setting he thong deu doc; chi ghi log khoa media-svc thuc su dung.
        if settings_store.ap_dung(d["key"], d["value"], d["version"]) and d["key"].startswith("media."):
            log.info("Setting %s = %s (v%s)", d["key"], d["value"], d["version"])
    return len(dong)


async def _dong_pool() -> None:
    global _pool
    if _pool is not None:
        await _pool.close()
        _pool = None


async def warm_up() -> None:
    lan = 0
    while True:
        lan += 1
        try:
            so = await nap_tu_admin()
            log.info("Nap %s setting tu admin-svc (lan thu %s)", so, lan)
            return
        except Exception as ex:  # noqa: BLE001 — admin_db chua san sang: chua nhan viec, thu lai
            await _dong_pool()
            log.warning("Chua doc duoc setting tu admin-svc (lan %s): %s — thu lai sau %ss", lan, ex, CHO_THU_LAI_GIAY)
            await asyncio.sleep(CHO_THU_LAI_GIAY)


async def vong_lap_nap_lai(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            await asyncio.wait_for(dung.wait(), timeout=settings_store.giay(CHU_KY))
            break
        except asyncio.TimeoutError:
            pass
        try:
            await nap_tu_admin()
        except Exception:  # noqa: BLE001
            log.exception("Doc lai setting tu admin-svc that bai — giu gia tri dang co, thu lai vong sau")


async def dong() -> None:
    await _dong_pool()
