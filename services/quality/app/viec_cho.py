"""Worker VIEC CHO (kien truc 3.22): phan viec can doc du lieu cua project-svc qua gRPC.

Consumer khong goi gRPC (processors.py) — chi ghi hang cho. Moi vong (quality.pending_work_interval):
  1. XET GIAN LAN bai vua nop (pending_attempts.fraud_checked_at NULL) — fraud.xet_bai.
  2. CHAM LAI du an da dong thu bai ma chua cham xong (project_gradings.graded_at NULL): lan dau project-svc
     khong tra loi, hoac dang cho ml-svc phan tich cau tra loi mo. thu_cham tu quyet: du vector → so nghia;
     qua han → so chu.
  3. AP DUNG CHAM: du an quality da cham ma project-svc da ghi xong project.graded (gRPC thay moi bai graded)
     → xep labeler cua du an vao reputation_dirty. Truoc luc do tong bang chung doc tu project-svc con thieu
     dung du an vua cham, nen khong tinh uy tin trong transaction cham.
  4. TINH UY TIN cho labeler trong reputation_dirty: bang chung cong don moi du an doc qua gRPC
     (GetLabelerEvidence — quality khong giu ban sao bai da cham) + ds_skills.
  5. Xoa dong pending_attempts da xong ca hai viec (gian lan + cau mo).

project-svc khong tra loi (KhongGoiDuoc) → dung buoc do, vong sau lam tiep; khong mat viec vi dong cho chi
danh dau xong SAU khi xu ly thanh cong (cung transaction). Nhieu instance: FOR UPDATE SKIP LOCKED / khoa du an.
"""

import asyncio
import logging
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace

from sqlalchemy import text

from app import fraud, processors, project_client, settings_store
from app.db import session_factory

log = logging.getLogger(__name__)

LO = 50
LO_UY_TIN = 200
CHO_AP_DUNG_LAU = timedelta(minutes=10)  # du an cham xong qua lau ma project-svc chua ghi ket qua → canh bao


async def xet_gian_lan() -> int:
    """Xet toi da LO bai. Tra so bai da xet."""
    da_xet = 0
    for _ in range(LO):
        async with session_factory()() as s, s.begin():
            d = (await s.execute(text(
                "SELECT attempt_id, project_id FROM pending_attempts WHERE fraud_checked_at IS NULL "
                "ORDER BY queued_at LIMIT 1 FOR UPDATE SKIP LOCKED"))).first()
            if d is None:
                break
            try:
                async with s.begin_nested():
                    await fraud.xet_bai(s, d.attempt_id, d.project_id)
            except project_client.KhongGoiDuoc:
                raise  # tam thoi: giu dong, vong sau xet lai
            except project_client.KhongTimThay:
                log.warning("Xet gian lan: project-svc khong con du an %s — bo qua bai %s", d.project_id, d.attempt_id)
            except Exception:  # noqa: BLE001 — bai loi khong duoc chan dau hang cho (xet gian lan la best-effort)
                log.exception("Xet gian lan bai %s that bai — bo qua", d.attempt_id)
            await s.execute(text("UPDATE pending_attempts SET fraud_checked_at = now() WHERE attempt_id = :a"),
                            {"a": d.attempt_id})
        da_xet += 1
    return da_xet


async def cham_lai() -> int:
    """Thu cham cac du an chua cham xong. Tra so du an vua cham xong."""
    async with session_factory()() as s:
        cho = (await s.execute(text(
            "SELECT project_id, correlation_id FROM project_gradings WHERE graded_at IS NULL ORDER BY requested_at"))).all()
    da_cham = 0
    for d in cho:
        nguon = SimpleNamespace(correlation_id=d.correlation_id, event_id=None)
        try:
            async with session_factory()() as s, s.begin():
                await processors.thu_cham(s, d.project_id, nguon)
        except project_client.KhongGoiDuoc:
            raise  # project-svc chua tra loi: du an sau cung se loi — dung vong nay
        except Exception:  # noqa: BLE001 — du an loi du lieu khong chan du an khac
            log.exception("Cham lai du an %s that bai", d.project_id)
            continue
        async with session_factory()() as s:
            if (await s.execute(text("SELECT graded_at FROM project_gradings WHERE project_id = :p"),
                                {"p": d.project_id})).scalar() is not None:
                da_cham += 1
    return da_cham


async def ap_dung_cham() -> int:
    """Xep labeler cua du an da cham ma project-svc da ghi ket qua vao reputation_dirty. Tra so du an."""
    xong = 0
    async with session_factory()() as s, s.begin():
        cho = (await s.execute(text(
            "SELECT project_id, correlation_id, graded_at FROM project_gradings "
            "WHERE graded_at IS NOT NULL AND reputation_at IS NULL ORDER BY graded_at LIMIT :n FOR UPDATE SKIP LOCKED"),
            {"n": LO})).all()
        for d in cho:
            try:
                bai = await project_client.bai_da_nop(d.project_id)
            except project_client.KhongTimThay:
                bai = []
            if bai and not all(b.graded for b in bai):
                if datetime.now(timezone.utc) - d.graded_at > CHO_AP_DUNG_LAU:
                    log.warning("Du an %s: cham tu %s ma project-svc chua ghi project.graded — chua tinh uy tin",
                                d.project_id, d.graded_at)
                continue
            await processors.xep_tinh_uy_tin(s, {b.labeler_id for b in bai}, d.correlation_id)
            await s.execute(text("UPDATE project_gradings SET reputation_at = now() WHERE project_id = :p"),
                            {"p": d.project_id})
            xong += 1
    return xong


async def tinh_uy_tin() -> int:
    """Tinh lai uy tin cho labeler trong reputation_dirty (tung lo). Tra so labeler."""
    tong = 0
    while True:
        async with session_factory()() as s, s.begin():
            dong = (await s.execute(text(
                "SELECT labeler_id, correlation_id FROM reputation_dirty ORDER BY dirty_at LIMIT :n FOR UPDATE SKIP LOCKED"),
                {"n": LO_UY_TIN})).all()
            if not dong:
                return tong
            bang_chung = await project_client.bang_chung([d.labeler_id for d in dong])
            for d in sorted(dong, key=lambda x: str(x.labeler_id)):
                await processors.cap_nhat_uy_tin(
                    s, d.labeler_id, bang_chung.get(d.labeler_id, project_client.BangChung()), d.correlation_id)
            # Dong bi xep lai trong luc tinh (upsert cho khoa dong) → sau commit no chen lai, vong sau tinh tiep.
            await s.execute(text("DELETE FROM reputation_dirty WHERE labeler_id = ANY(:l)"),
                            {"l": [d.labeler_id for d in dong]})
        tong += len(dong)
        if len(dong) < LO_UY_TIN:
            return tong


async def don_xong() -> int:
    async with session_factory()() as s, s.begin():
        return (await s.execute(text(
            "DELETE FROM pending_attempts WHERE fraud_checked_at IS NOT NULL AND freetext_done_at IS NOT NULL"))).rowcount


async def mot_vong() -> None:
    for ten, viec in (("xet gian lan", xet_gian_lan), ("cham lai", cham_lai), ("ap dung cham", ap_dung_cham),
                      ("tinh uy tin", tinh_uy_tin)):
        try:
            so = await viec()
            if so:
                log.info("Viec cho — %s: %s", ten, so)
        except project_client.KhongGoiDuoc as ex:
            log.warning("Viec cho — %s: project-svc chua tra loi (%s), vong sau thu lai", ten, ex)
        except Exception:  # noqa: BLE001 — mot viec hong khong chan viec kia
            log.exception("Viec cho — %s that bai, vong sau thu lai", ten)
    await don_xong()


async def vong_lap(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            await mot_vong()
        except Exception:  # noqa: BLE001 — khong duoc lam chet service
            log.exception("Viec cho that bai, thu lai vong sau")
        if await settings_store.cho_theo_setting(settings_store.PENDING_WORK_INTERVAL, dung):
            break
