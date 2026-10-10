"""Xu ly event — moi ham chay TRONG transaction consumer da mo (khong commit).

Thu tu event khong dam bao: annotation.submitted (tu annotation-svc) va
task.redundancy_reached (tu task-svc) di hai queue khac nhau, cai nao den truoc
cung duoc. Ca hai deu goi danh_gia_task(): chi tinh khi DA BIET target VA da du
nhan; moi (task, target) chi tinh MOT lan (khoa chinh consensus_rounds).
"""

import json
import logging
import uuid
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import consensus, reputation, settings_store
from app.contracts import (
    AnnotationSubmitted,
    ConsensusReached,
    ConsensusVote,
    GoldAnswered,
    ProjectPublished,
    RedundancyIncreaseRequested,
    ReputationChanged,
    TaskRedundancyReached,
)
from app.messaging import outbox
from app.messaging.envelope import Envelope

log = logging.getLogger(__name__)


async def project_published(s: AsyncSession, env: Envelope[ProjectPublished]) -> None:
    p = env.payload
    # Dieu khoan bat bien sau publish: da co thi thoi.
    await s.execute(
        text(
            "INSERT INTO projects (project_id, owner_id, modality, label_schema, redundancy, max_redundancy, published_at) "
            "VALUES (:id, :owner, :modality, CAST(:schema AS jsonb), :r, :max_r, :at) ON CONFLICT (project_id) DO NOTHING"
        ),
        {"id": p.project_id, "owner": p.owner_id, "modality": p.modality, "schema": json.dumps(p.label_schema),
         "r": p.redundancy, "max_r": max(p.max_redundancy, p.redundancy), "at": env.occurred_at},
    )


async def annotation_submitted(s: AsyncSession, env: Envelope[AnnotationSubmitted]) -> None:
    p = env.payload
    if p.labeler_id is None:
        # Khach vang lai o cong link (P2) — khong co uy tin de cap nhat, khong tinh dong thuan.
        return

    await s.execute(
        text(
            "INSERT INTO labels (annotation_id, project_id, task_id, sample_id, labeler_id, data, received_at) "
            "VALUES (:a, :p, :t, :sm, :l, CAST(:d AS jsonb), :at) ON CONFLICT (annotation_id) DO NOTHING"
        ),
        {"a": p.annotation_id, "p": p.project_id, "t": p.task_id, "sm": p.sample_id, "l": p.labeler_id,
         "d": json.dumps(p.label_payload.data), "at": env.occurred_at},
    )
    await danh_gia_task(s, p.task_id, env)


async def task_redundancy_reached(s: AsyncSession, env: Envelope[TaskRedundancyReached]) -> None:
    p = env.payload
    # Target chi tang (redundancy thich ung): ban giao lai / cu hon khong ha target.
    await s.execute(
        text(
            "INSERT INTO task_targets (task_id, project_id, sample_id, target, reached_at) VALUES (:t, :p, :sm, :n, :at) "
            "ON CONFLICT (task_id) DO UPDATE SET target = EXCLUDED.target, reached_at = EXCLUDED.reached_at "
            "WHERE task_targets.target < EXCLUDED.target"
        ),
        {"t": p.task_id, "p": p.project_id, "sm": p.sample_id, "n": p.redundancy, "at": env.occurred_at},
    )
    await danh_gia_task(s, p.task_id, env)


async def gold_answered(s: AsyncSession, env: Envelope[GoldAnswered]) -> None:
    p = env.payload
    ket_qua = await s.execute(
        text(
            "INSERT INTO gold_answers (assignment_id, project_id, sample_id, labeler_id, correct, answered_at) "
            "VALUES (:a, :p, :sm, :l, :c, :at) ON CONFLICT (assignment_id) DO NOTHING"
        ),
        {"a": p.assignment_id, "p": p.project_id, "sm": p.sample_id, "l": p.labeler_id, "c": p.correct, "at": p.answered_at},
    )
    if ket_qua.rowcount:
        await cap_nhat_uy_tin(s, p.labeler_id, env)


# =====================================================================
# DONG THUAN
# =====================================================================


NHOM_KHOA_TASK = 7002  # tham so dau cua pg_advisory_xact_lock — tach khoi khoa cua service khac


async def danh_gia_task(s: AsyncSession, task_id: uuid.UUID, env: Envelope) -> None:
    # KHOA THEO TASK truoc khi dem: hai nhan cuoi cua cung task (hoac nhan cuoi + target) xu ly
    # SONG SONG trong hai transaction thi moi ben chi thay nhan cua minh (ben kia chua commit),
    # deu dem thieu va KHONG BEN NAO tinh dong thuan — task ket vinh vien (gap khi chay E2E:
    # hai nhan den cach nhau 19 ms). Khoa xep hang hai ben; ben sau lay khoa khi ben truoc da
    # commit nen dem du (READ COMMITTED). Khoa tu nha khi transaction cua consumer ket thuc.
    await s.execute(text("SELECT pg_advisory_xact_lock(:nhom, hashtext(:t))"), {"nhom": NHOM_KHOA_TASK, "t": str(task_id)})

    muc_tieu = (await s.execute(
        text("SELECT project_id, sample_id, target FROM task_targets WHERE task_id = :t"), {"t": task_id})).first()
    if muc_tieu is None:
        return  # chua biet task can bao nhieu nguoi — doi task.redundancy_reached

    du_an = (await s.execute(
        text("SELECT label_schema, max_redundancy FROM projects WHERE project_id = :p"), {"p": muc_tieu.project_id})).first()
    if du_an is None:
        # Du an publish TRUOC khi quality-svc ton tai (hoac truoc khi quality nhan
        # project.published) — khong co tap nhan va tran redundancy thi khong tinh
        # duoc. Bo qua kem canh bao thay vi nem loi: nem loi la moi nhan cua du an cu
        # quay vong 5 lan roi vao DLQ, ma thu lai bao nhieu lan cung khong co du an.
        log.warning("Bo qua dong thuan task %s: chua co ban sao du an %s", task_id, muc_tieu.project_id)
        return

    dong = (await s.execute(
        text("SELECT annotation_id, labeler_id, data FROM labels WHERE task_id = :t ORDER BY received_at"),
        {"t": task_id})).all()
    if len(dong) < muc_tieu.target:
        return  # chua du nhan — doi annotation.submitted con lai

    da_tinh = (await s.execute(
        text("SELECT 1 FROM consensus_rounds WHERE task_id = :t AND target = :n"),
        {"t": task_id, "n": muc_tieu.target})).first()
    if da_tinh is not None:
        return

    schema = du_an.label_schema if isinstance(du_an.label_schema, dict) else json.loads(du_an.label_schema)
    nhan = [consensus.Nhan(str(d.annotation_id), str(d.labeler_id), d.data if isinstance(d.data, dict) else json.loads(d.data))
            for d in dong]
    cs = await chinh_sach_hien_tai(s, [d.labeler_id for d in dong])
    kq = consensus.danh_gia(schema, nhan, muc_tieu.target, du_an.max_redundancy, cs)

    trang_thai = "moreRequested" if kq.xin_them is not None else kq.status
    await s.execute(
        text(
            "INSERT INTO consensus_rounds (task_id, target, project_id, sample_id, status, final, label_count, decided_at) "
            "VALUES (:t, :n, :p, :sm, :st, CAST(:f AS jsonb), :c, now())"
        ),
        {"t": task_id, "n": muc_tieu.target, "p": muc_tieu.project_id, "sm": muc_tieu.sample_id, "st": trang_thai,
         "f": json.dumps(kq.final) if kq.final is not None else None, "c": len(nhan)},
    )

    if kq.xin_them is not None:
        await outbox.ghi(s, RedundancyIncreaseRequested(
            task_id=task_id, project_id=muc_tieu.project_id, sample_id=muc_tieu.sample_id,
            new_redundancy=kq.xin_them,
            reason=f"Tranh chap sau {len(nhan)} nhan: khong lua chon nao qua ban."),
            env.correlation_id, env.event_id)
        log.info("Task %s tranh chap voi %s nhan → xin redundancy %s", task_id, len(nhan), kq.xin_them)
        return

    labeler_cua: dict[str, uuid.UUID] = {str(d.annotation_id): d.labeler_id for d in dong}
    await outbox.ghi(s, ConsensusReached(
        task_id=task_id, project_id=muc_tieu.project_id, sample_id=muc_tieu.sample_id, status=kq.status, final=kq.final,
        votes=[ConsensusVote(annotation_id=uuid.UUID(a), labeler_id=labeler_cua[a], agrees=v) for a, v in kq.agrees.items()]),
        env.correlation_id, env.event_id)

    for a, v in kq.agrees.items():
        if v is None:
            continue
        await s.execute(
            text(
                "INSERT INTO label_agreements (annotation_id, project_id, task_id, labeler_id, agrees, decided_at) "
                "VALUES (:a, :p, :t, :l, :v, now()) "
                "ON CONFLICT (annotation_id) DO UPDATE SET agrees = EXCLUDED.agrees, decided_at = EXCLUDED.decided_at"
            ),
            {"a": uuid.UUID(a), "p": muc_tieu.project_id, "t": task_id, "l": labeler_cua[a], "v": v},
        )

    for labeler in {labeler_cua[a] for a, v in kq.agrees.items() if v is not None}:
        await cap_nhat_uy_tin(s, labeler, env)

    log.info("Task %s: %s (%s nhan)", task_id, kq.status, len(nhan))


# =====================================================================
# UY TIN
# =====================================================================


async def chinh_sach_hien_tai(s: AsyncSession, labeler_ids: list[uuid.UUID]) -> consensus.ChinhSach:
    """
    Chinh sach dung theo setting quality.redundancy_policy (NC-D-01). Voi posterior / voi can
    do chinh xac tung labeler: do tin cay Dawid–Skene neu co, khong thi ti le dung cau vang
    LAM MUOT ve tien nghiem (cung cach tinh uy tin) — labeler moi khong bi tin qua muc.
    """
    ten = settings_store.chuoi(settings_store.REDUNDANCY_POLICY)
    ts = reputation.tham_so_hien_tai()
    if ten == "majority":
        return consensus.ChinhSach()

    do_cx: dict[str, float] = {}
    for l in set(labeler_ids):
        ds = await ds_skill_cua(s, l)
        if ds is not None:
            do_cx[str(l)] = ds
            continue
        g = (await s.execute(
            text("SELECT COUNT(*) AS tong, COUNT(*) FILTER (WHERE correct) AS dung FROM gold_answers WHERE labeler_id = :l"),
            {"l": l})).one()
        do_cx[str(l)] = reputation.lam_muot(g.dung, g.tong, ts)

    return consensus.ChinhSach(
        ten=ten,
        nguong=settings_store.so_thuc(settings_store.POSTERIOR_TARGET),
        ti_le_gia_tri=settings_store.so_thuc(settings_store.VOI_VALUE_RATIO),
        do_chinh_xac=do_cx,
        mac_dinh=ts.tien_nghiem,
    )


async def ds_skill_cua(s: AsyncSession, labeler_id: uuid.UUID) -> float | None:
    """Do tin cay DS trung binh theo so nhan, tren moi du an / cong cu da chay DS."""
    dong = (await s.execute(
        text("SELECT SUM(skill * label_count) / NULLIF(SUM(label_count), 0) AS skill FROM ds_skills WHERE labeler_id = :l"),
        {"l": labeler_id})).first()
    return float(dong.skill) if dong is not None and dong.skill is not None else None


async def cap_nhat_uy_tin(s: AsyncSession, labeler_id: uuid.UUID, env: Envelope | None) -> int | None:
    """Tinh lai uy tin tu bang chung goc; doi thi luu va phat reputation.changed."""
    gold = (await s.execute(
        text("SELECT COUNT(*) AS tong, COUNT(*) FILTER (WHERE correct) AS dung FROM gold_answers WHERE labeler_id = :l"),
        {"l": labeler_id})).one()
    khop = (await s.execute(
        text("SELECT COUNT(*) AS tong, COUNT(*) FILTER (WHERE agrees) AS dung FROM label_agreements WHERE labeler_id = :l"),
        {"l": labeler_id})).one()
    ds = await ds_skill_cua(s, labeler_id)

    moi = reputation.tinh(gold.dung, gold.tong, khop.dung, khop.tong, ds, reputation.tham_so_hien_tai())
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
        {"l": labeler_id, "r": moi, "gt": gold.tong, "gd": gold.dung, "kt": khop.tong, "kd": khop.dung, "ds": ds},
    )

    if cu is None or cu.reputation != moi:
        correlation = env.correlation_id if env is not None else uuid.uuid4()
        causation = env.event_id if env is not None else None
        await outbox.ghi(s, ReputationChanged(user_id=labeler_id, reputation=moi), correlation, causation)
        log.info("Labeler %s: uy tin %s → %s", labeler_id, None if cu is None else cu.reputation, moi)

    return moi
