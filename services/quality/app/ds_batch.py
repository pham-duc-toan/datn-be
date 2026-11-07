"""Dawid-Skene + chi so do tin cay chay THEO LO (docs 3.7), khong trong request.

Moi lo, moi du an co nhan moi:
  - MOI cong cu do duoc (tru freeText): Krippendorff alpha voi ham khoang cach theo loai du lieu
    (chi_so.py), Fleiss' kappa cho cong cu chon mot gia tri, STAPLE (do nhay / dac hieu) cho mat na;
  - cong cu chon MOT gia tri roi rac (classification mot lop, pairwise): them crowd-kit Dawid-Skene —
    ma tran nham lan tung labeler → do tin cay = trung binh P(chon dung | dap an that).
Labeler co it hon setting quality.ds_min_labels nhan trong du an thi chua tin uoc
luong DS cua ho (VD-Q-07). Xong thi tinh lai uy tin cho labeler bi anh huong (STAPLE KHONG vao uy tin).

Nhan doc qua gRPC tu project-svc (kien truc 3.22) — quality khong giu ban sao. Du an "can tinh" = co bai nop
moi tu lan tinh truoc (ds_state.dirty_at > computed_at, attempt.submitted dat dirty_at).
"""

import asyncio
import logging
import uuid
import warnings
from datetime import datetime, timezone
from typing import Any

import pandas as pd
from sqlalchemy import text

from app import chi_so, project_client, settings_store
from app.db import session_factory
from app.processors import xep_tinh_uy_tin

log = logging.getLogger(__name__)


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


# Du an can tinh: co bai nop moi tu lan tinh truoc. tat_ca = moi du an tung co bai nop (admin chay lai toan bo,
# vd sau khi doi quality.ds_min_labels). Du an da luu tru thi khong tinh nua.
_SQL_DU_AN_CAN_TINH = text(
    "SELECT st.project_id FROM ds_state st "
    "WHERE (:tat_ca OR st.computed_at IS NULL OR st.dirty_at > st.computed_at) "
    "AND NOT EXISTS (SELECT 1 FROM project_archives a WHERE a.project_id = st.project_id AND a.archived_at IS NOT NULL) "
    "ORDER BY st.dirty_at"
)


async def chay_mot_lo(tat_ca: bool = False) -> int:
    """Chay DS cho cac du an CO BAI NOP MOI (tat_ca=True: moi du an). Tra ve so (du an, cong cu) da tinh.
    project-svc khong tra loi duoc thi bo qua du an do (lo sau tinh lai — dirty_at chua bi xoa)."""
    so = 0
    async with session_factory()() as s:
        du_an_ids = (await s.execute(_SQL_DU_AN_CAN_TINH, {"tat_ca": tat_ca})).scalars().all()

    for project_id in du_an_ids:
        # Moc TRUOC khi doc nhan: bai toi trong luc tinh dat dirty_at moi hon → lo sau tinh lai.
        bat_dau = datetime.now(timezone.utc)
        try:
            schema = (await project_client.du_an(project_id)).label_schema
            nhan = await project_client.nhan(project_id)
        except (project_client.KhongGoiDuoc, project_client.KhongTimThay) as ex:
            log.warning("DS: bo qua du an %s lo nay (%s)", project_id, ex)
            continue
        ds_ten = {t["name"] for t in cong_cu_dung_duoc(schema)}

        du_lieu = [(n, n.data, n.sample_metadata) for n in _moi_nguoi_mot_dong(nhan)]
        nguong = settings_store.nguong_gop()

        anh_huong: set[uuid.UUID] = set()
        for t in schema.get("tools", []) if du_lieu else []:
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
                    ky_nang = await asyncio.to_thread(uoc_luong, df, settings_store.so_nguyen(settings_store.DS_MIN_LABELS))

            async with session_factory()() as s, s.begin():
                await s.execute(
                    text(
                        "INSERT INTO project_metrics (project_id, tool, alpha, kappa, metric, item_count, label_count, computed_at) "
                        "VALUES (:p, :t, :a, :k, :m, :i, :n, now()) ON CONFLICT (project_id, tool) DO UPDATE SET "
                        "alpha = EXCLUDED.alpha, kappa = EXCLUDED.kappa, metric = EXCLUDED.metric, "
                        "item_count = EXCLUDED.item_count, label_count = EXCLUDED.label_count, computed_at = EXCLUDED.computed_at"
                    ),
                    {"p": project_id, "t": t["name"], "a": chi["alpha"], "k": chi["kappa"], "m": chi["metric"],
                     "i": chi["item_count"], "n": chi["label_count"]},
                )
                for w, (nhay, dac_hieu, so_mau) in chi["staple"].items():
                    await s.execute(
                        text(
                            "INSERT INTO staple_skills (labeler_id, project_id, tool, sensitivity, specificity, sample_count, computed_at) "
                            "VALUES (:l, :p, :t, :se, :sp, :n, now()) ON CONFLICT (labeler_id, project_id, tool) DO UPDATE SET "
                            "sensitivity = EXCLUDED.sensitivity, specificity = EXCLUDED.specificity, "
                            "sample_count = EXCLUDED.sample_count, computed_at = EXCLUDED.computed_at"
                        ),
                        {"l": uuid.UUID(w), "p": project_id, "t": t["name"], "se": nhay, "sp": dac_hieu, "n": so_mau},
                    )
                for w, (skill, n) in ky_nang.items():
                    await s.execute(
                        text(
                            "INSERT INTO ds_skills (labeler_id, project_id, tool, skill, label_count, computed_at) "
                            "VALUES (:l, :p, :t, :k, :n, now()) ON CONFLICT (labeler_id, project_id, tool) DO UPDATE SET "
                            "skill = EXCLUDED.skill, label_count = EXCLUDED.label_count, computed_at = EXCLUDED.computed_at"
                        ),
                        {"l": uuid.UUID(w), "p": project_id, "t": t["name"], "k": skill, "n": n},
                    )
                    anh_huong.add(uuid.UUID(w))
            so += 1

        # Do tin cay DS doi → tinh lai uy tin (worker viec_cho doc bang chung qua gRPC).
        if anh_huong:
            async with session_factory()() as s, s.begin():
                await xep_tinh_uy_tin(s, anh_huong, None)

        async with session_factory()() as s, s.begin():
            await s.execute(
                text("UPDATE ds_state SET label_count = :n, computed_at = :at WHERE project_id = :p"),
                {"p": project_id, "n": len(nhan), "at": bat_dau},
            )

    return so


async def vong_lap(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            so = await chay_mot_lo()
            log.info("Lo Dawid-Skene xong: %s (du an, cong cu)", so)
        except Exception:  # noqa: BLE001
            log.exception("Lo Dawid-Skene that bai, thu lai lo sau")
        await settings_store.cho_theo_setting(settings_store.DS_INTERVAL, dung)
