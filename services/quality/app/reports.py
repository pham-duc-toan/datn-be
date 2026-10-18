"""Bao cao chat luong cua mot du an — tinh tu du lieu song, hoac doc ban chup neu du an da luu tru.

Du an da dong qua project.closed_data_retention thi nhan tho (labels), bai nop (attempts) va ket qua gop
tung cau (sample_majorities) bi xoa (archive.py). Truoc khi xoa, hai bao cao duoi day duoc CHUP vao project_reports,
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
    """Ket qua cham ca du an (docs 3.18) + cau vang + Krippendorff alpha theo cong cu — tu du lieu song."""
    so_nhan = (await s.execute(text("SELECT COUNT(*) FROM labels WHERE project_id = :p"), {"p": project_id})).scalar_one()
    bai = (await s.execute(text(
        "SELECT COUNT(*) AS n, COALESCE(SUM(gold_total), 0) AS gt, COALESCE(SUM(gold_correct), 0) AS gd "
        "FROM attempts WHERE project_id = :p"), {"p": project_id})).one()
    dot = (await s.execute(text(
        "SELECT reason, finished_at, graded_at, majority_used FROM project_collections WHERE project_id = :p"),
        {"p": project_id})).first()
    cham = (await s.execute(text(
        "SELECT COUNT(*) AS n, COUNT(*) FILTER (WHERE passed) AS dat FROM attempt_grades WHERE project_id = :p"),
        {"p": project_id})).one()
    cau = (await s.execute(text(
        "SELECT status, COUNT(*) AS n FROM sample_majorities WHERE project_id = :p GROUP BY status"), {"p": project_id})).all()
    chi_so = (await s.execute(text(
        "SELECT tool, alpha, kappa, metric, item_count, label_count, computed_at FROM project_metrics WHERE project_id = :p ORDER BY tool"),
        {"p": project_id})).all()

    dem = {r.status: r.n for r in cau}
    return {
        "projectId": str(project_id),
        "labelCount": so_nhan,
        "attemptsSubmitted": bai.n,
        "attemptsGraded": cham.n,
        "attemptsPassed": cham.dat,
        "attemptsFailed": cham.n - cham.dat,
        "collectionReason": None if dot is None else dot.reason,
        "collectionFinishedAt": None if dot is None else dot.finished_at.isoformat(),
        "gradedAt": None if dot is None or dot.graded_at is None else dot.graded_at.isoformat(),
        "majorityUsed": None if dot is None else dot.majority_used,
        "samplesEvaluated": sum(dem.values()),
        "agreed": dem.get("agreed", 0),
        "disputed": dem.get("disputed", 0),
        "notApplicable": dem.get("notApplicable", 0),
        "goldAnswers": int(bai.gt),
        "goldAccuracyPercent": phan_tram(int(bai.gd), int(bai.gt)),
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
    """Tung bai nop trong du an: so cau tra loi, ti le giong da so, cau vang, dat / truot, DS, uy tin — tu du lieu song."""
    dong = (await s.execute(text(
        "SELECT a.attempt_id, a.labeler_id, a.answered_count, cardinality(a.sample_ids) AS so_cau, a.gold_total, a.gold_correct, "
        "  a.submitted_at, g.comparable_count, g.matched_count, g.agreement_percent, g.passed, "
        "  (SELECT reputation FROM reputations r WHERE r.labeler_id = a.labeler_id) AS uy_tin "
        "FROM attempts a LEFT JOIN attempt_grades g ON g.attempt_id = a.attempt_id "
        "WHERE a.project_id = :p ORDER BY g.agreement_percent DESC NULLS LAST, a.submitted_at"),
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
            "attemptId": str(d.attempt_id),
            "labelCount": d.answered_count,
            "questionCount": d.so_cau,
            "submittedAt": d.submitted_at.isoformat(),
            # null = chua cham (du an chua dong thu bai).
            "agreementPercent": d.agreement_percent,
            "agreementSampleCount": d.comparable_count,
            "matchedCount": d.matched_count,
            "passed": d.passed,
            "goldAnswers": d.gold_total,
            "goldAccuracyPercent": phan_tram(d.gold_correct, d.gold_total),
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
