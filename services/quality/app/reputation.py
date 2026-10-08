"""Diem uy tin labeler, thang 0-100 — logic thuan, de test va giai thich duoc.

Hai nguon bang chung:
  gold   — ti le tra loi dung cau vang kiem tra (do truc tiep, khong gian lan duoc
           neu dap an khong roi server — VD-Q-01);
  dong thuan — ti le nhan khop ket qua dong thuan. Khi Dawid-Skene da du du lieu
           (VD-Q-07) thi dung do tin cay DS thay cho ti le khop tho: DS tinh ca viec
           "dong thuan voi ai" (khop voi nguoi gioi co gia tri hon khop voi dam dong).

Moi ti le duoc LAM MUOT Bayes ve muc tien nghiem (mac dinh 0.7) voi suc nang
(mac dinh 2 nhan): nguoi moi lam 1 cau dung khong vot len 100, lam 1 cau sai
khong roi xuong 0 (cold start, VD-Q-04).

    uy_tin = 100 x (w x gold + (1 - w) x dong_thuan)      w mac dinh 0.6
Thieu mot nguon thi lay nguon con lai. Chua co bang chung nao → None (khong phat).

Ba tham so la setting dong quality.reputation_* (admin-svc) — xem tham_so_hien_tai().
"""

from dataclasses import dataclass

from app import settings_store


@dataclass(frozen=True)
class ThamSo:
    trong_so_gold: float
    tien_nghiem: float
    suc_nang_tien_nghiem: float


def tham_so_hien_tai() -> ThamSo:
    return ThamSo(
        trong_so_gold=settings_store.so_thuc(settings_store.REPUTATION_GOLD_WEIGHT),
        tien_nghiem=settings_store.so_thuc(settings_store.REPUTATION_PRIOR),
        suc_nang_tien_nghiem=settings_store.so_thuc(settings_store.REPUTATION_PRIOR_STRENGTH),
    )


def lam_muot(dung: int, tong: int, ts: ThamSo) -> float:
    return (dung + ts.tien_nghiem * ts.suc_nang_tien_nghiem) / (tong + ts.suc_nang_tien_nghiem)


def tinh(gold_dung: int, gold_tong: int, khop: int, khop_tong: int, ds_skill: float | None, ts: ThamSo) -> int | None:
    co_gold = gold_tong > 0
    co_dong_thuan = ds_skill is not None or khop_tong > 0

    if not co_gold and not co_dong_thuan:
        return None

    dong_thuan = ds_skill if ds_skill is not None else (lam_muot(khop, khop_tong, ts) if khop_tong > 0 else None)
    gold = lam_muot(gold_dung, gold_tong, ts) if co_gold else None

    if gold is not None and dong_thuan is not None:
        diem = ts.trong_so_gold * gold + (1 - ts.trong_so_gold) * dong_thuan
    elif gold is not None:
        diem = gold
    else:
        diem = dong_thuan

    return max(0, min(100, round(100 * diem)))
