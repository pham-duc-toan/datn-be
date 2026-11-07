"""Worker nhung cau tra loi mo (kien truc 3.19).

Moi vong (quality.freetext_analysis_interval): bai vua nop con trong pending_attempts (freetext_done_at NULL) →
doc nhan cua bai qua gRPC tu project-svc (3.22 — quality khong giu ban sao nhan), voi moi cong cu freeText cua
tap nhan gui cau tra loi sang ml-svc theo lo, ghi freetext_vectors. Chay NGAY khi bai toi (khong doi dong thu
bai), nen luc du an ket thuc gan nhu da du. Du an khong co cau mo / da cham / ml-svc khong cau hinh → danh dau
xong luon (cham so chu). Viec CHAM LAI du an dang cho phan tich nam o viec_cho.py.

Khong lam hong gi khi chay nhieu instance: chon dong FOR UPDATE SKIP LOCKED, ghi vector ON CONFLICT DO NOTHING.
"""

import asyncio
import logging
import uuid

from sqlalchemy import text

from app import cau_mo, ml_client, processors, project_client, settings_store
from app.db import session_factory

log = logging.getLogger(__name__)

LO_CAU = 64          # cau gui ml-svc moi lan
LO_BAI = 20          # bai moi lo
LO_MOI_VONG = 20     # toi da so lo moi vong (vong sau lam tiep)


async def nhung_mot_lo() -> int:
    """Nhung mot lo bai. Tra so bai da xu ly; 0 = het viec. Loi gRPC / ml-svc → nem ra (vong sau thu lai)."""
    async with session_factory()() as s, s.begin():
        dong = (await s.execute(text(
            "SELECT attempt_id, project_id FROM pending_attempts WHERE freetext_done_at IS NULL "
            "ORDER BY queued_at LIMIT :n FOR UPDATE SKIP LOCKED"), {"n": LO_BAI})).all()
        if not dong:
            return 0

        theo_du_an: dict[uuid.UUID, list[uuid.UUID]] = {}
        for d in dong:
            theo_du_an.setdefault(d.project_id, []).append(d.attempt_id)

        for project_id, bai in theo_du_an.items():
            await _nhung_bai(s, project_id, bai)
            await s.execute(text("UPDATE pending_attempts SET freetext_done_at = now() WHERE attempt_id = ANY(:a)"), {"a": bai})
    return len(dong)


async def _nhung_bai(s, project_id: uuid.UUID, bai: list[uuid.UUID]) -> None:
    da_cham = (await s.execute(text(
        "SELECT 1 FROM project_gradings WHERE project_id = :p AND graded_at IS NOT NULL"), {"p": project_id})).first()
    if da_cham is not None or not ml_client.co_cau_hinh():
        return
    try:
        schema = (await project_client.du_an(project_id)).label_schema
    except project_client.KhongTimThay:
        return
    ten = processors.cong_cu_mo(schema)
    if not ten:
        return

    # Moi (bai, mau, cong cu freeText) mot dong; cau rong → vector NULL (khong goi ml-svc).
    muc: list[tuple[uuid.UUID, uuid.UUID, str, str]] = []
    for n in await project_client.nhan(project_id, bai):
        for t in ten:
            muc.append((n.attempt_id, n.sample_id, t, cau_mo.van_ban(n.data.get(t))))

    can = [m for m in muc if m[3]]
    vector: dict[int, list[float]] = {}
    mo_hinh = None
    for i in range(0, len(can), LO_CAU):
        lo = can[i:i + LO_CAU]
        mo_hinh, vs = await ml_client.nhung([m[3] for m in lo])
        vector.update({id(m): v for m, v in zip(lo, vs)})

    for m in muc:
        v = vector.get(id(m))
        await s.execute(text(
            "INSERT INTO freetext_vectors (attempt_id, sample_id, tool, project_id, model, vector) "
            "VALUES (:a, :sm, :t, :p, :m, :v) ON CONFLICT DO NOTHING"),
            {"a": m[0], "sm": m[1], "t": m[2], "p": project_id, "m": mo_hinh if v is not None else None, "v": v})


async def mot_vong() -> None:
    try:
        for _ in range(LO_MOI_VONG):
            if await nhung_mot_lo() < LO_BAI:
                break
    except (project_client.KhongGoiDuoc, OSError) as ex:
        log.warning("Chua nhung duoc cau tra loi mo (project-svc / ml-svc): %s", ex)
    except Exception as ex:  # noqa: BLE001 — ml-svc sap / cham: vong sau thu lai, qua han thi so chu
        log.warning("Chua nhung duoc cau tra loi mo: %s", ex)


async def vong_lap(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            await mot_vong()
        except Exception:  # noqa: BLE001 — khong duoc lam chet service
            log.exception("Phan tich cau tra loi mo that bai, thu lai vong sau")
        if await settings_store.cho_theo_setting(settings_store.FREETEXT_ANALYSIS_INTERVAL, dung):
            break
