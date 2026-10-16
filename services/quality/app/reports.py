"""Bao cao chat luong cua mot du an — tinh tu du lieu song, hoac doc ban chup neu du an da luu tru.

Du an da dong qua project.closed_data_retention thi nhan tho (labels), vong dong thuan (consensus_rounds)
va task_targets bi xoa (archive.py). Truoc khi xoa, hai bao cao duoi day duoc CHUP vao project_reports,
nen chu du an van xem duoc — chi la so lieu dung yen.
"""

import json
import uuid
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession


def phan_tram(dung: int, tong: int) -> int | None:
    return round(100 * dung / tong) if tong else None


async def tinh_tong_quan(s: AsyncSession, project_id: uuid.UUID) -> dict[str, Any]:
    """Dong thuan + redundancy thich ung + cau vang + Krippendorff alpha theo cong cu — tu du lieu song."""
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
        "SELECT tool, alpha, kappa, metric, item_count, label_count, computed_at FROM project_metrics WHERE project_id = :p ORDER BY tool"),
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
        "goldAccuracyPercent": phan_tram(gold.dung, gold.tong),
        "tools": [
            {"tool": r.tool, "krippendorffAlpha": r.alpha, "fleissKappa": r.kappa,
             # Ham khoang cach cua alpha: nominal / interval / cer / kendall / set_f1 (null = ban tinh cu, nominal).
             "alphaMetric": r.metric or "nominal",
             "itemCount": r.item_count, "labelCount": r.label_count, "computedAt": r.computed_at.isoformat()}
            for r in chi_so
        ],
        # null = so lieu song; co gia tri = ban chup luc luu tru (du an da dong, nhan tho da xoa).
        "archivedAt": None,
    }


async def tinh_labelers(s: AsyncSession, project_id: uuid.UUID) -> list[dict[str, Any]]:
    """Tung labeler trong du an: so nhan, ti le khop dong thuan, cau vang, do tin cay DS, uy tin — tu du lieu song."""
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

    staple = (await s.execute(text(
        "SELECT labeler_id, tool, sensitivity, specificity, sample_count FROM staple_skills WHERE project_id = :p"),
        {"p": project_id})).all()

    ds: dict[uuid.UUID, dict] = {}
    for k in ky_nang:
        ds.setdefault(k.labeler_id, {})[k.tool] = round(k.skill, 3)
    st: dict[uuid.UUID, dict] = {}
    for k in staple:
        st.setdefault(k.labeler_id, {})[k.tool] = {
            "sensitivity": round(k.sensitivity, 3), "specificity": round(k.specificity, 3), "sampleCount": k.sample_count}

    return [
        {
            "labelerId": str(d.labeler_id),
            "labelCount": d.so_nhan,
            "agreementPercent": phan_tram(d.khop, d.khop_tong),
            "agreementSampleCount": d.khop_tong,
            "goldAnswers": d.gold_tong,
            "goldAccuracyPercent": phan_tram(d.gold_dung, d.gold_tong),
            "dawidSkeneSkill": ds.get(d.labeler_id, {}),
            # STAPLE (mat na): chi de chu du an xem, khong tinh vao uy tin.
            "stapleMask": st.get(d.labeler_id, {}),
            "reputation": d.uy_tin,
        }
        for d in dong
    ]


def _json(v: Any) -> Any:
    return v if isinstance(v, (dict, list)) else json.loads(v)


async def _ban_chup(s: AsyncSession, project_id: uuid.UUID):
    return (await s.execute(text(
        "SELECT summary, labelers, created_at FROM project_reports WHERE project_id = :p"), {"p": project_id})).first()


async def tong_quan(s: AsyncSession, project_id: uuid.UUID) -> dict[str, Any]:
    chup = await _ban_chup(s, project_id)
    if chup is None:
        return await tinh_tong_quan(s, project_id)
    kq = dict(_json(chup.summary))
    kq["archivedAt"] = chup.created_at.isoformat()
    return kq


async def labelers(s: AsyncSession, project_id: uuid.UUID) -> list[dict[str, Any]]:
    chup = await _ban_chup(s, project_id)
    if chup is None:
        return await tinh_labelers(s, project_id)

    # Ban chup giu so lieu CUA DU AN; uy tin la diem TOAN CUC cua labeler nen luon lay gia tri hien tai.
    ds = [dict(d) for d in _json(chup.labelers)]
    if ds:
        uy_tin = {str(r.labeler_id): r.reputation for r in (await s.execute(text(
            "SELECT labeler_id, reputation FROM reputations WHERE labeler_id = ANY(:u)"),
            {"u": [uuid.UUID(d["labelerId"]) for d in ds]})).all()}
        for d in ds:
            d["reputation"] = uy_tin.get(d["labelerId"], d.get("reputation"))
    return ds


async def chup(s: AsyncSession, project_id: uuid.UUID) -> None:
    """Chup hai bao cao vao project_reports (trong transaction cua noi goi). Da co thi giu ban cu."""
    tq = await tinh_tong_quan(s, project_id)
    tq.pop("archivedAt", None)
    lb = await tinh_labelers(s, project_id)
    await s.execute(text(
        "INSERT INTO project_reports (project_id, summary, labelers, created_at) "
        "VALUES (:p, CAST(:s AS jsonb), CAST(:l AS jsonb), now()) ON CONFLICT (project_id) DO NOTHING"),
        {"p": project_id, "s": json.dumps(tq), "l": json.dumps(lb)})
