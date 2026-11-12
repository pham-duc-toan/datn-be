"""Bao cao chat luong cua mot du an — tinh tu du lieu song, hoac doc ban chup neu du an da luu tru.

Du lieu song = hai nguon (kien truc 3.22, khong ban sao):
  - project-svc qua gRPC: bai da nop (so cau, cau vang, thoi diem) va KET QUA CUOI cua bai (dat, duoc thuong —
    da tinh ca phan xu khieu nai), ly do / luc dong thu bai;
  - quality: moc cham, da so tung cau, chi so do tin cay, DS / STAPLE (chi co SAU khi cham — ds_at), uy tin.

Du an da dong qua project.closed_data_retention thi ket qua trung gian (sample_majorities, vector cau mo)
bi xoa (archive.py). Truoc khi xoa, hai bao cao duoi day duoc CHUP vao project_reports, nen chu du an van xem duoc
— chi la so lieu dung yen.
"""

import json
import uuid
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import project_client


def phan_tram(dung: int, tong: int) -> int | None:
    return round(100 * dung / tong) if tong else None


async def tinh_tong_quan(s: AsyncSession, project_id: uuid.UUID) -> dict[str, Any]:
    """Ket qua cham ca du an (docs 3.18) + cau vang + Krippendorff alpha theo cong cu — tu du lieu song."""
    du_an = await project_client.du_an(project_id)
    bai = await project_client.bai_da_nop(project_id)
    cham = (await s.execute(text(
        "SELECT graded_at, majority_used, freetext_state, freetext_method FROM project_gradings WHERE project_id = :p"),
        {"p": project_id})).first()
    cau = (await s.execute(text(
        "SELECT status, COUNT(*) AS n FROM sample_majorities WHERE project_id = :p GROUP BY status"), {"p": project_id})).all()
    chi_so = (await s.execute(text(
        "SELECT tool, alpha, kappa, metric, item_count, label_count, computed_at FROM project_metrics WHERE project_id = :p ORDER BY tool"),
        {"p": project_id})).all()

    da_cham = [b for b in bai if b.graded]
    dat = sum(1 for b in da_cham if b.passed)
    vang_tong = sum(b.gold_total for b in bai)
    dem = {r.status: r.n for r in cau}
    return {
        "projectId": str(project_id),
        "labelCount": sum(b.label_count for b in bai),
        "attemptsSubmitted": len(bai),
        "attemptsGraded": len(da_cham),
        "attemptsPassed": dat,
        # Bai duoc thuong (toi da so suat — n bai dat nop som nhat, lap suat trong, khieu nai duoc chap nhan).
        "attemptsRewarded": sum(1 for b in da_cham if b.rewarded),
        "attemptsFailed": len(da_cham) - dat,
        "collectionReason": du_an.finish_reason,
        "collectionFinishedAt": None if du_an.collection_finished_at is None else du_an.collection_finished_at.isoformat(),
        "gradedAt": None if cham is None or cham.graded_at is None else cham.graded_at.isoformat(),
        "majorityUsed": None if cham is None else cham.majority_used,
        # Cau tra loi mo (3.19): pending = cho ml-svc phan tich; method semantic (so nghia) | lexical (so chu).
        "freeTextAnalysis": None if cham is None or cham.freetext_state is None else {
            "state": cham.freetext_state, "method": cham.freetext_method},
        "samplesEvaluated": sum(dem.values()),
        "agreed": dem.get("agreed", 0),
        "disputed": dem.get("disputed", 0),
        "notApplicable": dem.get("notApplicable", 0),
        "goldAnswers": vang_tong,
        "goldAccuracyPercent": phan_tram(sum(b.gold_correct for b in bai), vang_tong),
        "tools": [
            {"tool": r.tool, "krippendorffAlpha": r.alpha, "fleissKappa": r.kappa,
             # Ham khoang cach cua alpha: nominal / interval / cer / kendall / set_f1 (null = ban tinh cu, nominal).
             "alphaMetric": r.metric or "nominal",
             "itemCount": r.item_count, "labelCount": r.label_count, "computedAt": r.computed_at.isoformat()}
            for r in chi_so
        ],
        # null = so lieu song; co gia tri = ban chup luc luu tru (du an da dong, ket qua trung gian da xoa).
        "archivedAt": None,
    }


async def tinh_labelers(s: AsyncSession, project_id: uuid.UUID) -> list[dict[str, Any]]:
    """Tung bai nop trong du an: so cau tra loi, ti le giong da so, cau vang, dat / truot, DS, uy tin — tu du lieu song."""
    bai = await project_client.bai_da_nop(project_id)
    nguoi = list({b.labeler_id for b in bai})
    uy_tin = {r.labeler_id: r.reputation for r in (await s.execute(text(
        "SELECT labeler_id, reputation FROM reputations WHERE labeler_id = ANY(:u)"), {"u": nguoi})).all()} if nguoi else {}
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

    # Ti le giong da so cao truoc (chua cham xuong cuoi), cung ti le thi nop som truoc.
    bai = sorted(bai, key=lambda b: (0 if b.graded else 1, -b.agreement_percent if b.graded else 0, b.submitted_at))
    return [
        {
            "labelerId": str(b.labeler_id),
            "attemptId": str(b.attempt_id),
            "labelCount": b.label_count,
            "questionCount": len(b.sample_ids),
            "submittedAt": b.submitted_at.isoformat(),
            # null = chua cham (du an chua dong thu bai).
            "agreementPercent": b.agreement_percent if b.graded else None,
            "agreementSampleCount": b.comparable_count if b.graded else None,
            "matchedCount": b.matched_count if b.graded else None,
            "passed": b.passed if b.graded else None,
            # Trong n suat thuong (n bai dat nop som nhat). passed ma rewarded false = dat nhung het suat;
            # rewarded ma passed false = duoc lap suat trong / khieu nai duoc chap nhan.
            "rewarded": b.rewarded if b.graded else None,
            "goldAnswers": b.gold_total,
            "goldAccuracyPercent": phan_tram(b.gold_correct, b.gold_total),
            "dawidSkeneSkill": ds.get(b.labeler_id, {}),
            # STAPLE (mat na): chi de chu du an xem, khong tinh vao uy tin.
            "stapleMask": st.get(b.labeler_id, {}),
            "reputation": uy_tin.get(b.labeler_id),
        }
        for b in bai
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
