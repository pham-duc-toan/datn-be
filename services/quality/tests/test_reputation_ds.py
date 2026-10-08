import pandas as pd

from app.ds_batch import cong_cu_dung_duoc, gia_tri_roi_rac, uoc_luong
from app.reputation import ThamSo, tinh

# Gia tri mac dinh cua setting quality.reputation_*.
TS = ThamSo(trong_so_gold=0.6, tien_nghiem=0.7, suc_nang_tien_nghiem=2.0)


def test_chua_co_bang_chung_thi_khong_cham():
    assert tinh(0, 0, 0, 0, None, TS) is None


def test_lam_muot_cold_start():
    # Mot cau vang dung khong vot len 100, mot cau sai khong roi xuong 0.
    assert tinh(1, 1, 0, 0, None, TS) == 80
    assert tinh(0, 1, 0, 0, None, TS) == 47


def test_tron_gold_va_dong_thuan():
    # gold 10/10 → (10 + 1.4) / 12 = 0.95; khop 0/10 → 1.4 / 12 = 0.1167
    # 100 x (0.6 x 0.95 + 0.4 x 0.1167) = 61.67 → 62
    assert tinh(10, 10, 0, 10, None, TS) == 62


def test_ds_thay_cho_ti_le_khop_tho():
    assert tinh(0, 0, 0, 10, 0.9, TS) == 90
    assert tinh(0, 0, 10, 10, 0.2, TS) == 20


def test_chon_cong_cu_dung_duoc_cho_ds():
    schema = {"tools": [
        {"name": "a", "kind": "classification", "allowMultiple": False},
        {"name": "b", "kind": "classification", "allowMultiple": True},
        {"name": "c", "kind": "pairwise"},
        {"name": "d", "kind": "bbox"},
    ]}
    assert [t["name"] for t in cong_cu_dung_duoc(schema)] == ["a", "c"]
    assert gia_tri_roi_rac("classification", {"labelIds": ["x"]}) == "x"
    assert gia_tri_roi_rac("classification", {"labelIds": ["x", "y"]}) is None
    assert gia_tri_roi_rac("pairwise", {"choice": "tie"}) == "tie"
    assert gia_tri_roi_rac("pairwise", None) is None


def test_dawid_skene_phat_hien_labeler_kem():
    # w1, w2 dung; w3 tra loi nguoc o moi task.
    dong = []
    that = ["a", "b", "a", "b", "a", "b"]
    for i, t in enumerate(that):
        dong.append((f"t{i}", "w1", t))
        dong.append((f"t{i}", "w2", t))
        dong.append((f"t{i}", "w3", "b" if t == "a" else "a"))
    df = pd.DataFrame(dong, columns=["task", "worker", "label"])

    ky_nang, alpha = uoc_luong(df, min_nhan=5)

    assert ky_nang["w1"][0] > 0.9
    assert ky_nang["w3"][0] < 0.2
    assert ky_nang["w1"][1] == 6
    assert alpha is not None


def test_labeler_it_nhan_chua_duoc_cham_ds():
    df = pd.DataFrame([("t1", "w1", "a"), ("t1", "w2", "b"), ("t2", "w1", "a"), ("t2", "w2", "a")],
                      columns=["task", "worker", "label"])
    ky_nang, _ = uoc_luong(df, min_nhan=5)
    assert ky_nang == {}
