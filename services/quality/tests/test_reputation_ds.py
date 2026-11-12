import asyncio
from types import SimpleNamespace

import pandas as pd

from app import settings_store
from app.ds_batch import cong_cu_dung_duoc, gia_tri_roi_rac, tinh, uoc_luong
from app.reputation import ThamSo
from app.reputation import tinh as tinh_uy_tin

# Gia tri mac dinh cua setting quality.reputation_*.
TS = ThamSo(trong_so_gold=0.6, tien_nghiem=0.7, suc_nang_tien_nghiem=2.0)


def test_chua_co_bang_chung_thi_khong_cham():
    assert tinh_uy_tin(0, 0, 0, 0, None, TS) is None


def test_lam_muot_cold_start():
    # Mot cau vang dung khong vot len 100, mot cau sai khong roi xuong 0.
    assert tinh_uy_tin(1, 1, 0, 0, None, TS) == 80
    assert tinh_uy_tin(0, 1, 0, 0, None, TS) == 47


def test_tron_gold_va_dong_thuan():
    # gold 10/10 → (10 + 1.4) / 12 = 0.95; khop 0/10 → 1.4 / 12 = 0.1167
    # 100 x (0.6 x 0.95 + 0.4 x 0.1167) = 61.67 → 62
    assert tinh_uy_tin(10, 10, 0, 10, None, TS) == 62


def test_ds_thay_cho_ti_le_khop_tho():
    assert tinh_uy_tin(0, 0, 0, 10, 0.9, TS) == 90
    assert tinh_uy_tin(0, 0, 10, 10, 0.2, TS) == 20


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

    ky_nang = uoc_luong(df, min_nhan=5)

    assert ky_nang["w1"][0] > 0.9
    assert ky_nang["w3"][0] < 0.2
    assert ky_nang["w1"][1] == 6


def test_labeler_it_nhan_chua_duoc_cham_ds():
    df = pd.DataFrame([("t1", "w1", "a"), ("t1", "w2", "b"), ("t2", "w1", "a"), ("t2", "w2", "a")],
                      columns=["task", "worker", "label"])
    ky_nang = uoc_luong(df, min_nhan=5)
    assert ky_nang == {}


# ---- tinh(): chi so ca du an sau khi cham ----

def _nhan(sample_id, labeler_id, lop):
    return SimpleNamespace(sample_id=sample_id, labeler_id=labeler_id, data={"mau": {"labelIds": [lop]}}, sample_metadata=None)


SCHEMA_MAU = {"tools": [{"name": "mau", "kind": "classification", "classes": ["do", "vang", "xanh"]}]}

# Vi du P1 trong tai lieu: 10 cau, 6 labeler (A..F) — dung la DUNG, SAI la cau tra loi lech.
DUNG = {"s01": "xanh", "s02": "do", "s03": "vang", "s04": "xanh", "s05": "do",
        "s06": "vang", "s07": "do", "s08": "xanh", "s09": "vang", "s10": "do"}
SAI = {("B", "s04"): "do", ("C", "s05"): "vang", ("C", "s08"): "vang", ("D", "s05"): "vang", ("D", "s09"): "do",
       ("E", "s02"): "vang", ("E", "s08"): "vang", ("E", "s09"): "do", ("F", "s07"): "xanh"}


def _du_an_p1(nguoi):
    return [_nhan(s, w, SAI.get((w, s), d)) for s, d in DUNG.items() for w in nguoi]


def test_tinh_du_bai_ra_dung_so_cua_vi_du_p1(monkeypatch):
    monkeypatch.setattr(settings_store, "so_nguyen", lambda key: 5)
    kq = asyncio.run(tinh(SCHEMA_MAU, _du_an_p1("ABCDEF"), frozenset()))
    assert [k.ten for k in kq] == ["mau"]
    ky = {w: round(v[0], 3) for w, v in kq[0].ky_nang.items()}
    assert ky == {"A": 1.0, "B": 0.889, "C": 0.806, "D": 0.806, "E": 0.694, "F": 0.917}
    assert kq[0].chi["item_count"] == 10 and kq[0].chi["label_count"] == 60


def test_tinh_bo_cau_bi_loai(monkeypatch):
    # Cau bi loai (samples.excluded_at) khong vao chi so lan DS — giong luc cham.
    monkeypatch.setattr(settings_store, "so_nguyen", lambda key: 5)
    kq = asyncio.run(tinh(SCHEMA_MAU, _du_an_p1("ABCDEF"), frozenset({"s05", "s08"})))
    assert kq[0].chi["item_count"] == 8 and kq[0].chi["label_count"] == 48
    assert all(v[1] == 8 for v in kq[0].ky_nang.values())


def test_tinh_khong_co_nhan_thi_rong():
    assert asyncio.run(tinh(SCHEMA_MAU, [], frozenset())) == []
