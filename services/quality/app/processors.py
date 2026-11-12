"""Xu ly event — moi ham chay TRONG transaction consumer da mo (khong commit).

KHONG BAN SAO (kien truc 3.22): de bai, bai nop va nhan doc thang tu project-svc qua gRPC (project_client).
Consumer chi ghi viec vao hang cho / moc cua chinh quality roi tra loi — attempt.submitted khong goi gRPC:
project-svc tam sap thi message khong bi giao lai den het luot roi vao DLQ; worker (viec_cho.py) thu lai.

  attempt.submitted (v2, chi la tin hieu) → pending_attempts (xet gian lan, nhung cau tra loi mo). KHONG dung
      den DS: Dawid-Skene chay mot lan sau khi cham (project_gradings.ds_at, ds_batch.py) — luc nop bai chua du
      bai cua moi labeler.
  project.collection_finished → project_gradings (yeu cau cham) roi thu cham ngay; chua cham duoc (gRPC loi,
      cho ml-svc) thi worker cham lai. Moi bai trong dot da commit o project-svc TRUOC event nay (outbox cung
      transaction dong thu bai) nen khong con phai doi attempt.submitted nhu truoc.
  project.completed / project.cancelled → project_archives.closed_at (dong ho luu tru).

UY TIN khong tinh trong transaction cham: bang chung (so cau so duoc / giong / cau vang cua moi bai da cham) nam
tren project.attempts — quality khong giu ban sao. Worker cho DS cua du an chay xong (ds_at) va project-svc ghi
xong project.graded roi moi xep labeler vao reputation_dirty va doc tong qua gRPC (viec_cho.py).
"""

import json
import logging
import uuid
from datetime import datetime, timedelta, timezone

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import cau_mo, cham, consensus, ml_client, project_client, reputation, settings_store
from app.contracts import AttemptSubmitted, GradedAttempt, ProjectCollectionFinished, ProjectGraded, ReputationChanged
from app.messaging import outbox
from app.messaging.envelope import Envelope

log = logging.getLogger(__name__)


async def project_dong(s: AsyncSession, env: Envelope) -> None:
    """project.completed / project.cancelled: ghi moc dong (lan dau thang) — archive.py dem thoi han tu day."""
    await s.execute(
        text("INSERT INTO project_archives (project_id, closed_at) VALUES (:p, :at) ON CONFLICT (project_id) DO NOTHING"),
        {"p": env.payload.project_id, "at": env.occurred_at},
    )


async def da_luu_tru(s: AsyncSession, project_id: uuid.UUID) -> bool:
    """Du an da chup bao cao va (dang) xoa ket qua trung gian — event den tre cua no khong duoc ghi lai du lieu."""
    return (await s.execute(
        text("SELECT 1 FROM project_archives WHERE project_id = :p AND archived_at IS NOT NULL"), {"p": project_id})).first() is not None


async def attempt_submitted(s: AsyncSession, env: Envelope[AttemptSubmitted]) -> None:
    """Xep bai vao hang cho (gian lan, cau tra loi mo). Khong goi gRPC. Moi bai mot dong rieng — cac bai nop
    cung luc vao mot du an khong tranh khoa chung dong nao."""
    p = env.payload
    if await da_luu_tru(s, p.project_id):
        log.info("Bo qua bai %s: du an %s da luu tru", p.attempt_id, p.project_id)
        return

    await s.execute(
        text("INSERT INTO pending_attempts (attempt_id, project_id, queued_at) VALUES (:a, :p, now()) "
             "ON CONFLICT (attempt_id) DO NOTHING"),
        {"a": p.attempt_id, "p": p.project_id})


async def collection_finished(s: AsyncSession, env: Envelope[ProjectCollectionFinished]) -> None:
    p = env.payload
    await s.execute(
        text("INSERT INTO project_gradings (project_id, requested_at, correlation_id) VALUES (:p, now(), :c) "
             "ON CONFLICT (project_id) DO NOTHING"),
        {"p": p.project_id, "c": env.correlation_id})
    try:
        await thu_cham(s, p.project_id, env)
    except project_client.KhongGoiDuoc as ex:
        # Yeu cau cham da ghi (commit cung processed_events); worker cham lai khi project-svc tra loi.
        log.warning("Du an %s: chua doc duoc bai nop tu project-svc (%s) — worker cham lai", p.project_id, ex)


# =====================================================================
# CHAM CA DU AN
# =====================================================================


NHOM_KHOA_DU_AN = 7004  # pg_advisory_xact_lock theo du an


async def thu_cham(s: AsyncSession, project_id: uuid.UUID, env) -> None:
    """Cham ca du an MOT lan (project_gradings.graded_at). Doc de bai + bai nop + nhan qua gRPC.

    project_client.KhongGoiDuoc → nem ra (noi goi de lai cho worker). Du an da dong thu bai ma project-svc khong
    tra dieu khoan → loi du lieu, nem RuntimeError (consumer giao lai roi DLQ — khong chi tien sai)."""
    # KHOA THEO DU AN: consumer va worker cham lai co the chay cung luc → xep hang, ben sau thay graded_at.
    await s.execute(text("SELECT pg_advisory_xact_lock(:nhom, hashtext(:p))"), {"nhom": NHOM_KHOA_DU_AN, "p": str(project_id)})

    dot = (await s.execute(
        text("SELECT graded_at, requested_at FROM project_gradings WHERE project_id = :p"), {"p": project_id})).first()
    if dot is None or dot.graded_at is not None:
        return  # chua dong thu bai, hoac da cham

    try:
        du_an = await project_client.du_an(project_id)
    except project_client.KhongTimThay as ex:
        raise RuntimeError(f"Du an {project_id} da dong thu bai nhung project-svc khong tra dieu khoan — khong cham duoc.") from ex
    if du_an.finish_reason is None:
        raise RuntimeError(f"Du an {project_id}: project-svc chua dong thu bai — event lech trang thai.")

    bai = await project_client.bai_da_nop(project_id)
    dong_nhan = await project_client.nhan(project_id)
    nhan = {(str(d.attempt_id), str(d.sample_id)): d.data for d in dong_nhan}
    metadata: dict[str, dict | None] = {}
    for d in dong_nhan:
        if d.sample_metadata and str(d.sample_id) not in metadata:
            metadata[str(d.sample_id)] = d.sample_metadata

    # Cau tra loi mo (3.19): cham theo nghia khi ml-svc da nhung du; chua du thi cho (worker cham lai), qua han /
    # khong cau hinh ml-svc thi so chu.
    mo = await _tham_so_mo(s, project_id, du_an.label_schema, nhan, du_an.collection_finished_at or dot.requested_at)
    if mo is False:
        return

    tham_so = consensus.ThamSoGop(
        nguong=settings_store.nguong_gop(), f1_toi_thieu=settings_store.so_thuc(settings_store.CONSENSUS_MIN_F1))
    kq = cham.cham_du_an(
        du_an.label_schema,
        [cham.Bai(str(b.attempt_id), str(b.labeler_id), b.sample_ids, b.gold_total, b.gold_correct) for b in bai],
        nhan, metadata, set(du_an.excluded_sample_ids), du_an.pass_threshold_percent, tham_so, mo or None)

    # So suat = so nguoi DUOC THUONG: n bai dat nop som nhat (kq.bai giu thu tu luc nop cua `bai`); du an bat
    # lap suat trong thi suat con lai trao cho bai giong da so nhieu nhat.
    thuong = cham.chon_duoc_thuong(kq.bai, du_an.slots, du_an.fill_slots_min_percent)

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
    await s.execute(
        text("UPDATE project_gradings SET graded_at = :at, majority_used = :mj, "
             "freetext_state = CASE WHEN CAST(:pp AS varchar) IS NULL THEN NULL ELSE 'done' END, freetext_method = CAST(:pp AS varchar) "
             "WHERE project_id = :p"),
        {"p": project_id, "at": luc, "mj": kq.dung_da_so, "pp": None if not mo else mo.phuong_phap},
    )

    await outbox.ghi(s, ProjectGraded(
        project_id=project_id, owner_id=du_an.owner_id, pass_threshold_percent=du_an.pass_threshold_percent, majority_used=kq.dung_da_so,
        attempts=[GradedAttempt(
            attempt_id=uuid.UUID(b.attempt_id), labeler_id=uuid.UUID(b.labeler_id), comparable_count=b.so_duoc,
            matched_count=b.giong, agreement_percent=b.phan_tram, gold_total=b.gold_total, gold_correct=b.gold_correct,
            passed=b.dat, rewarded=b.attempt_id in thuong, answered_count=b.da_tra_loi) for b in kq.bai],
        graded_at=luc), env.correlation_id, env.event_id)

    log.info("Cham du an %s: %s bai, %s dat, %s duoc thuong (da so: %s)", project_id, len(kq.bai),
             sum(1 for b in kq.bai if b.dat), len(thuong), kq.dung_da_so)


def cong_cu_mo(schema: dict) -> list[str]:
    return [t["name"] for t in schema.get("tools", []) if t.get("kind") == "freeText"]


async def _tham_so_mo(s: AsyncSession, project_id: uuid.UUID, schema: dict, nhan: dict, finished_at):
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
            text("UPDATE project_gradings SET freetext_state = 'pending' WHERE project_id = :p"), {"p": project_id})
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


async def xep_tinh_uy_tin(s: AsyncSession, labeler_ids, correlation_id: uuid.UUID | None) -> None:
    """Xep labeler vao reputation_dirty (worker tinh lai). Thu tu co dinh: hai transaction xep chung nhieu
    labeler khong khoa cheo nhau."""
    for labeler in sorted(set(labeler_ids), key=str):
        await s.execute(
            text("INSERT INTO reputation_dirty (labeler_id, dirty_at, correlation_id) VALUES (:l, now(), :c) "
                 "ON CONFLICT (labeler_id) DO UPDATE SET dirty_at = EXCLUDED.dirty_at, "
                 "correlation_id = COALESCE(EXCLUDED.correlation_id, reputation_dirty.correlation_id)"),
            {"l": labeler, "c": correlation_id})


async def cap_nhat_uy_tin(s: AsyncSession, labeler_id: uuid.UUID, bang_chung: project_client.BangChung,
                          correlation_id: uuid.UUID | None = None) -> int | None:
    """Tinh lai uy tin tu bang chung goc; doi thi luu va phat reputation.changed.

    Bang chung = bai DA CHAM tren moi du an (project_client.bang_chung — doc tu project.attempts) + do tin cay
    DS (ds_skills, cua quality). Noi goi giu dong reputation_dirty (FOR UPDATE SKIP LOCKED) nen moi labeler chi
    mot worker tinh mot luc."""
    ds = await ds_skill_cua(s, labeler_id)
    b = bang_chung
    moi = reputation.tinh(b.gold_correct, b.gold_total, b.matched_total, b.comparable_total, ds, reputation.tham_so_hien_tai())
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
        {"l": labeler_id, "r": moi, "gt": b.gold_total, "gd": b.gold_correct, "kt": b.comparable_total, "kd": b.matched_total, "ds": ds},
    )

    if cu is None or cu.reputation != moi:
        await outbox.ghi(s, ReputationChanged(user_id=labeler_id, reputation=moi), correlation_id or uuid.uuid4(), None)
        log.info("Labeler %s: uy tin %s → %s", labeler_id, None if cu is None else cu.reputation, moi)

    return moi
