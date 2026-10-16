"""Chi so dong thuan / do tin cay theo LOAI cong cu (FQ-02) — ham thuan, test duoc.

  - Fleiss' kappa       : cong cu chon MOT gia tri (classification mot lop, pairwise). Ban mo rong cho so
                          nguoi gan KHAC NHAU giua cac mau (redundancy thich ung): P_i tinh tren n_i nguoi cua mau i.
  - Krippendorff alpha  : alpha = 1 - D_o / D_e voi HAM KHOANG CACH tuy loai du lieu:
                            nominal  (phan loai, cap)            δ = 0 / 1
                            interval (number)                    δ = (a - b)^2
                            cer      (transcription)             δ = do lech ky tu chuan hoa
                            kendall  (ranking)                   δ = (1 - tau) / 2
                            set_f1   (bbox, polygon, span, ...)  δ = 1 - F1 giua hai bo doi tuong (ghep theo nguong
                                                                  cua cong cu — cung phep ghep voi gop.py)
                          D_o = trung binh khoang cach giua cac gia tri TRONG cung mau; D_e = giua MOI cap gia tri.
                          Nhieu gia tri thi D_e lay mau toi da `toi_da_gia_tri` (hat giong co dinh → tat dinh).
  - STAPLE              : mat na (mask) — uoc luong do nhay / do dac hieu tung nguoi bang EM (Warfield 2004) tren
                          CA anh. Pixel duoc gom theo "kieu phieu" (bo gia tri 0/1 cua n nguoi, toi da 2^n kieu)
                          kem so pixel moi kieu — chinh xac nhu chay tung pixel ma nhanh hon nhieu.
"""

from __future__ import annotations

import math
import random
from collections import Counter, defaultdict
from typing import Any, Callable

import numpy as np

from app import gop


# =====================================================================
# Fleiss' kappa
# =====================================================================


def fleiss_kappa(don_vi: list[list[str]]) -> float | None:
    """don_vi: moi mau mot danh sach gia tri (moi nguoi mot gia tri). Mau duoi 2 nguoi bi bo."""
    dung = [u for u in don_vi if len(u) >= 2]
    if not dung:
        return None
    tong_gia_tri: Counter = Counter()
    p_mau = []
    tong = 0
    for u in dung:
        n = len(u)
        dem = Counter(u)
        tong_gia_tri.update(dem)
        tong += n
        p_mau.append((sum(c * c for c in dem.values()) - n) / (n * (n - 1)))
    p_tb = sum(p_mau) / len(p_mau)
    pe = sum((c / tong) ** 2 for c in tong_gia_tri.values())
    if pe >= 1 - 1e-12:
        return None   # moi nguoi chon cung mot gia tri o moi mau: kappa khong xac dinh
    return (p_tb - pe) / (1 - pe)


# =====================================================================
# Krippendorff alpha tong quat
# =====================================================================


def krippendorff_alpha(don_vi: list[list[Any]], khoang_cach: Callable[[Any, Any], float],
                       toi_da_gia_tri: int = 300, hat_giong: int = 7) -> float | None:
    """alpha = 1 - D_o / D_e. None khi khong du du lieu (it hon 2 mau co >= 2 gia tri, hoac D_e = 0)."""
    co_cap = [u for u in don_vi if len(u) >= 2]
    n = sum(len(u) for u in co_cap)
    if len(co_cap) < 2 or n < 2:
        return None

    # D_o: trong tung mau, moi cap co thu tu (i != j), trong so 1 / (m_u - 1).
    tong_o = 0.0
    for u in co_cap:
        m = len(u)
        s = 0.0
        for i in range(m):
            for j in range(m):
                if i != j:
                    s += khoang_cach(u[i], u[j])
        tong_o += s / (m - 1)
    d_o = tong_o / n

    # D_e: moi cap gia tri (lay mau neu nhieu).
    gia_tri = [v for u in co_cap for v in u]
    if len(gia_tri) > toi_da_gia_tri:
        gia_tri = random.Random(hat_giong).sample(gia_tri, toi_da_gia_tri)
    k = len(gia_tri)
    tong_e = 0.0
    for i in range(k):
        for j in range(i + 1, k):
            tong_e += 2 * khoang_cach(gia_tri[i], gia_tri[j])
    d_e = tong_e / (k * (k - 1))
    if d_e <= 1e-12:
        return None
    return 1 - (d_o / d_e)


def khoang_cach_cua(tool: dict[str, Any], nguong_mac_dinh: float | None) -> tuple[str, Callable[[Any, Any], float]] | None:
    """(ten phep do, ham khoang cach) cho mot cong cu; None neu khong do duoc (freeText)."""
    kind = tool["kind"]
    if kind in ("classification", "pairwise"):
        return "nominal", lambda a, b: 0.0 if a == b else 1.0
    if kind == "number":
        return "interval", lambda a, b: (float(a) - float(b)) ** 2
    if kind == "transcription":
        return "cer", lambda a, b: gop.do_lech_chu(a, b)
    if kind == "ranking":
        return "kendall", lambda a, b: (1 - gop.kendall(a, b)) / 2
    if kind in gop.CONG_CU_DANH_SACH:
        loai = gop._nho_do_giong(gop._LOAI[kind])
        if loai.trung_het:
            nguong = 1.0
        else:
            nguong = tool.get("matchThreshold")
            if nguong is None:
                nguong = nguong_mac_dinh if nguong_mac_dinh is not None else gop.NGUONG_THU_VIEN[kind]

        def d(a: dict, b: dict) -> float:
            # a, b: {"objs": [...], "mau": metadata | None}
            ty_le = loai.ty_le(a["mau"], a["objs"] + b["objs"])
            return 1.0 - gop._diem_f1(a["objs"], b["objs"], tool, loai, nguong, ty_le)
        return "set_f1", d
    return None


def gia_tri_cua(tool: dict[str, Any], ket_qua: Any, mau: dict | None) -> Any:
    """Ket qua mot nguoi cho mot cong cu → gia tri dua vao alpha; None = bo qua (khong lam / sai dang)."""
    kind = tool["kind"]
    if ket_qua is None:
        return None
    if kind == "classification":
        lop = ket_qua.get("labelIds") if isinstance(ket_qua, dict) else None
        if not lop:
            return None
        return lop[0] if len(lop) == 1 else "|".join(sorted(lop))
    if kind == "pairwise":
        return ket_qua.get("choice") if isinstance(ket_qua, dict) else None
    if kind == "number":
        return ket_qua.get("value") if isinstance(ket_qua, dict) else None
    if kind == "transcription":
        return ket_qua.get("text") if isinstance(ket_qua, dict) else None
    if kind == "ranking":
        return ket_qua.get("order") if isinstance(ket_qua, dict) else None
    if kind in gop.CONG_CU_DANH_SACH:
        return {"objs": ket_qua if isinstance(ket_qua, list) else [], "mau": mau}
    return None


# =====================================================================
# STAPLE (mat na)
# =====================================================================


def giai_rle(rle: dict[str, Any]) -> np.ndarray:
    """RLE khong nen kieu COCO (doan 0 / 1 xen ke, bat dau bang 0) → mang bool phang (thu tu theo cot)."""
    counts = np.asarray(rle["counts"], dtype=np.int64)
    gia_tri = np.zeros(len(counts), dtype=bool)
    gia_tri[1::2] = True
    return np.repeat(gia_tri, counts)


def staple(mau: list[dict[str, np.ndarray]], so_vong: int = 50) -> dict[str, tuple[float, float, int]]:
    """
    mau: moi mau mot {nguoi: mat na bool phang} (cung kich thuoc). Tra {nguoi: (do nhay p, do dac hieu q, so mau)}.
    Mau khong ai to gi bi bo (khong noi gi ve do nhay).
    """
    # (nguoi, kieu phieu [nguoi, kieu], so pixel moi kieu)
    du_lieu: list[tuple[list[str], np.ndarray, np.ndarray]] = []
    so_mau: Counter = Counter()
    for m in mau:
        if len(m) < 2:
            continue
        nguoi = sorted(m)
        ma_tran = np.stack([m[n] for n in nguoi])           # [nguoi, pixel]
        if not ma_tran.any():
            continue
        kieu, dem = np.unique(ma_tran.T, axis=0, return_counts=True)
        du_lieu.append((nguoi, kieu.T.astype(np.float64), dem.astype(np.float64)))
        so_mau.update(nguoi)
    if not du_lieu:
        return {}

    p: dict[str, float] = defaultdict(lambda: 0.9)
    q: dict[str, float] = defaultdict(lambda: 0.9)
    # Tien nghiem: ti le pixel la vat = trung binh ti le to cua moi nguoi.
    tong_1 = sum(float((d * c).sum()) for _, d, c in du_lieu)
    tong = sum(float(c.sum()) * d.shape[0] for _, d, c in du_lieu)
    f = min(max(tong_1 / tong, 1e-3), 1 - 1e-3)

    for _ in range(so_vong):
        tu_p: dict[str, float] = defaultdict(float)
        mau_p: dict[str, float] = defaultdict(float)
        tu_q: dict[str, float] = defaultdict(float)
        mau_q: dict[str, float] = defaultdict(float)
        for nguoi, d, c in du_lieu:
            vp = np.array([p[n] for n in nguoi])[:, None]
            vq = np.array([q[n] for n in nguoi])[:, None]
            log_a = math.log(f) + (d * np.log(vp) + (1 - d) * np.log(1 - vp)).sum(axis=0)
            log_b = math.log(1 - f) + ((1 - d) * np.log(vq) + d * np.log(1 - vq)).sum(axis=0)
            w = 1.0 / (1.0 + np.exp(np.clip(log_b - log_a, -700, 700)))   # P(pixel that la vat | quan sat)
            wc = w * c
            nc = (1 - w) * c
            for k, n in enumerate(nguoi):
                tu_p[n] += float((wc * d[k]).sum())
                mau_p[n] += float(wc.sum())
                tu_q[n] += float((nc * (1 - d[k])).sum())
                mau_q[n] += float(nc.sum())
        for n in mau_p:
            p[n] = min(max(tu_p[n] / mau_p[n], 1e-4), 1 - 1e-4) if mau_p[n] > 0 else p[n]
            q[n] = min(max(tu_q[n] / mau_q[n], 1e-4), 1 - 1e-4) if mau_q[n] > 0 else q[n]

    return {n: (round(p[n], 4), round(q[n], 4), so_mau[n]) for n in so_mau}


# =====================================================================
# Tinh cho mot cong cu cua mot du an (lo ds_batch goi, trong thread)
# =====================================================================

# Mat na so khoang cach bang RLE thuan Python — lay mau it gia tri hon cho D_e.
_TOI_DA_GIA_TRI = {"mask": 120, "track": 150}


def tinh_cho_cong_cu(tool: dict[str, Any], nhan: list[tuple[str, str, Any, dict | None]],
                     nguong_gop: dict[str, float]) -> dict[str, Any] | None:
    """
    nhan: (sample_id, labeler_id, ket_qua cong cu nay, sample_metadata) — moi (mau, nguoi) MOT dong.
    Tra {"metric", "alpha", "kappa", "item_count", "label_count", "staple"} hoac None (cong cu khong do duoc).
    """
    phep = khoang_cach_cua(tool, nguong_gop.get(tool["kind"]))
    if phep is None:
        return None
    ten, d = phep

    theo_mau: dict[str, list[Any]] = defaultdict(list)
    so_nhan = 0
    for sample_id, _nguoi, ket_qua, mau in nhan:
        v = gia_tri_cua(tool, ket_qua, mau)
        if v is None:
            continue
        theo_mau[sample_id].append(v)
        so_nhan += 1
    if so_nhan == 0:
        return None

    don_vi = list(theo_mau.values())
    try:
        alpha = krippendorff_alpha(don_vi, d, _TOI_DA_GIA_TRI.get(tool["kind"], 300))
    except (TypeError, ValueError, KeyError):
        alpha = None   # du lieu cu sai dang (truoc khi co kiem tra payload) — bo qua chi so, khong lam hong lo
    kappa = fleiss_kappa(don_vi) if ten == "nominal" else None

    ra: dict[str, Any] = {
        "metric": ten,
        "alpha": None if alpha is None else round(alpha, 4),
        "kappa": None if kappa is None else round(kappa, 4),
        "item_count": len(theo_mau),
        "label_count": so_nhan,
        "staple": {},
    }
    if tool["kind"] == "mask":
        ra["staple"] = staple(_mat_na_theo_lop(nhan))
    return ra


def _mat_na_theo_lop(nhan: list[tuple[str, str, Any, dict | None]]) -> list[dict[str, np.ndarray]]:
    """Moi (mau, lop) mot don vi STAPLE: mat na cua moi nguoi = hop cac mat na lop do (khong to = rong)."""
    theo_mau: dict[str, dict[str, list[dict]]] = defaultdict(dict)
    for sample_id, nguoi, ket_qua, _mau in nhan:
        if isinstance(ket_qua, list):
            theo_mau[sample_id][nguoi] = [o for o in ket_qua if isinstance(o, dict) and "rle" in o]

    ra: list[dict[str, np.ndarray]] = []
    for cua_nguoi in theo_mau.values():
        if len(cua_nguoi) < 2:
            continue
        kich_thuoc = next((tuple(o["rle"]["size"]) for ds in cua_nguoi.values() for o in ds), None)
        if kich_thuoc is None:
            continue
        tong = kich_thuoc[0] * kich_thuoc[1]
        lop = sorted({o["labelId"] for ds in cua_nguoi.values() for o in ds})
        for l in lop:
            don_vi: dict[str, np.ndarray] = {}
            for nguoi, ds in cua_nguoi.items():
                m = np.zeros(tong, dtype=bool)
                for o in ds:
                    if o["labelId"] == l and tuple(o["rle"]["size"]) == kich_thuoc:
                        g = giai_rle(o["rle"])
                        if g.size == tong:
                            m |= g
                don_vi[nguoi] = m
            ra.append(don_vi)
    return ra
