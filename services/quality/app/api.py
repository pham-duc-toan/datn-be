"""API doc chi so chat luong. Moi duong dan duoi /quality (gateway route /quality/{everything}).

Quyen: chi so du an → chu du an hoac admin (khong phai chu → 404, khong lo du an
ton tai — giong cac service C#). Labeler chi xem diem cua chinh minh, KHONG xem
so cau vang dung/sai: dem thay doi sau tung lan nop la doan ra task nao la cau
vang (VD-Q-03).
"""

import uuid

from fastapi import APIRouter, Depends
from sqlalchemy import text

from app import ds_batch
from app.auth import NguoiGoi, can_vai_tro, nguoi_goi
from app.db import session_factory
from app.errors import LoiApi

router = APIRouter(prefix="/quality")


async def _kiem_chu_du_an(project_id: uuid.UUID, ng: NguoiGoi) -> None:
    async with session_factory()() as s:
        dong = (await s.execute(text("SELECT owner_id FROM projects WHERE project_id = :p"), {"p": project_id})).first()
    if dong is None or (not ng.la_admin and dong.owner_id != ng.user_id):
        raise LoiApi(404, "khong_tim_thay", "Khong tim thay du an.")


def _phan_tram(dung: int, tong: int) -> int | None:
    return round(100 * dung / tong) if tong else None


@router.get("/projects/{project_id}/summary")
async def tong_quan(project_id: uuid.UUID, ng: NguoiGoi = Depends(nguoi_goi)) -> dict:
    """Dong thuan + redundancy thich ung + cau vang + Krippendorff alpha theo cong cu."""
    await _kiem_chu_du_an(project_id, ng)
    async with session_factory()() as s:
        # Trang thai CUOI moi task = vong co target lon nhat.
        vong = (await s.execute(text(
            "SELECT status, COUNT(*) AS n FROM ("
            "  SELECT DISTINCT ON (task_id) task_id, status FROM consensus_rounds WHERE project_id = :p "
            "  ORDER BY task_id, target DESC) x GROUP BY status"), {"p": project_id})).all()
        xin_them = (await s.execute(text(
            "SELECT COUNT(*) FROM consensus_rounds WHERE project_id = :p AND status = 'moreRequested'"),
            {"p": project_id})).scalar_one()
        so_nhan = (await s.execute(text("SELECT COUNT(*) FROM labels WHERE project_id = :p"), {"p": project_id})).scalar_one()
        gold = (await s.execute(text(
            "SELECT COUNT(*) AS tong, COUNT(*) FILTER (WHERE correct) AS dung FROM gold_answers WHERE project_id = :p"),
            {"p": project_id})).one()
        chi_so = (await s.execute(text(
            "SELECT tool, alpha, item_count, label_count, computed_at FROM project_metrics WHERE project_id = :p ORDER BY tool"),
            {"p": project_id})).all()

    dem = {r.status: r.n for r in vong}
    return {
        "projectId": str(project_id),
        "labelCount": so_nhan,
        "tasksEvaluated": sum(dem.values()),
        "agreed": dem.get("agreed", 0),
        "disputed": dem.get("disputed", 0),
        "notApplicable": dem.get("notApplicable", 0),
        "waitingMoreLabels": dem.get("moreRequested", 0),
        "redundancyIncreases": xin_them,
        "goldAnswers": gold.tong,
        "goldAccuracyPercent": _phan_tram(gold.dung, gold.tong),
        "tools": [
            {"tool": r.tool, "krippendorffAlpha": r.alpha, "itemCount": r.item_count, "labelCount": r.label_count,
             "computedAt": r.computed_at.isoformat()}
            for r in chi_so
        ],
    }


@router.get("/projects/{project_id}/labelers")
async def labeler_trong_du_an(project_id: uuid.UUID, ng: NguoiGoi = Depends(nguoi_goi)) -> list[dict]:
    """Tung labeler trong du an: so nhan, ti le khop dong thuan, cau vang, do tin cay DS, uy tin."""
    await _kiem_chu_du_an(project_id, ng)
    async with session_factory()() as s:
        dong = (await s.execute(text(
            "SELECT l.labeler_id, COUNT(*) AS so_nhan, "
            "  (SELECT COUNT(*) FROM label_agreements a WHERE a.labeler_id = l.labeler_id AND a.project_id = :p) AS khop_tong, "
            "  (SELECT COUNT(*) FROM label_agreements a WHERE a.labeler_id = l.labeler_id AND a.project_id = :p AND a.agrees) AS khop, "
            "  (SELECT COUNT(*) FROM gold_answers g WHERE g.labeler_id = l.labeler_id AND g.project_id = :p) AS gold_tong, "
            "  (SELECT COUNT(*) FROM gold_answers g WHERE g.labeler_id = l.labeler_id AND g.project_id = :p AND g.correct) AS gold_dung, "
            "  (SELECT reputation FROM reputations r WHERE r.labeler_id = l.labeler_id) AS uy_tin "
            "FROM labels l WHERE l.project_id = :p AND l.labeler_id IS NOT NULL GROUP BY l.labeler_id ORDER BY so_nhan DESC"),
            {"p": project_id})).all()
        ky_nang = (await s.execute(text(
            "SELECT labeler_id, tool, skill, label_count FROM ds_skills WHERE project_id = :p"), {"p": project_id})).all()

    ds: dict[uuid.UUID, dict] = {}
    for k in ky_nang:
        ds.setdefault(k.labeler_id, {})[k.tool] = round(k.skill, 3)

    return [
        {
            "labelerId": str(d.labeler_id),
            "labelCount": d.so_nhan,
            "agreementPercent": _phan_tram(d.khop, d.khop_tong),
            "agreementSampleCount": d.khop_tong,
            "goldAnswers": d.gold_tong,
            "goldAccuracyPercent": _phan_tram(d.gold_dung, d.gold_tong),
            "dawidSkeneSkill": ds.get(d.labeler_id, {}),
            "reputation": d.uy_tin,
        }
        for d in dong
    ]


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
        "agreementPercent": _phan_tram(r.agree_count, r.agree_total),
        "updatedAt": r.updated_at.isoformat(),
    }


@router.post("/admin/dawid-skene/run")
async def chay_ds_ngay(ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """Admin chay lo Dawid-Skene ngay (binh thuong chay dinh ky) — tien cho demo."""
    so = await ds_batch.chay_mot_lo()
    return {"computed": so}
