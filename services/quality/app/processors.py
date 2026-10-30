"""Xu ly event — moi ham chay TRONG transaction consumer da mo (khong commit).

Du an lam trung tam (docs 3.18): attempt.submitted (labeler nop ca bai) va project.collection_finished
(task-svc dong thu bai) di hai queue khac nhau, cai nao den truoc cung duoc. Ca hai deu goi thu_cham():
chi cham khi DA dong thu bai VA da co du moi bai trong danh sach; moi du an chi cham MOT lan
(project_collections.graded_at).

Du an co cau tra loi mo (3.19): thu_cham con cho ml-svc nhung du cau (freetext_state = pending); worker
phan_tich_mo nhung cau ngay khi bai toi va goi lai thu_cham.
"""

import json
import logging
import uuid
from datetime import datetime, timedelta, timezone

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import cau_mo, cham, consensus, fraud, ml_client, project_client, reputation, settings_store
from app.contracts import (
    AttemptSubmitted,
    GradedAttempt,
    ProjectCollectionFinished,
    ProjectGraded,
    ProjectPublished,
    ReputationChanged,
)
from app.messaging import outbox
from app.messaging.envelope import Envelope

log = logging.getLogger(__name__)

# Khoa tat dinh cho moi (bai, mau) — attempt.submitted giao lai khong thanh nhan thu hai.
KHONG_GIAN_NHAN = uuid.UUID("6f1d3c9e-2b7a-4f55-9a43-0c8e1d2b7a01")


def id_nhan(attempt_id: uuid.UUID, sample_id: uuid.UUID) -> uuid.UUID:
    return uuid.uuid5(KHONG_GIAN_NHAN, f"{attempt_id}:{sample_id}")


async def project_published(s: AsyncSession, env: Envelope[ProjectPublished]) -> None:
    await _ghi_ban_sao(s, env.payload, env.occurred_at)


async def _ghi_ban_sao(s: AsyncSession, p: ProjectPublished, luc) -> None:
    # Dieu khoan bat bien sau publish: da co thi thoi.
    await s.execute(
        text(
            "INSERT INTO projects (project_id, owner_id, modality, label_schema, pass_threshold_percent, slots, "
            "fill_slots_min_percent, question_count, published_at) "
            "VALUES (:id, :owner, :modality, CAST(:schema AS jsonb), :t, :slots, :lap, :q, :at) ON CONFLICT (project_id) DO NOTHING"
        ),
        {"id": p.project_id, "owner": p.owner_id, "modality": p.modality, "schema": json.dumps(p.label_schema),
         "t": p.pass_threshold_percent, "slots": p.slots, "lap": p.fill_slots_min_percent, "q": p.question_count, "at": luc},
    )


async def _bu_ban_sao(s: AsyncSession, project_id: uuid.UUID):
    """Thieu ban sao du an → hoi project-svc roi ghi. None = khong bu duoc (chua cau hinh / chua publish)."""
    try:
        kq = await project_client.lay_dieu_khoan(project_id)
    except project_client.ChuaCauHinh:
        return None
    if kq is None:
        return None
    await _ghi_ban_sao(s, kq[0], kq[1])
    log.info("Bu ban sao du an %s tu project-svc", project_id)
    return await _du_an(s, project_id)


async def _du_an(s: AsyncSession, project_id: uuid.UUID):
    return (await s.execute(
        text("SELECT label_schema, pass_threshold_percent, slots, fill_slots_min_percent FROM projects WHERE project_id = :p"),
        {"p": project_id})).first()


async def project_dong(s: AsyncSession, env: Envelope) -> None:
    """project.completed / project.cancelled: ghi moc dong (lan dau thang) — archive.py dem thoi han tu day.
    Du an chua tung publish (huy khi con nhap) khong co ban sao o day → khong lam gi."""
    await s.execute(
        text("UPDATE projects SET closed_at = :at WHERE project_id = :p AND closed_at IS NULL"),
        {"p": env.payload.project_id, "at": env.occurred_at},
    )


async def da_luu_tru(s: AsyncSession, project_id: uuid.UUID) -> bool:
    """Du an da chup bao cao va (dang) xoa nhan tho — event den tre cua no khong duoc ghi lai du lieu."""
    return (await s.execute(
        text("SELECT 1 FROM projects WHERE project_id = :p AND archived_at IS NOT NULL"), {"p": project_id})).first() is not None


async def attempt_submitted(s: AsyncSession, env: Envelope[AttemptSubmitted]) -> None:
    """Luu bai + tung cau da tra loi, xet gian lan, roi thu cham (neu da dong thu bai)."""
    p = env.payload
    if await da_luu_tru(s, p.project_id):
        log.info("Bo qua bai %s: du an %s da luu tru", p.attempt_id, p.project_id)
        return

    da_tra_loi = [a for a in p.answers if a.label_payload is not None]
    moi = (await s.execute(
        text(
            "INSERT INTO attempts (attempt_id, project_id, labeler_id, sample_ids, answered_count, gold_total, gold_correct, started_at, submitted_at) "
            "VALUES (:a, :p, :l, :ids, :n, :gt, :gd, :st, :sb) ON CONFLICT (attempt_id) DO NOTHING RETURNING attempt_id"
        ),
        {"a": p.attempt_id, "p": p.project_id, "l": p.labeler_id, "ids": [a.sample_id for a in p.answers],
         "n": len(da_tra_loi), "gt": p.gold_total, "gd": p.gold_correct, "st": p.started_at, "sb": p.submitted_at},
    )).first()
    if moi is None:
        return  # bai da luu (giao lai)

    for a in da_tra_loi:
        await s.execute(
            text(
                "INSERT INTO labels (annotation_id, project_id, attempt_id, sample_id, labeler_id, data, sample_metadata, received_at) "
                "VALUES (:id, :p, :a, :sm, :l, CAST(:d AS jsonb), CAST(:md AS jsonb), :at) ON CONFLICT DO NOTHING"
            ),
            {"id": id_nhan(p.attempt_id, a.sample_id), "p": p.project_id, "a": p.attempt_id, "sm": a.sample_id,
             "l": p.labeler_id, "d": json.dumps(a.label_payload.data), "md": json.dumps(a.sample_metadata or {}),
             "at": p.submitted_at},
        )

    await fraud.xet_bai(s, p)
    await thu_cham(s, p.project_id, env)


async def collection_finished(s: AsyncSession, env: Envelope[ProjectCollectionFinished]) -> None:
    p = env.payload
    await s.execute(
        text(
            "INSERT INTO project_collections (project_id, reason, attempt_ids, excluded_sample_ids, finished_at) "
            "VALUES (:p, :r, :a, :x, :at) ON CONFLICT (project_id) DO NOTHING"
        ),
        {"p": p.project_id, "r": p.reason, "a": [a.attempt_id for a in p.attempts], "x": list(p.excluded_sample_ids),
         "at": p.finished_at},
    )
    await thu_cham(s, p.project_id, env)


# =====================================================================
# CHAM CA DU AN
# =====================================================================


NHOM_KHOA_DU_AN = 7004  # pg_advisory_xact_lock theo du an — tach khoi khoa labeler (7003)


async def thu_cham(s: AsyncSession, project_id: uuid.UUID, env: Envelope) -> None:
    # KHOA THEO DU AN truoc khi dem: bai cuoi va event dong thu bai xu ly SONG SONG trong hai transaction
    # thi moi ben chi thay du lieu cua minh, deu thay thieu va KHONG BEN NAO cham — du an ket o Grading.
    # Khoa xep hang hai ben; ben sau thay du (READ COMMITTED).
    await s.execute(text("SELECT pg_advisory_xact_lock(:nhom, hashtext(:p))"), {"nhom": NHOM_KHOA_DU_AN, "p": str(project_id)})

    dot = (await s.execute(
        text("SELECT attempt_ids, excluded_sample_ids, graded_at, finished_at FROM project_collections WHERE project_id = :p"),
        {"p": project_id})).first()
    if dot is None or dot.graded_at is not None:
        return  # chua dong thu bai, hoac da cham

    can_cham = set(dot.attempt_ids)
    bai = (await s.execute(
        text("SELECT attempt_id, labeler_id, sample_ids, gold_total, gold_correct FROM attempts "
             "WHERE attempt_id = ANY(:a) ORDER BY submitted_at, attempt_id"), {"a": list(can_cham)})).all()
    if len(bai) < len(can_cham):
        log.info("Du an %s: moi co %s / %s bai — doi attempt.submitted", project_id, len(bai), len(can_cham))
        return

    du_an = await _du_an(s, project_id)
    if du_an is None:
        du_an = await _bu_ban_sao(s, project_id)
    if du_an is None or du_an.pass_threshold_percent is None:
        # Khong co tap nhan / nguong thi khong cham duoc — NEM LOI de giao lai roi vao DLQ (khong chi tien sai).
        raise RuntimeError(f"Chua co ban sao v2 cua du an {project_id} — khong cham duoc.")

    schema = du_an.label_schema if isinstance(du_an.label_schema, dict) else json.loads(du_an.label_schema)
    dong_nhan = (await s.execute(
        text("SELECT attempt_id, sample_id, data, sample_metadata FROM labels WHERE attempt_id = ANY(:a)"),
        {"a": list(can_cham)})).all()
    nhan = {(str(d.attempt_id), str(d.sample_id)): (d.data if isinstance(d.data, dict) else json.loads(d.data)) for d in dong_nhan}
    metadata: dict[str, dict | None] = {}
    for d in dong_nhan:
        md = d.sample_metadata if isinstance(d.sample_metadata, dict) else None
        if md and str(d.sample_id) not in metadata:
            metadata[str(d.sample_id)] = md

    # Cau tra loi mo (3.19): cham theo nghia khi ml-svc da nhung du; chua du thi cho (worker phan_tich_mo
    # cham lai), qua han / khong cau hinh ml-svc thi so chu.
    mo = await _tham_so_mo(s, project_id, schema, nhan, dot.finished_at, env)
    if mo is False:
        return

    tham_so = consensus.ThamSoGop(
        nguong=settings_store.nguong_gop(), f1_toi_thieu=settings_store.so_thuc(settings_store.CONSENSUS_MIN_F1))
    kq = cham.cham_du_an(
        schema,
        [cham.Bai(str(b.attempt_id), str(b.labeler_id), tuple(str(x) for x in b.sample_ids), b.gold_total, b.gold_correct)
         for b in bai],
        nhan, metadata, {str(x) for x in dot.excluded_sample_ids}, du_an.pass_threshold_percent, tham_so, mo or None)

    # So suat = so nguoi DUOC THUONG: n bai dat nop som nhat (kq.bai giu thu tu luc nop cua `bai`); du an bat
    # lap suat trong thi suat con lai trao cho bai giong da so nhieu nhat.
    thuong = cham.chon_duoc_thuong(kq.bai, du_an.slots if du_an.slots is not None else len(kq.bai), du_an.fill_slots_min_percent)

    luc = datetime.now(timezone.utc)
    for c in kq.cau:
        await s.execute(
            text(
                "INSERT INTO sample_majorities (project_id, sample_id, status, final, label_count) "
                "VALUES (:p, :sm, :st, CAST(:f AS jsonb), :n) ON CONFLICT (project_id, sample_id) DO NOTHING"
            ),
            {"p": project_id, "sm": uuid.UUID(c.sample_id), "st": c.status,
             "f": None if c.final is None else json.dumps(c.final), "n": c.so_nhan},
        )
    for b in kq.bai:
        await s.execute(
            text(
                "INSERT INTO attempt_grades (attempt_id, project_id, labeler_id, comparable_count, matched_count, agreement_percent, "
                "gold_total, gold_correct, passed, rewarded, answered_count, graded_at) "
                "VALUES (:a, :p, :l, :c, :m, :pt, :gt, :gd, :ok, :th, :n, :at) ON CONFLICT (attempt_id) DO NOTHING"
            ),
            {"a": uuid.UUID(b.attempt_id), "p": project_id, "l": uuid.UUID(b.labeler_id), "c": b.so_duoc, "m": b.giong,
             "pt": b.phan_tram, "gt": b.gold_total, "gd": b.gold_correct, "ok": b.dat, "th": b.attempt_id in thuong,
             "n": b.da_tra_loi, "at": luc},
        )
    await s.execute(
        text("UPDATE project_collections SET graded_at = :at, majority_used = :mj, "
             "freetext_state = CASE WHEN CAST(:pp AS varchar) IS NULL THEN NULL ELSE 'done' END, freetext_method = CAST(:pp AS varchar) "
             "WHERE project_id = :p"),
        {"p": project_id, "at": luc, "mj": kq.dung_da_so, "pp": None if not mo else mo.phuong_phap},
    )

    await outbox.ghi(s, ProjectGraded(
        project_id=project_id, pass_threshold_percent=du_an.pass_threshold_percent, majority_used=kq.dung_da_so,
        attempts=[GradedAttempt(
            attempt_id=uuid.UUID(b.attempt_id), labeler_id=uuid.UUID(b.labeler_id), comparable_count=b.so_duoc,
            matched_count=b.giong, agreement_percent=b.phan_tram, gold_total=b.gold_total, gold_correct=b.gold_correct,
            passed=b.dat, rewarded=b.attempt_id in thuong, answered_count=b.da_tra_loi) for b in kq.bai],
        graded_at=luc), env.correlation_id, env.event_id)

    # Cap nhat uy tin theo THU TU CO DINH (labeler_id tang dan): hai du an cham song song cung hai labeler
    # A, B ma mot ben khoa A→B, ben kia B→A thi deadlock.
    for labeler in sorted({uuid.UUID(b.labeler_id) for b in kq.bai}, key=str):
        await cap_nhat_uy_tin(s, labeler, env)

    log.info("Cham du an %s: %s bai, %s dat, %s duoc thuong (da so: %s)", project_id, len(kq.bai),
             sum(1 for b in kq.bai if b.dat), len(thuong), kq.dung_da_so)


def cong_cu_mo(schema: dict) -> list[str]:
    return [t["name"] for t in schema.get("tools", []) if t.get("kind") == "freeText"]


async def _tham_so_mo(s: AsyncSession, project_id: uuid.UUID, schema: dict, nhan: dict, finished_at, env):
    """None = du an khong co cau tra loi mo; False = CHUA cham duoc (cho ml-svc); ThamSoMoDuAn = cham."""
    ten = cong_cu_mo(schema)
    if not ten:
        return None

    can = {(a, sm, t) for (a, sm), d in nhan.items() for t in ten if cau_mo.van_ban(d.get(t))}
    co: dict[tuple[str, str, str], list[float]] = {}
    if can:
        for d in (await s.execute(
                text("SELECT attempt_id, sample_id, tool, vector FROM freetext_vectors "
                     "WHERE project_id = :p AND vector IS NOT NULL"), {"p": project_id})).all():
            co[(str(d.attempt_id), str(d.sample_id), d.tool)] = list(d.vector)
    thieu = len(can - co.keys())

    if thieu == 0:
        return cham.ThamSoMoDuAn(cau_mo.THEO_NGHIA, settings_store.so_thuc(settings_store.FREETEXT_SIMILARITY), co)

    han = finished_at + timedelta(seconds=settings_store.giay(settings_store.FREETEXT_ANALYSIS_TIMEOUT))
    if ml_client.co_cau_hinh() and datetime.now(timezone.utc) < han:
        await s.execute(
            text("UPDATE project_collections SET freetext_state = 'pending', "
                 "freetext_correlation_id = COALESCE(freetext_correlation_id, :c) WHERE project_id = :p"),
            {"p": project_id, "c": env.correlation_id})
        log.info("Du an %s: con %s / %s cau tra loi mo chua phan tich — cho ml-svc (han %s)", project_id, thieu, len(can), han)
        return False

    log.warning("Du an %s: %s / %s cau tra loi mo chua co vector (ml-svc %s) — cham bang so chu", project_id, thieu, len(can),
                "qua han" if ml_client.co_cau_hinh() else "khong cau hinh")
    return cham.ThamSoMoDuAn(cau_mo.THEO_CHU, settings_store.so_thuc(settings_store.FREETEXT_LEXICAL_SIMILARITY))


# =====================================================================
# UY TIN
# =====================================================================


async def ds_skill_cua(s: AsyncSession, labeler_id: uuid.UUID) -> float | None:
    """Do tin cay DS trung binh theo so nhan, tren moi du an / cong cu da chay DS."""
    dong = (await s.execute(
        text("SELECT SUM(skill * label_count) / NULLIF(SUM(label_count), 0) AS skill FROM ds_skills WHERE labeler_id = :l"),
        {"l": labeler_id})).first()
    return float(dong.skill) if dong is not None and dong.skill is not None else None


NHOM_KHOA_UY_TIN = 7003  # pg_advisory_xact_lock theo labeler — tach khoi khoa theo du an (7004)


async def cap_nhat_uy_tin(s: AsyncSession, labeler_id: uuid.UUID, env: Envelope | None) -> int | None:
    """Tinh lai uy tin tu bang chung goc; doi thi luu va phat reputation.changed.

    Bang chung = bai DA CHAM (attempt_grades: cau giong da so / cau so duoc, cau vang) + du lieu cu truoc
    du an lam trung tam (label_agreements, gold_answers) — xoa du lieu cu la doi diem cua nguoi ta."""
    # KHOA THEO LABELER TRUOC KHI DEM: hai du an cua cung labeler cham song song thi ben ghi sau co the
    # mang so dem thieu. Khong deadlock: noi goi nhieu labeler luon theo thu tu labeler_id tang dan.
    await s.execute(text("SELECT pg_advisory_xact_lock(:nhom, hashtext(:l))"), {"nhom": NHOM_KHOA_UY_TIN, "l": str(labeler_id)})

    gold = (await s.execute(text(
        "SELECT "
        "  (SELECT COUNT(*) FROM gold_answers WHERE labeler_id = :l) "
        "    + COALESCE((SELECT SUM(gold_total) FROM attempt_grades WHERE labeler_id = :l), 0) AS tong, "
        "  (SELECT COUNT(*) FILTER (WHERE correct) FROM gold_answers WHERE labeler_id = :l) "
        "    + COALESCE((SELECT SUM(gold_correct) FROM attempt_grades WHERE labeler_id = :l), 0) AS dung"),
        {"l": labeler_id})).one()
    khop = (await s.execute(text(
        "SELECT "
        "  (SELECT COUNT(*) FROM label_agreements WHERE labeler_id = :l) "
        "    + COALESCE((SELECT SUM(comparable_count) FROM attempt_grades WHERE labeler_id = :l), 0) AS tong, "
        "  (SELECT COUNT(*) FILTER (WHERE agrees) FROM label_agreements WHERE labeler_id = :l) "
        "    + COALESCE((SELECT SUM(matched_count) FROM attempt_grades WHERE labeler_id = :l), 0) AS dung"),
        {"l": labeler_id})).one()
    ds = await ds_skill_cua(s, labeler_id)

    moi = reputation.tinh(int(gold.dung), int(gold.tong), int(khop.dung), int(khop.tong), ds, reputation.tham_so_hien_tai())
    if moi is None:
        return None

    cu = (await s.execute(
        text("SELECT reputation FROM reputations WHERE labeler_id = :l FOR UPDATE"), {"l": labeler_id})).first()

    await s.execute(
        text(
            "INSERT INTO reputations (labeler_id, reputation, gold_total, gold_correct, agree_total, agree_count, ds_skill, updated_at) "
            "VALUES (:l, :r, :gt, :gd, :kt, :kd, :ds, now()) "
            "ON CONFLICT (labeler_id) DO UPDATE SET reputation = EXCLUDED.reputation, gold_total = EXCLUDED.gold_total, "
            "gold_correct = EXCLUDED.gold_correct, agree_total = EXCLUDED.agree_total, agree_count = EXCLUDED.agree_count, "
            "ds_skill = EXCLUDED.ds_skill, updated_at = EXCLUDED.updated_at"
        ),
        {"l": labeler_id, "r": moi, "gt": int(gold.tong), "gd": int(gold.dung), "kt": int(khop.tong), "kd": int(khop.dung), "ds": ds},
    )

    if cu is None or cu.reputation != moi:
        correlation = env.correlation_id if env is not None else uuid.uuid4()
        causation = env.event_id if env is not None else None
        await outbox.ghi(s, ReputationChanged(user_id=labeler_id, reputation=moi), correlation, causation)
        log.info("Labeler %s: uy tin %s → %s", labeler_id, None if cu is None else cu.reputation, moi)

    return moi
