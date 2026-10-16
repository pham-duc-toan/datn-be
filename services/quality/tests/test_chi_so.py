import numpy as np
import pandas as pd
import pytest

from app import chi_so


def _nominal(a, b):
    return 0.0 if a == b else 1.0


# ---------------- Fleiss' kappa ----------------


def test_fleiss_kappa_vi_du_wikipedia():
    # Vi du chuan (Fleiss 1971, 10 mau x 14 nguoi x 5 lop) — kappa = 0.210.
    bang = [
        [0, 0, 0, 0, 14], [0, 2, 6, 4, 2], [0, 0, 3, 5, 6], [0, 3, 9, 2, 0], [2, 2, 8, 1, 1],
        [7, 7, 0, 0, 0], [3, 2, 6, 3, 0], [2, 5, 3, 2, 2], [6, 5, 2, 1, 0], [0, 2, 2, 3, 7],
    ]
    don_vi = [[str(lop) for lop, so in enumerate(dong) for _ in range(so)] for dong in bang]
    assert chi_so.fleiss_kappa(don_vi) == pytest.approx(0.210, abs=1e-3)


def test_fleiss_kappa_dong_y_hoan_toan_va_suy_bien():
    assert chi_so.fleiss_kappa([["a", "a"], ["b", "b"], ["a", "a"]]) == pytest.approx(1.0)
    assert chi_so.fleiss_kappa([["a", "a"], ["a", "a"]]) is None      # chi mot gia tri: khong xac dinh
    assert chi_so.fleiss_kappa([["a"], ["b"]]) is None                # khong mau nao >= 2 nguoi


# ---------------- Krippendorff alpha ----------------


def test_alpha_nominal_khop_crowdkit():
    from crowdkit.metrics.data import alpha_krippendorff

    dong = [("t1", "w1", "a"), ("t1", "w2", "a"), ("t1", "w3", "b"),
            ("t2", "w1", "b"), ("t2", "w2", "b"),
            ("t3", "w1", "a"), ("t3", "w2", "b"), ("t3", "w3", "a"),
            ("t4", "w2", "c"), ("t4", "w3", "c")]
    df = pd.DataFrame(dong, columns=["task", "worker", "label"])
    don_vi = [list(g["label"]) for _, g in df.groupby("task")]
    assert chi_so.krippendorff_alpha(don_vi, _nominal) == pytest.approx(float(alpha_krippendorff(df)), abs=1e-9)


def test_alpha_interval_vi_du_krippendorff():
    # Krippendorff (2011) "Computing Krippendorff's alpha-reliability", du lieu 4 nguoi x 12 mau co thieu;
    # alpha interval = 0.849.
    nguoi = [
        [1, 2, 3, 3, 2, 1, 4, 1, 2, None, None, None],
        [1, 2, 3, 3, 2, 2, 4, 1, 2, 5, None, 3],
        [None, 3, 3, 3, 2, 3, 4, 2, 2, 5, 1, None],
        [1, 2, 3, 3, 2, 4, 4, 1, 2, 5, 1, None],
    ]
    don_vi = [[n[i] for n in nguoi if n[i] is not None] for i in range(12)]
    assert chi_so.krippendorff_alpha(don_vi, lambda a, b: (a - b) ** 2) == pytest.approx(0.849, abs=1e-3)
    # Cung du lieu, nominal = 0.743
    assert chi_so.krippendorff_alpha(don_vi, _nominal) == pytest.approx(0.743, abs=1e-3)


def test_alpha_khong_du_du_lieu():
    assert chi_so.krippendorff_alpha([[1, 1]], _nominal) is None
    assert chi_so.krippendorff_alpha([[1, 1], [1, 1]], _nominal) is None   # D_e = 0


def test_alpha_lay_mau_tat_dinh():
    don_vi = [[i % 5, (i + 1) % 5, i % 5] for i in range(200)]
    a1 = chi_so.krippendorff_alpha(don_vi, lambda a, b: (a - b) ** 2, toi_da_gia_tri=100)
    a2 = chi_so.krippendorff_alpha(don_vi, lambda a, b: (a - b) ** 2, toi_da_gia_tri=100)
    assert a1 == a2


# ---------------- Theo cong cu ----------------


def _vao(cong_cu_ket_qua):
    """[(sample, nguoi, ket_qua)] → dinh dang tinh_cho_cong_cu."""
    return [(s, n, k, {"width": 200, "height": 100}) for s, n, k in cong_cu_ket_qua]


def test_cong_cu_so_dong_y_cao_hon_lech():
    tool = {"name": "tuoi", "kind": "number"}
    dong_y = _vao([(f"s{i}", n, {"value": 10 * i + d}) for i in range(5) for n, d in (("a", 0), ("b", 1), ("c", 0))])
    lech = _vao([(f"s{i}", n, {"value": v}) for i in range(5) for n, v in (("a", 10 * i), ("b", 40 - 10 * i), ("c", 7))])
    a = chi_so.tinh_cho_cong_cu(tool, dong_y, {})
    b = chi_so.tinh_cho_cong_cu(tool, lech, {})
    assert a["metric"] == "interval" and a["kappa"] is None
    assert a["alpha"] > 0.95
    assert b["alpha"] < 0.2
    assert a["item_count"] == 5 and a["label_count"] == 15


def test_cong_cu_khung_set_f1():
    tool = {"name": "khung", "kind": "bbox", "classes": ["xe"]}

    def k(x):
        return [{"labelId": "xe", "x": x, "y": 10, "w": 40, "h": 30}]

    tot = _vao([(f"s{i}", n, k(20 * i + d)) for i in range(4) for n, d in (("a", 0), ("b", 1), ("c", 2))])
    kq = chi_so.tinh_cho_cong_cu(tool, tot, {"bbox": 0.5})
    assert kq["metric"] == "set_f1"
    assert kq["alpha"] > 0.9
    # Mot nguoi ve bua (lech xa) → alpha giam.
    xau = _vao([(f"s{i}", n, k(20 * i + d)) for i in range(4) for n, d in (("a", 0), ("b", 1), ("c", 150 - 20 * i))])
    assert chi_so.tinh_cho_cong_cu(tool, xau, {"bbox": 0.5})["alpha"] < kq["alpha"]


def test_cong_cu_phan_loai_co_kappa():
    tool = {"name": "lop", "kind": "classification", "classes": ["a", "b"]}
    vao = _vao([(f"s{i}", n, {"labelIds": ["a" if i % 2 else "b"]}) for i in range(6) for n in ("x", "y", "z")])
    kq = chi_so.tinh_cho_cong_cu(tool, vao, {})
    assert kq["metric"] == "nominal"
    assert kq["alpha"] == pytest.approx(1.0) and kq["kappa"] == pytest.approx(1.0)


def test_cong_cu_chep_loi_va_xep_hang():
    t1 = {"name": "chep", "kind": "transcription"}
    vao = _vao([(f"s{i}", n, {"text": f"cau so {i} rat dai"}) for i in range(4) for n in ("a", "b")])
    assert chi_so.tinh_cho_cong_cu(t1, vao, {})["metric"] == "cer"
    t2 = {"name": "hang", "kind": "ranking"}
    vao = _vao([(f"s{i}", n, {"order": [0, 1, 2] if i % 2 else [2, 1, 0]}) for i in range(4) for n in ("a", "b")])
    kq = chi_so.tinh_cho_cong_cu(t2, vao, {})
    assert kq["metric"] == "kendall" and kq["alpha"] == pytest.approx(1.0)


def test_free_text_khong_do():
    assert chi_so.tinh_cho_cong_cu({"name": "y", "kind": "freeText"}, _vao([("s", "a", {"text": "x"})]), {}) is None


def test_du_lieu_sai_dang_khong_lam_hong():
    tool = {"name": "tuoi", "kind": "number"}
    vao = _vao([("s1", "a", {"value": "khong phai so"}), ("s1", "b", {"value": 3}),
                ("s2", "a", {"value": 1}), ("s2", "b", {"value": 2})])
    assert chi_so.tinh_cho_cong_cu(tool, vao, {})["alpha"] is None


# ---------------- STAPLE ----------------


def _rle(bat: np.ndarray) -> dict:
    """Mang bool phang → RLE (bat dau bang doan 0)."""
    counts = []
    hien = False
    dem = 0
    for v in bat:
        if bool(v) == hien:
            dem += 1
        else:
            counts.append(dem)
            hien = not hien
            dem = 1
    counts.append(dem)
    return {"size": [10, 10], "counts": counts}


def test_giai_rle_khu_hai_chieu():
    m = np.zeros(100, dtype=bool)
    m[5:20] = True
    m[50:51] = True
    assert (chi_so.giai_rle(_rle(m)) == m).all()


def test_staple_phat_hien_nguoi_to_thieu_va_to_thua():
    rng = np.random.default_rng(1)
    mau = []
    for _ in range(6):
        that = np.zeros(100, dtype=bool)
        bat_dau = int(rng.integers(0, 50))
        that[bat_dau:bat_dau + 30] = True
        thieu = that.copy()
        thieu[bat_dau:bat_dau + 15] = False            # bo nua vat → do nhay thap
        thua = that.copy()
        thua[(bat_dau + 30) % 100:(bat_dau + 30) % 100 + 20] = True   # to lan sang nen → do dac hieu thap
        mau.append({"tot1": that, "tot2": that.copy(), "tot3": that.copy(), "thieu": thieu, "thua": thua})
    kq = chi_so.staple(mau)
    assert kq["tot1"][0] > 0.95 and kq["tot1"][1] > 0.95
    assert kq["thieu"][0] < 0.7 and kq["thieu"][1] > 0.95
    assert kq["thua"][0] > 0.95 and kq["thua"][1] < kq["tot1"][1]
    assert kq["tot1"][2] == 6


def test_cong_cu_mask_tra_staple():
    tool = {"name": "vung", "kind": "mask", "classes": ["duong"]}
    that = np.zeros(100, dtype=bool)
    that[20:60] = True
    thieu = that.copy()
    thieu[20:40] = False
    vao = []
    for i in range(3):
        for n, m in (("a", that), ("b", that), ("c", thieu)):
            vao.append((f"s{i}", n, [{"labelId": "duong", "rle": _rle(m)}], None))
    kq = chi_so.tinh_cho_cong_cu(tool, vao, {"mask": 0.5})
    assert kq["metric"] == "set_f1"
    assert set(kq["staple"]) == {"a", "b", "c"}
    assert kq["staple"]["c"][0] < kq["staple"]["a"][0]


def test_staple_tinh_ca_nen_khong_chi_vung_tranh_cai():
    # Khong ai to lan ra nen: nguoi tot van phai co do dac hieu ~ 1 (nen ca anh duoc dem).
    that = np.zeros(800, dtype=bool)
    that[100:300] = True
    thieu = np.zeros(800, dtype=bool)
    thieu[200:300] = True
    kq = chi_so.staple([{"a": that, "b": that.copy(), "c": thieu} for _ in range(4)])
    assert kq["a"][0] > 0.99 and kq["a"][1] > 0.99
    assert kq["c"][0] == pytest.approx(0.5, abs=0.01) and kq["c"][1] > 0.99
