"""Dawid-Skene + chi so do tin cay cua MOT du an, chay MOT lan SAU KHI CHAM (project_gradings.ds_at).

Vi sao sau khi cham: luc dang thu bai, nhan con do dang (VD chi 2 nguoi nop, bat dong o mot cau → DS khong
biet ai dung) → do tin cay dao dong theo thu tu nop roi lan sang uy tin. Kien truc 3.18 "cham mot lan ca
du an" — DS cung vay: dong thu bai → cham → DS → uy tin.

Moi du an (bo cau bi loai — samples.excluded_at, giong luc cham):
  - MOI cong cu do duoc (tru freeText): Krippendorff alpha voi ham khoang cach theo loai du lieu
    (chi_so.py), Fleiss' kappa cho cong cu chon mot gia tri, STAPLE (do nhay / dac hieu) cho mat na;
  - cong cu chon MOT gia tri roi rac (classification mot lop, pairwise): them crowd-kit Dawid-Skene —
    ma tran nham lan tung labeler → do tin cay = trung binh P(chon dung | dap an that).
Labeler co it hon setting quality.ds_min_labels nhan trong du an thi chua tin uoc
luong DS cua ho (VD-Q-07). STAPLE KHONG vao uy tin.

Thu tu trong worker viec_cho: cham lai → CHAY DS (o day) → ap dung cham (can ds_at) → tinh uy tin. Nen moi
labeler chi tinh lai uy tin MOT lan cho moi du an, voi du ca bang chung cham lan ds_skills moi.
Tinh lai (admin, sau khi doi quality.ds_min_labels): du an da xep uy tin (reputation_at co) thi buoc nay tu
xep labeler bi anh huong vao reputation_dirty.

Nhan doc qua gRPC tu project-svc (kien truc 3.22) — quality khong giu ban sao.
"""

import asyncio
import logging
import uuid
import warnings
from typing import Any

import pandas as pd
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import chi_so, project_client, settings_store
from app.db import session_factory
from app.processors import xep_tinh_uy_tin

log = logging.getLogger(__name__)

# So du an toi da moi vong worker (moi du an mot transaction).
LO = 20


def gia_tri_roi_rac(kind: str, ket_qua: Any) -> str | None:
    """Mot nhan → mot gia tri cho DS. None = khong dung duoc (bo trong / nhieu lop)."""
    if not isinstance(ket_qua, dict):
        return None
    if kind == "classification":
        lop = ket_qua.get("labelIds") or []
        return lop[0] if len(lop) == 1 else None
    if kind == "pairwise":
        return ket_qua.get("choice")
    return None


def cong_cu_dung_duoc(label_schema: dict[str, Any]) -> list[dict[str, Any]]:
    ra = []
    for t in label_schema.get("tools", []):
        if t.get("kind") == "pairwise":
            ra.append(t)
        elif t.get("kind") == "classification" and not t.get("allowMultiple", False):
            ra.append(t)
    return ra


def uoc_luong(df: pd.DataFrame, min_nhan: int) -> dict[str, tuple[float, int]]:
    """df(task, worker, label) → {worker: (do_tin_cay, so_nhan)}. Chay trong thread."""
    from crowdkit.aggregation import DawidSkene

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        so_nhan = df.groupby("worker").size()
        if df["worker"].nunique() < 2 or df["label"].nunique() < 2:
            return {}

        ds = DawidSkene(n_iter=100).fit(df)
        loi = ds.errors_  # index (worker, nhan quan sat), cot = nhan that; gia tri P(quan sat | that)

    ky_nang: dict[str, tuple[float, int]] = {}
    for w, n in so_nhan.items():
        if n < min_nhan:
            continue
        diem = []
        for that in loi.columns:
            if (w, that) in loi.index:
                diem.append(float(loi.loc[(w, that), that]))
        if diem:
            ky_nang[str(w)] = (sum(diem) / len(diem), int(n))
    return ky_nang


def _moi_nguoi_mot_dong(nhan: list) -> list:
    """Moi (mau, labeler) giu nhan NHAN SAU CUNG (labeler lam lai sau khi bi tu choi)."""
    cuoi: dict[tuple, Any] = {}
    for n in nhan:
        cuoi[(n.sample_id, n.labeler_id)] = n
    return list(cuoi.values())


class KetQuaCongCu:
    """Chi so cua mot cong cu: dong project_metrics + staple_skills + ds_skills."""

    def __init__(self, ten: str, chi: dict[str, Any], ky_nang: dict[str, tuple[float, int]]):
        self.ten = ten
        self.chi = chi
        self.ky_nang = ky_nang


async def tinh(schema: dict[str, Any], nhan: list, loai: frozenset[str]) -> list[KetQuaCongCu]:
    """Tinh chi so moi cong cu tu nhan cua du an (bo cau bi loai). CPU chay trong thread."""
    du_lieu = [(n, n.data, n.sample_metadata) for n in _moi_nguoi_mot_dong(nhan) if str(n.sample_id) not in loai]
    if not du_lieu:
        return []
    ds_ten = {t["name"] for t in cong_cu_dung_duoc(schema)}
    nguong = settings_store.nguong_gop()
    min_nhan = settings_store.so_nguyen(settings_store.DS_MIN_LABELS)

    ra: list[KetQuaCongCu] = []
    for t in schema.get("tools", []):
        vao = [(str(n.sample_id), str(n.labeler_id), data.get(t["name"]), md or None) for n, data, md in du_lieu]
        chi = await asyncio.to_thread(chi_so.tinh_cho_cong_cu, t, vao, nguong)
        if chi is None:
            continue

        ky_nang: dict[str, tuple[float, int]] = {}
        if t["name"] in ds_ten:
            dong = []
            for sample_id, labeler_id, ket_qua, _md in vao:
                v = gia_tri_roi_rac(t["kind"], ket_qua)
                if v is not None:
                    dong.append((sample_id, labeler_id, v))
            if dong:
                df = pd.DataFrame(dong, columns=["task", "worker", "label"])
                ky_nang = await asyncio.to_thread(uoc_luong, df, min_nhan)
        ra.append(KetQuaCongCu(t["name"], chi, ky_nang))
    return ra


async def _ghi(s: AsyncSession, project_id: uuid.UUID, ket_qua: list[KetQuaCongCu]) -> set[uuid.UUID]:
    """Thay toan bo chi so cua du an (xoa roi ghi — tinh lai voi ds_min_labels moi thi labeler khong con du
    nhan cung mat dong cu). Tra labeler co ds_skills cu hoac moi (uy tin can tinh lai neu chay lai)."""
    anh_huong = set((await s.execute(
        text("DELETE FROM ds_skills WHERE project_id = :p RETURNING labeler_id"), {"p": project_id})).scalars().all())
    await s.execute(text("DELETE FROM staple_skills WHERE project_id = :p"), {"p": project_id})
    await s.execute(text("DELETE FROM project_metrics WHERE project_id = :p"), {"p": project_id})

    for k in ket_qua:
        c = k.chi
        await s.execute(
            text("INSERT INTO project_metrics (project_id, tool, alpha, kappa, metric, item_count, label_count, computed_at) "
                 "VALUES (:p, :t, :a, :k, :m, :i, :n, now())"),
            {"p": project_id, "t": k.ten, "a": c["alpha"], "k": c["kappa"], "m": c["metric"],
             "i": c["item_count"], "n": c["label_count"]},
        )
        for w, (nhay, dac_hieu, so_mau) in c["staple"].items():
            await s.execute(
                text("INSERT INTO staple_skills (labeler_id, project_id, tool, sensitivity, specificity, sample_count, computed_at) "
                     "VALUES (:l, :p, :t, :se, :sp, :n, now())"),
                {"l": uuid.UUID(w), "p": project_id, "t": k.ten, "se": nhay, "sp": dac_hieu, "n": so_mau},
            )
        for w, (skill, n) in k.ky_nang.items():
            await s.execute(
                text("INSERT INTO ds_skills (labeler_id, project_id, tool, skill, label_count, computed_at) "
                     "VALUES (:l, :p, :t, :k, :n, now())"),
                {"l": uuid.UUID(w), "p": project_id, "t": k.ten, "k": skill, "n": n},
            )
            anh_huong.add(uuid.UUID(w))
    return anh_huong


async def _chay_du_an(s: AsyncSession, project_id: uuid.UUID, reputation_at) -> bool:
    """DS cho mot du an da cham (noi goi giu khoa dong project_gradings). True = da ghi chi so.

    project-svc khong tra loi → KhongGoiDuoc nem ra (ds_at van NULL, vong sau chay lai). Du lieu lam thuat
    toan loi → ghi log, VAN dat ds_at: uy tin khong bi chan mai, chi thieu do tin cay DS cua du an nay."""
    ghi = False
    da_luu_tru = (await s.execute(text(
        "SELECT 1 FROM project_archives WHERE project_id = :p AND archived_at IS NOT NULL"), {"p": project_id})).first()
    if da_luu_tru is not None:
        log.info("DS: du an %s da luu tru — khong tinh", project_id)
    else:
        try:
            du_an = await project_client.du_an(project_id)
            nhan = await project_client.nhan(project_id)
        except project_client.KhongTimThay:
            du_an = None
            log.warning("DS: project-svc khong con du an %s — bo qua", project_id)
        if du_an is not None:
            try:
                ket_qua = await tinh(du_an.label_schema, nhan, du_an.excluded_sample_ids)
            except Exception:  # noqa: BLE001 — du lieu la khong duoc chan uy tin cua ca du an
                log.exception("DS: tinh chi so du an %s that bai — bo qua, uy tin tinh khong co DS", project_id)
                ket_qua = None
            if ket_qua is not None:
                anh_huong = await _ghi(s, project_id, ket_qua)
                ghi = True
                # Chay lai (uy tin da tinh tu truoc) → tinh lai cho labeler bi anh huong. Lan dau thi ap_dung_cham lo.
                if reputation_at is not None and anh_huong:
                    await xep_tinh_uy_tin(s, anh_huong, None)

    await s.execute(text("UPDATE project_gradings SET ds_at = now() WHERE project_id = :p"), {"p": project_id})
    return ghi


async def chay_mot_lo() -> int:
    """Chay DS cho du an DA CHAM ma chua chay (ds_at NULL), toi da LO du an. Tra so du an da xu ly (dat ds_at).
    Nhieu instance: FOR UPDATE SKIP LOCKED — moi du an mot ben tinh."""
    so = 0
    for _ in range(LO):
        async with session_factory()() as s, s.begin():
            d = (await s.execute(text(
                "SELECT project_id, reputation_at FROM project_gradings WHERE graded_at IS NOT NULL AND ds_at IS NULL "
                "ORDER BY graded_at LIMIT 1 FOR UPDATE SKIP LOCKED"))).first()
            if d is None:
                break
            await _chay_du_an(s, d.project_id, d.reputation_at)
        so += 1
    return so


async def yeu_cau_tinh_lai() -> int:
    """Admin: tinh lai DS moi du an da cham, chua luu tru (vd sau khi doi quality.ds_min_labels). Tra so du an
    duoc xep — worker viec_cho (hoac noi goi) chay tiep."""
    async with session_factory()() as s, s.begin():
        return (await s.execute(text(
            "UPDATE project_gradings g SET ds_at = NULL WHERE g.graded_at IS NOT NULL AND g.ds_at IS NOT NULL "
            "AND NOT EXISTS (SELECT 1 FROM project_archives a WHERE a.project_id = g.project_id AND a.archived_at IS NOT NULL)"
        ))).rowcount
