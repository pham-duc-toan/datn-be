"""API doc chi so chat luong. Moi duong dan duoi /quality (gateway route /quality/{everything}).

Quyen: chi so du an → chu du an hoac admin (khong phai chu → 404, khong lo du an
ton tai — giong cac service C#). Labeler chi xem diem cua chinh minh, KHONG xem
so cau vang dung/sai: dem thay doi sau tung lan nop la doan ra task nao la cau
vang (VD-Q-03).
"""

import json
import uuid
from datetime import datetime, timezone

from fastapi import APIRouter, Depends
from pydantic import BaseModel
from sqlalchemy import text

from app import archive, ds_batch, reports
from app.contracts import FraudCaseConfirmed
from app.messaging import outbox
from app.auth import NguoiGoi, can_vai_tro, nguoi_goi
from app.db import session_factory
from app.errors import LoiApi
from app.reports import phan_tram

router = APIRouter(prefix="/quality")


async def _kiem_chu_du_an(project_id: uuid.UUID, ng: NguoiGoi) -> None:
    async with session_factory()() as s:
        dong = (await s.execute(text("SELECT owner_id FROM projects WHERE project_id = :p"), {"p": project_id})).first()
    if dong is None or (not ng.la_admin and dong.owner_id != ng.user_id):
        raise LoiApi(404, "khong_tim_thay", "Khong tim thay du an.")


@router.get("/projects/{project_id}/summary")
async def tong_quan(project_id: uuid.UUID, ng: NguoiGoi = Depends(nguoi_goi)) -> dict:
    """Dong thuan + redundancy thich ung + cau vang + Krippendorff alpha theo cong cu.
    Du an da luu tru (archivedAt co gia tri): so lieu la ban chup, khong con doi."""
    await _kiem_chu_du_an(project_id, ng)
    async with session_factory()() as s:
        return await reports.tong_quan(s, project_id)


@router.get("/projects/{project_id}/labelers")
async def labeler_trong_du_an(project_id: uuid.UUID, ng: NguoiGoi = Depends(nguoi_goi)) -> list[dict]:
    """Tung labeler trong du an: so nhan, ti le khop dong thuan, cau vang, do tin cay DS, uy tin."""
    await _kiem_chu_du_an(project_id, ng)
    async with session_factory()() as s:
        return await reports.labelers(s, project_id)


@router.post("/admin/projects/{project_id}/archive")
async def luu_tru_du_an(project_id: uuid.UUID, ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """Admin luu tru NGAY du lieu chat luong cua mot du an DA DONG (chup bao cao, xoa nhan tho), khong doi
    het project.closed_data_retention. 409 neu du an chua dong."""
    return await archive.luu_tru(project_id)


@router.get("/me")
async def cua_toi(ng: NguoiGoi = Depends(can_vai_tro("labeler"))) -> dict:
    """Diem uy tin cua chinh labeler. Khong tra so cau vang (chong doan cau vang)."""
    async with session_factory()() as s:
        r = (await s.execute(text(
            "SELECT reputation, agree_total, agree_count, updated_at FROM reputations WHERE labeler_id = :l"),
            {"l": ng.user_id})).first()

    if r is None:
        return {"reputation": None, "agreementPercent": None, "updatedAt": None}
    return {
        "reputation": r.reputation,
        "agreementPercent": phan_tram(r.agree_count, r.agree_total),
        "updatedAt": r.updated_at.isoformat(),
    }


@router.post("/admin/dawid-skene/run")
async def chay_ds_ngay(full: bool = False, ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """Admin chay lo Dawid-Skene ngay (binh thuong chay dinh ky). Mac dinh chi du an co nhan moi;
    full=true tinh lai tat ca (cham: ~1 giay / du an) — dung sau khi doi quality.ds_min_labels."""
    so = await ds_batch.chay_mot_lo(tat_ca=full)
    return {"computed": so, "full": full}


# =====================================================================
# CHONG GIAN LAN (FM-07) — ho so nghi van cho admin
# =====================================================================

TRANG_THAI_HO_SO = ("open", "dismissed", "confirmed")


def _ho_so(r) -> dict:
    return {
        "id": str(r.id),
        "type": r.type,
        "projectId": None if r.project_id is None else str(r.project_id),
        "userIds": [str(u) for u in r.user_ids],
        "evidence": r.evidence if isinstance(r.evidence, dict) else json.loads(r.evidence),
        "status": r.status,
        "createdAt": r.created_at.isoformat(),
        "updatedAt": r.updated_at.isoformat(),
        "resolvedAt": None if r.resolved_at is None else r.resolved_at.isoformat(),
        "note": r.note,
        "blocked": r.blocked,
    }


@router.get("/admin/fraud-cases")
async def ho_so_gian_lan(status: str = "open", type: str | None = None, page: int = 1, pageSize: int = 20,
                         ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """Hang doi ho so nghi van — dang mo truoc het, moi cap nhat truoc."""
    if status not in TRANG_THAI_HO_SO:
        raise LoiApi(400, "trang_thai_khong_hop_le", "status: open | dismissed | confirmed.")
    trang = max(page, 1)
    co = pageSize if 1 <= pageSize <= 100 else 20
    dk = "status = :st" + ("" if type is None else " AND type = :ty")
    tham_so = {"st": status, "ty": type, "n": co, "o": (trang - 1) * co}
    async with session_factory()() as s:
        tong = (await s.execute(text(f"SELECT COUNT(*) FROM fraud_cases WHERE {dk}"), tham_so)).scalar_one()
        dong = (await s.execute(text(
            f"SELECT * FROM fraud_cases WHERE {dk} ORDER BY updated_at DESC LIMIT :n OFFSET :o"), tham_so)).all()
    return {"items": [_ho_so(r) for r in dong], "page": trang, "pageSize": co, "total": tong}


@router.get("/admin/fraud-cases/{case_id}")
async def chi_tiet_ho_so(case_id: uuid.UUID, ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    async with session_factory()() as s:
        r = (await s.execute(text("SELECT * FROM fraud_cases WHERE id = :id"), {"id": case_id})).first()
        if r is None:
            raise LoiApi(404, "khong_tim_thay", "Khong tim thay ho so.")
        # Lich su gan day cua tung nguoi trong ho so (giup admin quyet dinh).
        luot = (await s.execute(text(
            "SELECT labeler_id, COUNT(*) AS so_luot, percentile_cont(0.5) WITHIN GROUP (ORDER BY work_seconds) AS trung_vi "
            "FROM fraud_submissions WHERE labeler_id = ANY(:u) GROUP BY labeler_id"), {"u": list(r.user_ids)})).all()
        uy_tin = {d.labeler_id: d.reputation for d in (await s.execute(text(
            "SELECT labeler_id, reputation FROM reputations WHERE labeler_id = ANY(:u)"), {"u": list(r.user_ids)})).all()}
    kq = _ho_so(r)
    kq["users"] = [
        {"userId": str(d.labeler_id), "submissions": d.so_luot,
         "medianWorkSeconds": None if d.trung_vi is None else round(float(d.trung_vi), 3),
         "reputation": uy_tin.get(d.labeler_id)}
        for d in luot
    ]
    return kq


class XuLyHoSo(BaseModel):
    action: str
    note: str | None = None
    blockAccounts: bool = False


@router.post("/admin/fraud-cases/{case_id}/resolve")
async def xu_ly_ho_so(case_id: uuid.UUID, body: XuLyHoSo, ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """dismiss (khong gian lan) | confirm (gian lan; blockAccounts=true → identity khoa cac tai khoan)."""
    if body.action not in ("dismiss", "confirm"):
        raise LoiApi(400, "hanh_dong_khong_hop_le", "action: dismiss | confirm.")
    ghi_chu = (body.note or "").strip()
    if not ghi_chu or len(ghi_chu) > 1000:
        raise LoiApi(400, "thieu_ghi_chu", "Phai ghi ly do (toi da 1000 ky tu).")
    khoa = body.action == "confirm" and body.blockAccounts

    async with session_factory()() as s, s.begin():
        r = (await s.execute(text("SELECT * FROM fraud_cases WHERE id = :id FOR UPDATE"), {"id": case_id})).first()
        if r is None:
            raise LoiApi(404, "khong_tim_thay", "Khong tim thay ho so.")
        if r.status != "open":
            raise LoiApi(409, "ho_so_da_xu_ly", "Ho so da duoc xu ly.")
        await s.execute(text(
            "UPDATE fraud_cases SET status = :st, resolved_at = :at, resolved_by = :by, note = :n, blocked = :b WHERE id = :id"),
            {"st": "confirmed" if body.action == "confirm" else "dismissed", "at": datetime.now(timezone.utc),
             "by": ng.user_id, "n": ghi_chu, "b": khoa, "id": case_id})
        if body.action == "confirm":
            await outbox.ghi(s, FraudCaseConfirmed(case_id=case_id, user_ids=list(r.user_ids), reason=ghi_chu,
                                                   block_accounts=khoa), uuid.uuid4(), None)
        moi = (await s.execute(text("SELECT * FROM fraud_cases WHERE id = :id"), {"id": case_id})).first()
    return _ho_so(moi)
