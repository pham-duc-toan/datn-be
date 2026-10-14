"""Luu tru du an DA DONG: chup bao cao chat luong roi xoa du lieu chi dung khi du an dang chay.

XOA: labels (nhan tho), consensus_rounds, task_targets, ds_state.
GIU: label_agreements, gold_answers, ds_skills, project_metrics — uy tin labeler tinh TOAN CUC tu ba bang
     dau (processors.cap_nhat_uy_tin), xoa la doi diem cua nguoi ta; va chung nho (khong mang jsonb nhan).

BA BUOC, dung giua chung thi vong sau chay tiep:
  1. chup bao cao + dat archived_at (mot transaction, khoa dong du an). Tu day API doc ban chup,
     nhan / target den tre cua du an bi bo qua (processors).
  2. xoa theo lo.
  3. dat purged_at.
"""

import logging
import uuid
from datetime import datetime, timedelta, timezone

from sqlalchemy import text

from app import cleanup, reports, settings_store
from app.db import session_factory
from app.errors import LoiApi

log = logging.getLogger(__name__)

# So du an toi da moi vong — du an lon xoa lau, khong don dong mot luc.
DU_AN_MOI_VONG = 20

# (ten bang, khoa chinh de chon lo)
BANG_XOA = [
    ("labels", "annotation_id"),
    ("consensus_rounds", "task_id, target"),
    ("task_targets", "task_id"),
    ("ds_state", "project_id"),
]


async def luu_tru(project_id: uuid.UUID) -> dict:
    """Luu tru mot du an. Khong co → 404; chua dong → 409; da xong tu truoc → purgedNow = False."""
    async with session_factory()() as s, s.begin():
        d = (await s.execute(text(
            "SELECT closed_at, archived_at, purged_at FROM projects WHERE project_id = :p FOR UPDATE"),
            {"p": project_id})).first()
        if d is None:
            raise LoiApi(404, "khong_tim_thay", "Khong tim thay du an.")
        if d.closed_at is None:
            raise LoiApi(409, "du_an_chua_dong", "Chi luu tru du lieu chat luong cua du an da dong.")
        if d.purged_at is not None:
            return {"projectId": str(project_id), "purgedAt": d.purged_at.isoformat(), "purgedNow": False, "deleted": {}}
        if d.archived_at is None:
            await reports.chup(s, project_id)
            await s.execute(text("UPDATE projects SET archived_at = now() WHERE project_id = :p"), {"p": project_id})

    da_xoa: dict[str, int] = {}
    het = True
    for bang, khoa in BANG_XOA:
        so, xong = await cleanup.don_mot_bang(bang, khoa, "project_id = :p", {"p": project_id})
        da_xoa[bang] = so
        het = het and xong

    if not het:
        # Du an rat lon: vong nay da xoa het han muc, vong sau xoa tiep (purged_at van null).
        log.info("Luu tru du an %s chua xong (da xoa %s), vong sau tiep", project_id, da_xoa)
        return {"projectId": str(project_id), "purgedAt": None, "purgedNow": True, "deleted": da_xoa}

    async with session_factory()() as s, s.begin():
        luc = (await s.execute(text(
            "UPDATE projects SET purged_at = COALESCE(purged_at, now()) WHERE project_id = :p RETURNING purged_at"),
            {"p": project_id})).scalar_one()
    log.info("Luu tru du an da dong %s: %s", project_id, da_xoa)
    return {"projectId": str(project_id), "purgedAt": luc.isoformat(), "purgedNow": True, "deleted": da_xoa}


async def don_mot_vong() -> int:
    """Luu tru cac du an dong qua project.closed_data_retention (0 = khong bao gio). Tra ve so du an da xu ly."""
    giu = settings_store.giay(settings_store.PROJECT_CLOSED_DATA_RETENTION)
    if giu <= 0:
        return 0

    han = datetime.now(timezone.utc) - timedelta(seconds=giu)
    async with session_factory()() as s:
        ds = (await s.execute(text(
            "SELECT project_id FROM projects WHERE closed_at IS NOT NULL AND purged_at IS NULL AND closed_at < :han "
            "ORDER BY closed_at LIMIT :n"), {"han": han, "n": DU_AN_MOI_VONG})).scalars().all()

    for project_id in ds:
        await luu_tru(project_id)
    return len(ds)
