"""Dong bo setting tu admin-svc — ban Python cua SettingsReplicaSync + SettingsProcessors (C#).

  setting.changed / settings.snapshot → ghi bang settings_replica (chi khi version
  MOI hon) roi cap nhat bo nho (settings_store).
  Luc khoi dong: nap settings_replica vao bo nho, roi xin admin-svc phat lai toan
  bo (settings.snapshot_requested) — lo mat event luc tat van dong bo lai duoc.
  Dinh ky nap lai tu DB (nhieu ban sao quality cung doc mot DB).
"""

import asyncio
import json
import logging
import uuid

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import settings_store
from app.config import settings
from app.contracts import SettingChanged, SettingsSnapshot, SettingsSnapshotRequested
from app.db import session_factory
from app.messaging import outbox
from app.messaging.envelope import Envelope

log = logging.getLogger(__name__)


async def _ghi(s: AsyncSession, key: str, gia_tri: object, version: int) -> None:
    gia_tri_json = json.dumps(gia_tri)
    if settings_store.kiem_gia_tri(key, gia_tri) is not None:
        log.warning("Bo qua setting %s = %s: khong hop le voi catalog cua quality-svc", key, gia_tri_json)
        return

    await s.execute(
        text(
            "INSERT INTO settings_replica (key, value, version, updated_at) "
            "VALUES (:k, CAST(:v AS jsonb), :ver, now()) "
            "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, version = EXCLUDED.version, updated_at = now() "
            "WHERE settings_replica.version < EXCLUDED.version"
        ),
        {"k": key, "v": gia_tri_json, "ver": version},
    )
    if settings_store.ap_dung(key, gia_tri_json, version):
        log.info("Setting %s = %s (v%s)", key, gia_tri_json, version)


async def setting_changed(s: AsyncSession, env: Envelope[SettingChanged]) -> None:
    p = env.payload
    await _ghi(s, p.key, p.value, p.setting_version)


async def settings_snapshot(s: AsyncSession, env: Envelope[SettingsSnapshot]) -> None:
    for item in env.payload.items:
        await _ghi(s, item.key, item.value, item.setting_version)


async def nap_tu_db() -> int:
    async with session_factory()() as s:
        dong = (await s.execute(text("SELECT key, value::text AS value, version FROM settings_replica"))).all()
    so = 0
    for d in dong:
        if settings_store.ap_dung(d.key, d.value, d.version):
            so += 1
    return so


async def xin_snapshot() -> None:
    async with session_factory()() as s, s.begin():
        await outbox.ghi(s, SettingsSnapshotRequested(service=settings().service_name), uuid.uuid4(), None)


async def vong_lap_nap_lai(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            await asyncio.wait_for(dung.wait(), timeout=settings_store.giay(settings_store.SETTINGS_RELOAD_INTERVAL))
        except asyncio.TimeoutError:
            pass
        if dung.is_set():
            break
        try:
            await nap_tu_db()
        except Exception:  # noqa: BLE001
            log.exception("Nap lai setting tu DB that bai, thu lai vong sau")
