"""Dawid-Skene + Krippendorff alpha chay THEO LO (docs 3.7), khong trong request.

Moi lo, moi du an, moi cong cu chon MOT gia tri roi rac (classification mot lop,
pairwise): dung crowd-kit uoc luong
  - ma tran nham lan tung labeler → do tin cay = trung binh P(chon dung | dap an that);
  - Krippendorff alpha ca du an — muc dong thuan da tru phan trung hop ngau nhien.
Labeler co it hon setting quality.ds_min_labels nhan trong du an thi chua tin uoc
luong cua ho (VD-Q-07). Xong thi tinh lai uy tin cho labeler bi anh huong.
"""

import asyncio
import json
import logging
import uuid
import warnings
from typing import Any

import pandas as pd
from sqlalchemy import text

from app import settings_store
from app.db import session_factory
from app.processors import cap_nhat_uy_tin

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


def uoc_luong(df: pd.DataFrame, min_nhan: int) -> tuple[dict[str, tuple[float, int]], float | None]:
    """df(task, worker, label) → ({worker: (do_tin_cay, so_nhan)}, alpha). Chay trong thread."""
    from crowdkit.aggregation import DawidSkene
    from crowdkit.metrics.data import alpha_krippendorff

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        alpha: float | None = None
        if df["task"].nunique() >= 2 and df["worker"].nunique() >= 2:
            try:
                alpha = float(alpha_krippendorff(df))
            except Exception:  # noqa: BLE001 — du lieu suy bien (mot gia tri duy nhat...)
                alpha = None

        so_nhan = df.groupby("worker").size()
        if df["worker"].nunique() < 2 or df["label"].nunique() < 2:
            return {}, alpha

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
    return ky_nang, alpha


async def chay_mot_lo() -> int:
    """Chay DS cho moi du an. Tra ve so (du an, cong cu) da tinh."""
    so = 0
    async with session_factory()() as s:
        du_an = (await s.execute(text("SELECT project_id, label_schema FROM projects"))).all()

    for d in du_an:
        schema = d.label_schema if isinstance(d.label_schema, dict) else json.loads(d.label_schema)
        cong_cu = cong_cu_dung_duoc(schema)
        if not cong_cu:
            continue

        async with session_factory()() as s:
            nhan = (await s.execute(
                text("SELECT sample_id, labeler_id, data FROM labels WHERE project_id = :p AND labeler_id IS NOT NULL"),
                {"p": d.project_id})).all()
        if not nhan:
            continue

        anh_huong: set[uuid.UUID] = set()
        for t in cong_cu:
            dong = []
            for n in nhan:
                data = n.data if isinstance(n.data, dict) else json.loads(n.data)
                v = gia_tri_roi_rac(t["kind"], data.get(t["name"]))
                if v is not None:
                    dong.append((str(n.sample_id), str(n.labeler_id), v))
            if not dong:
                continue

            df = pd.DataFrame(dong, columns=["task", "worker", "label"])
            ky_nang, alpha = await asyncio.to_thread(uoc_luong, df, settings_store.so_nguyen(settings_store.DS_MIN_LABELS))

            async with session_factory()() as s, s.begin():
                await s.execute(
                    text(
                        "INSERT INTO project_metrics (project_id, tool, alpha, item_count, label_count, computed_at) "
                        "VALUES (:p, :t, :a, :i, :n, now()) ON CONFLICT (project_id, tool) DO UPDATE SET "
                        "alpha = EXCLUDED.alpha, item_count = EXCLUDED.item_count, label_count = EXCLUDED.label_count, "
                        "computed_at = EXCLUDED.computed_at"
                    ),
                    {"p": d.project_id, "t": t["name"], "a": alpha, "i": int(df["task"].nunique()), "n": len(df)},
                )
                for w, (skill, n) in ky_nang.items():
                    await s.execute(
                        text(
                            "INSERT INTO ds_skills (labeler_id, project_id, tool, skill, label_count, computed_at) "
                            "VALUES (:l, :p, :t, :k, :n, now()) ON CONFLICT (labeler_id, project_id, tool) DO UPDATE SET "
                            "skill = EXCLUDED.skill, label_count = EXCLUDED.label_count, computed_at = EXCLUDED.computed_at"
                        ),
                        {"l": uuid.UUID(w), "p": d.project_id, "t": t["name"], "k": skill, "n": n},
                    )
                    anh_huong.add(uuid.UUID(w))
            so += 1

        for labeler in anh_huong:
            async with session_factory()() as s, s.begin():
                await cap_nhat_uy_tin(s, labeler, None)

    return so


async def vong_lap(dung: asyncio.Event) -> None:
    while not dung.is_set():
        try:
            so = await chay_mot_lo()
            log.info("Lo Dawid-Skene xong: %s (du an, cong cu)", so)
        except Exception:  # noqa: BLE001
            log.exception("Lo Dawid-Skene that bai, thu lai lo sau")
        try:
            await asyncio.wait_for(dung.wait(), timeout=settings_store.giay(settings_store.DS_INTERVAL))
        except asyncio.TimeoutError:
            pass
