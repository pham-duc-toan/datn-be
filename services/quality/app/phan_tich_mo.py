"""Worker phan tich cau tra loi mo (kien truc 3.19).

Moi vong (quality.freetext_analysis_interval):
  1. NHUNG: cau da tra loi cua bai nop (labels) thuoc du an co cong cu freeText, chua cham, chua co vector
     → gui ml-svc theo lo, ghi freetext_vectors. Chay NGAY khi bai toi (khong doi dong thu bai), nen luc
     du an ket thuc gan nhu da du; ml-svc sap thi vong sau thu lai.
  2. CHAM LAI: du an dang cho (project_collections.freetext_state = pending) → thu_cham trong transaction
     rieng (cung khoa theo du an voi consumer). thu_cham tu quyet: du vector → so nghia; qua han → so chu.

Khong lam hong gi khi chay nhieu instance: ghi vector ON CONFLICT DO NOTHING, cham xep hang qua khoa du an
va graded_at.
"""

import asyncio
import json
import logging
import uuid
from types import SimpleNamespace

from sqlalchemy import text

from app import cau_mo, ml_client, processors, settings_store
from app.db import session_factory

log = logging.getLogger(__name__)

LO_CAU = 64          # cau gui ml-svc moi lan
LO_MOI_VONG = 20     # toi da so lo moi vong (vong sau lam tiep)

_CO_CAU_MO = "jsonb_path_exists(p.label_schema, '$.tools[*] ? (@.kind == \"freeText\")')"


async def nhung_mot_lo() -> int:
    """Nhung mot lo cau chua co vector. Tra so cau (bai x mau) da xu ly; 0 = het viec."""
    async with session_factory()() as s:
        dong = (await s.execute(text(
            "SELECT l.attempt_id, l.sample_id, l.project_id, l.data, p.label_schema "
            "FROM labels l JOIN projects p ON p.project_id = l.project_id "
            "LEFT JOIN project_collections c ON c.project_id = l.project_id "
            f"WHERE l.attempt_id IS NOT NULL AND c.graded_at IS NULL AND {_CO_CAU_MO} "
            "AND NOT EXISTS (SELECT 1 FROM freetext_vectors v WHERE v.attempt_id = l.attempt_id AND v.sample_id = l.sample_id) "
            "ORDER BY l.received_at LIMIT :n"), {"n": LO_CAU})).all()
    if not dong:
        return 0

    # Moi (bai, mau, cong cu freeText) mot dong; cau rong → vector NULL (khong goi ml-svc).
    muc: list[tuple[uuid.UUID, uuid.UUID, uuid.UUID, str, str]] = []
    for d in dong:
        schema = d.label_schema if isinstance(d.label_schema, dict) else json.loads(d.label_schema)
        du_lieu = d.data if isinstance(d.data, dict) else json.loads(d.data)
        for ten in processors.cong_cu_mo(schema):
            muc.append((d.attempt_id, d.sample_id, d.project_id, ten, cau_mo.van_ban(du_lieu.get(ten))))

    can = [m for m in muc if m[4]]
    vector: dict[int, list[float]] = {}
    mo_hinh = None
    if can:
        mo_hinh, vs = await ml_client.nhung([m[4] for m in can])
        vector = {id(m): v for m, v in zip(can, vs)}

    async with session_factory()() as s, s.begin():
        for m in muc:
            v = vector.get(id(m))
            await s.execute(text(
                "INSERT INTO freetext_vectors (attempt_id, sample_id, tool, project_id, model, vector) "
                "VALUES (:a, :sm, :t, :p, :m, :v) ON CONFLICT DO NOTHING"),
                {"a": m[0], "sm": m[1], "t": m[3], "p": m[2], "m": mo_hinh if v is not None else None, "v": v})
    return len(dong)


async def cham_lai_du_an_cho() -> int:
    async with session_factory()() as s:
        cho = (await s.execute(text(
            "SELECT project_id, freetext_correlation_id FROM project_collections "
            "WHERE freetext_state = 'pending' AND graded_at IS NULL ORDER BY finished_at"))).all()
    da_cham = 0
    for d in cho:
        nguon = SimpleNamespace(correlation_id=d.freetext_correlation_id or uuid.uuid4(), event_id=None)
        async with session_factory()() as s, s.begin():
            await processors.thu_cham(s, d.project_id, nguon)
        async with session_factory()() as s:
            if (await s.execute(text("SELECT graded_at FROM project_collections WHERE project_id = :p"),
                                {"p": d.project_id})).scalar() is not None:
                da_cham += 1
    return da_cham


async def mot_vong() -> None:
    if ml_client.co_cau_hinh():
        try:
            for _ in range(LO_MOI_VONG):
                if await nhung_mot_lo() < LO_CAU:
                    break
        except Exception as ex:  # noqa: BLE001 — ml-svc sap / cham: vong sau thu lai, qua han thi so chu
            log.warning("Chua nhung duoc cau tra loi mo (ml-svc): %s", ex)
    so = await cham_lai_du_an_cho()
    if so:
        log.info("Cham %s du an sau khi phan tich cau tra loi mo", so)


async def vong_lap(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            await mot_vong()
        except Exception:  # noqa: BLE001 — khong duoc lam chet service
            log.exception("Phan tich cau tra loi mo that bai, thu lai vong sau")
        if await settings_store.cho_theo_setting(settings_store.FREETEXT_ANALYSIS_INTERVAL, dung):
            break
