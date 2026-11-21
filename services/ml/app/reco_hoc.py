"""Hoc mo hinh goi y cho reco-svc (kien truc 3.39 muc 8) — phan THUAN (khong IO), test duoc.

Hai cau hoi cho MOT cap (labeler, du an), dau vao la vector dac trung reco-svc ghi luc tra feed (feed_served.features,
CUNG thu tu voi DacTrung.Ten ben C#):
    start — P(bat dau lam | thay the);
    pass  — P(bai dat | da bat dau).
"Kha nang kiem tien" khong phai dau ra mo hinh: reco-svc ghep hai xac suat voi thuong, P(lot suat), so phut.

Hai mo hinh, CHON THEO LUONG DU LIEU (bo mang no-ron):
    it du lieu  → hoi quy logistic (vai chuc trong so, de giai thich);
    du du lieu  → hoc ca LightGBM, lay ban tot hon tren 20 % du lieu kiem (chia theo THOI GIAN, khong xao).
Ban moi chi thanh "candidate" (chay thu A/B) khi tot hon ban dang chay tren du lieu kiem; thang A/B co y nghia thong ke
moi thanh "production" (danh_gia_ab).
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np

# PHAI khop Crowd.Reco.Domain.DacTrung.Ten (them dac trung = them o CUOI ca hai ben).
DAC_TRUNG = [
    "log_reward", "slots", "competitors", "p_slot", "est_minutes", "question_count", "match", "match_domain",
    "match_language", "match_modality", "match_tool", "p_pass_prior", "reputation", "level", "is_new",
    "hours_to_deadline", "hour_of_day", "position", "requires_test", "project_start_rate", "tried_before",
]
VI_TRI_MATCH = DAC_TRUNG.index("match")
VI_TRI_P_PASS = DAC_TRUNG.index("p_pass_prior")

# Nguong chuyen sang LightGBM (muc 8.1 ke hoach) — chinh khi co so do.
NGUONG_LUOT_HIEN = 200_000
NGUONG_LUOT_BAT_DAU = 5_000
# It hon so mau nay (hoac thieu mot trong hai nhan) thi khong hoc — giu mo hinh dang chay.
MAU_TOI_THIEU = 200


@dataclass
class DuLieu:
    """X: ma tran dac trung (n × so dac trung), y: nhan 0/1, t: thoi diem (giay) de chia theo thoi gian."""
    X: np.ndarray
    y: np.ndarray
    t: np.ndarray

    @staticmethod
    def tu_dong(dong: list[tuple[list[float], int, float]]) -> "DuLieu":
        so = len(DAC_TRUNG)
        X = np.full((len(dong), so), np.nan, dtype=float)
        for i, (f, _, _) in enumerate(dong):
            n = min(so, len(f))
            X[i, :n] = f[:n]
        y = np.array([int(d[1]) for d in dong], dtype=int)
        t = np.array([float(d[2]) for d in dong], dtype=float)
        return DuLieu(X, y, t)

    def __len__(self) -> int:
        return len(self.y)


def chia_theo_thoi_gian(d: DuLieu, ti_le_hoc: float = 0.8) -> tuple[DuLieu, DuLieu]:
    """80 % cu nhat de hoc, 20 % moi nhat de kiem — giong cach mo hinh se dung (hoc qua khu, du doan tuong lai)."""
    thu_tu = np.argsort(d.t, kind="stable")
    cat = int(len(thu_tu) * ti_le_hoc)
    a, b = thu_tu[:cat], thu_tu[cat:]
    return DuLieu(d.X[a], d.y[a], d.t[a]), DuLieu(d.X[b], d.y[b], d.t[b])


def du_de_hoc(d: DuLieu) -> bool:
    return len(d) >= MAU_TOI_THIEU and 0 < int(d.y.sum()) < len(d)


# ---------------------------------------------------------------- do luong

def log_loss(y: np.ndarray, p: np.ndarray) -> float:
    p = np.clip(p, 1e-6, 1 - 1e-6)
    return float(-np.mean(y * np.log(p) + (1 - y) * np.log(1 - p)))


def auc(y: np.ndarray, p: np.ndarray) -> float:
    """AUC = xac suat mot mau duong co diem cao hon mot mau am (Mann-Whitney, xu ly diem bang nhau)."""
    duong = int(y.sum())
    am = len(y) - duong
    if duong == 0 or am == 0:
        return 0.5
    hang = _hang_trung_binh(p)
    return float((hang[y == 1].sum() - duong * (duong + 1) / 2) / (duong * am))


def _hang_trung_binh(x: np.ndarray) -> np.ndarray:
    thu_tu = np.argsort(x, kind="stable")
    hang = np.empty(len(x), dtype=float)
    i = 0
    xs = x[thu_tu]
    while i < len(x):
        j = i
        while j + 1 < len(x) and xs[j + 1] == xs[i]:
            j += 1
        hang[thu_tu[i:j + 1]] = (i + j) / 2 + 1
        i = j + 1
    return hang


@dataclass
class KetQua:
    loai: str            # formula | logistic | lightgbm
    auc: float
    log_loss: float
    tham_so: dict = field(default_factory=dict)  # khoi JSON cua mot bo phan loai


# ---------------------------------------------------------------- G1 (ban dang chay mac dinh)

def du_doan_g1(cau_hoi: str, X: np.ndarray) -> np.ndarray:
    """Cong thuc tay G1 cua reco-svc: start = muc hop the, pass = bo dem dat theo cong cu."""
    cot = VI_TRI_MATCH if cau_hoi == "start" else VI_TRI_P_PASS
    p = X[:, cot]
    return np.where(np.isnan(p), 0.5, p)


# ---------------------------------------------------------------- logistic

def hoc_logistic(hoc: DuLieu, kiem: DuLieu) -> KetQua:
    from sklearn.linear_model import LogisticRegression

    tb = np.nanmean(hoc.X, axis=0)
    tb = np.where(np.isnan(tb), 0.0, tb)
    X = np.where(np.isnan(hoc.X), tb, hoc.X)
    thang = X.std(axis=0)
    thang = np.where(thang < 1e-9, 1.0, thang)
    m = LogisticRegression(C=1.0, max_iter=1000)
    m.fit((X - tb) / thang, hoc.y)

    Xk = np.where(np.isnan(kiem.X), tb, kiem.X)
    p = m.predict_proba((Xk - tb) / thang)[:, 1]
    return KetQua("logistic", auc(kiem.y, p), log_loss(kiem.y, p), {
        "intercept": float(m.intercept_[0]),
        "coef": [float(x) for x in m.coef_[0]],
        "mean": [float(x) for x in tb],
        "scale": [float(x) for x in thang],
    })


def du_doan_logistic(tham_so: dict, X: np.ndarray) -> np.ndarray:
    """Cung cong thuc voi Logistic.XacSuat ben C# (dac trung thieu → trung binh)."""
    tb = np.array(tham_so["mean"])
    th = np.array(tham_so["scale"])
    th = np.where(th == 0, 1.0, th)
    Xv = np.where(np.isnan(X), tb, X)
    z = tham_so["intercept"] + ((Xv - tb) / th) @ np.array(tham_so["coef"])
    return 1 / (1 + np.exp(-z))


# ---------------------------------------------------------------- LightGBM

def hoc_lightgbm(hoc: DuLieu, kiem: DuLieu) -> KetQua:
    import lightgbm as lgb

    m = lgb.LGBMClassifier(n_estimators=200, learning_rate=0.05, num_leaves=31, min_child_samples=50,
                           subsample=0.8, subsample_freq=1, colsample_bytree=0.8, verbose=-1)
    m.fit(hoc.X, hoc.y)
    p = m.predict_proba(kiem.X)[:, 1]
    cay = [t["tree_structure"] for t in m.booster_.dump_model()["tree_info"]]
    return KetQua("lightgbm", auc(kiem.y, p), log_loss(kiem.y, p), {"trees": cay})


def du_doan_cay(tham_so: dict, X: np.ndarray) -> np.ndarray:
    """Duyet cay giong RungCay.DiemTho ben C# — dung de test file xuat ra cho cung ket qua."""
    kq = np.zeros(len(X))
    for i, x in enumerate(X):
        tong = 0.0
        for goc in tham_so["trees"]:
            n = goc
            while "leaf_value" not in n:
                v = x[n["split_feature"]]
                thieu = math.isnan(v) or (n.get("missing_type") == "Zero" and v == 0)
                if thieu and n.get("missing_type", "None") != "None":
                    trai = bool(n.get("default_left", False))
                else:
                    trai = (0.0 if math.isnan(v) else v) <= float(n["threshold"])
                n = n["left_child"] if trai else n["right_child"]
            tong += n["leaf_value"]
        kq[i] = 1 / (1 + math.exp(-tong))
    return kq


# ---------------------------------------------------------------- chon mo hinh

def nen_hoc_lightgbm(so_luot_hien: int, so_luot_bat_dau: int) -> bool:
    return so_luot_hien >= NGUONG_LUOT_HIEN and so_luot_bat_dau >= NGUONG_LUOT_BAT_DAU


def chon_tot_nhat(ung_vien: list[KetQua]) -> KetQua:
    """AUC cao hon truoc; bang nhau (lech &lt; 0,002) thi log-loss thap hon."""
    tot = ung_vien[0]
    for k in ung_vien[1:]:
        if k.auc > tot.auc + 0.002 or (abs(k.auc - tot.auc) <= 0.002 and k.log_loss < tot.log_loss):
            tot = k
    return tot


def hoc_mot_cau_hoi(cau_hoi: str, d: DuLieu, cho_lightgbm: bool) -> tuple[dict[str, KetQua], KetQua, DuLieu] | None:
    """Hoc mot cau hoi (start | pass): {loai: ket qua tren du lieu kiem}, G1 tren cung du lieu kiem, du lieu kiem (de
    cham lai ban dang chay). Thieu du lieu → None (giu mo hinh dang chay)."""
    if not du_de_hoc(d):
        return None
    hoc, kiem = chia_theo_thoi_gian(d)
    if not du_de_hoc(hoc) or len(kiem) == 0 or int(kiem.y.sum()) in (0, len(kiem)):
        return None
    p0 = du_doan_g1(cau_hoi, kiem.X)
    g1 = KetQua("formula", auc(kiem.y, p0), log_loss(kiem.y, p0))
    ung_vien = {"logistic": hoc_logistic(hoc, kiem)}
    if cho_lightgbm:
        ung_vien["lightgbm"] = hoc_lightgbm(hoc, kiem)
    return ung_vien, g1, kiem


def cham_mo_hinh_co_san(file_mo_hinh: dict | None, cau_hoi: str, kiem: DuLieu) -> KetQua:
    """Cham ban dang chay tren du lieu kiem moi (None = G1). Dac trung map theo ten nhu ben C#."""
    if not file_mo_hinh:
        p = du_doan_g1(cau_hoi, kiem.X)
        return KetQua("formula", auc(kiem.y, p), log_loss(kiem.y, p))
    ten = file_mo_hinh.get("features", DAC_TRUNG)
    cot = [DAC_TRUNG.index(t) if t in DAC_TRUNG else -1 for t in ten]
    X = np.full((len(kiem), len(cot)), np.nan)
    for j, c in enumerate(cot):
        if c >= 0:
            X[:, j] = kiem.X[:, c]
    tham_so = file_mo_hinh[cau_hoi]
    if file_mo_hinh["kind"] == "logistic":
        p = du_doan_logistic(tham_so, X)
    else:
        p = du_doan_cay(tham_so, X)
    return KetQua(file_mo_hinh["kind"], auc(kiem.y, p), log_loss(kiem.y, p))


def tot_hon(moi: KetQua, cu: KetQua, nguong_auc: float = 0.005) -> bool:
    """Ban moi phai hon ro rang ban dang chay (AUC + 0,005, hoac AUC ngang ma log-loss thap hon)."""
    return moi.auc > cu.auc + nguong_auc or (abs(moi.auc - cu.auc) <= nguong_auc and moi.log_loss < cu.log_loss - 1e-4)


def tao_file_mo_hinh(phien_ban: str, start: KetQua, pass_: KetQua) -> dict:
    """File JSON reco-svc doc (MoHinhHocMay.Doc). Hai cau hoi phai cung loai mo hinh."""
    if start.loai != pass_.loai:
        raise ValueError("Hai cau hoi phai cung loai mo hinh")
    return {
        "version": phien_ban,
        "kind": start.loai,
        "features": DAC_TRUNG,
        "start": start.tham_so,
        "pass": pass_.tham_so,
        "metrics": {"start_auc": start.auc, "start_log_loss": start.log_loss,
                    "pass_auc": pass_.auc, "pass_log_loss": pass_.log_loss},
    }


# ---------------------------------------------------------------- A/B

@dataclass
class NhomAb:
    luot_hien: int
    bat_dau: int

    @property
    def ti_le(self) -> float:
        return self.bat_dau / self.luot_hien if self.luot_hien else 0.0


def kiem_dinh_hai_ti_le(dang_chay: NhomAb, thu: NhomAb) -> float:
    """z cua kiem dinh hai ti le (thu − dang chay). |z| > 1,96 ≈ co y nghia o muc 5 %."""
    n1, n2 = dang_chay.luot_hien, thu.luot_hien
    if n1 == 0 or n2 == 0:
        return 0.0
    p = (dang_chay.bat_dau + thu.bat_dau) / (n1 + n2)
    se = math.sqrt(p * (1 - p) * (1 / n1 + 1 / n2))
    if se == 0:
        return 0.0
    return (thu.ti_le - dang_chay.ti_le) / se


def quyet_dinh_ab(dang_chay: NhomAb, thu: NhomAb, so_ngay: int, ngay_toi_thieu: int = 14, ngay_toi_da: int = 28,
                  luot_toi_thieu: int = 1000) -> str:
    """promote | drop | keep. Thang co y nghia sau toi thieu 2 tuan → promote; thua co y nghia → drop ngay khi du
    luot; qua 4 tuan khong phan thang thua → drop (giu ban dang chay)."""
    if min(dang_chay.luot_hien, thu.luot_hien) < luot_toi_thieu:
        return "drop" if so_ngay > ngay_toi_da else "keep"
    z = kiem_dinh_hai_ti_le(dang_chay, thu)
    if z <= -1.96:
        return "drop"
    if so_ngay < ngay_toi_thieu:
        return "keep"
    if z >= 1.96:
        return "promote"
    return "drop" if so_ngay > ngay_toi_da else "keep"
