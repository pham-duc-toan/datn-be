"""Hoc mo hinh goi y (kien truc 3.39 muc 8): chon mo hinh theo du lieu, xuat file reco-svc doc duoc, quyet dinh A/B."""

import json

import numpy as np
import pytest

from app import reco_hoc as hoc


def _du_lieu(n: int, hat: int = 1) -> hoc.DuLieu:
    """Nhan phu thuoc thuong va so phut (G1 chi nhin muc hop the nen ban hoc may phai thang)."""
    rng = np.random.default_rng(hat)
    X = rng.uniform(0, 1, size=(n, len(hoc.DAC_TRUNG)))
    X[:, hoc.DAC_TRUNG.index("log_reward")] = rng.uniform(1, 5, n)
    X[:, hoc.DAC_TRUNG.index("est_minutes")] = rng.uniform(1, 60, n)
    z = 1.5 * X[:, 0] - 0.08 * X[:, 4] - 2
    y = (rng.uniform(0, 1, n) < 1 / (1 + np.exp(-z))).astype(int)
    return hoc.DuLieu(X, y, np.arange(n, dtype=float))


def test_auc_dung_voi_vi_du_tay():
    y = np.array([0, 0, 1, 1])
    assert hoc.auc(y, np.array([0.1, 0.4, 0.35, 0.8])) == pytest.approx(0.75)
    assert hoc.auc(y, np.array([0.5, 0.5, 0.5, 0.5])) == pytest.approx(0.5)
    assert hoc.auc(np.array([1, 1]), np.array([0.2, 0.9])) == 0.5


def test_chia_theo_thoi_gian_lay_phan_moi_nhat_lam_du_lieu_kiem():
    d = hoc.DuLieu(np.zeros((10, len(hoc.DAC_TRUNG))), np.array([0, 1] * 5), np.arange(10, 0, -1, dtype=float))
    hoc_, kiem = hoc.chia_theo_thoi_gian(d)
    assert len(hoc_) == 8 and len(kiem) == 2
    assert kiem.t.min() > hoc_.t.max()


def test_it_du_lieu_chi_hoc_logistic_va_thang_g1():
    kq = hoc.hoc_mot_cau_hoi("start", _du_lieu(3000), cho_lightgbm=hoc.nen_hoc_lightgbm(3000, 100))
    assert kq is not None
    ung_vien, g1, _ = kq
    assert set(ung_vien) == {"logistic"}
    assert ung_vien["logistic"].auc > g1.auc + 0.05
    assert hoc.tot_hon(ung_vien["logistic"], g1)


def test_thieu_du_lieu_thi_khong_hoc():
    assert hoc.hoc_mot_cau_hoi("start", _du_lieu(50), cho_lightgbm=False) is None
    d = _du_lieu(1000)
    d.y[:] = 0
    assert hoc.hoc_mot_cau_hoi("start", d, cho_lightgbm=False) is None


def test_nguong_chuyen_sang_lightgbm():
    assert not hoc.nen_hoc_lightgbm(199_999, 10_000)
    assert not hoc.nen_hoc_lightgbm(500_000, 4_999)
    assert hoc.nen_hoc_lightgbm(200_000, 5_000)


def test_cay_lightgbm_xuat_ra_cho_cung_xac_suat_voi_thu_vien():
    """du_doan_cay duyet cay GIONG RungCay ben C# — phai khop predict_proba cua LightGBM."""
    pytest.importorskip("lightgbm")
    d = _du_lieu(4000)
    d.X[::7, 3] = np.nan  # co dac trung thieu
    hoc_, kiem = hoc.chia_theo_thoi_gian(d)
    kq = hoc.hoc_lightgbm(hoc_, kiem)
    import lightgbm as lgb  # noqa: F401
    p_file = hoc.du_doan_cay(kq.tham_so, kiem.X[:200])
    assert kq.auc > 0.6
    # Dung lai tu chinh tham so da xuat: so voi xac suat cua thu vien tren cung du lieu.
    m = lgb.LGBMClassifier(n_estimators=200, learning_rate=0.05, num_leaves=31, min_child_samples=50,
                           subsample=0.8, subsample_freq=1, colsample_bytree=0.8, verbose=-1)
    m.fit(hoc_.X, hoc_.y)
    cay = [t["tree_structure"] for t in m.booster_.dump_model()["tree_info"]]
    np.testing.assert_allclose(hoc.du_doan_cay({"trees": cay}, kiem.X[:200]), m.predict_proba(kiem.X[:200])[:, 1], atol=1e-6)
    assert p_file.shape == (200,)


def test_logistic_xuat_ra_cho_cung_xac_suat_khi_doc_lai():
    d = _du_lieu(2000)
    hoc_, kiem = hoc.chia_theo_thoi_gian(d)
    kq = hoc.hoc_logistic(hoc_, kiem)
    p = hoc.du_doan_logistic(kq.tham_so, kiem.X)
    assert hoc.auc(kiem.y, p) == pytest.approx(kq.auc)


def test_file_mo_hinh_dung_hop_dong_reco_svc():
    d = _du_lieu(2000)
    s = hoc.hoc_mot_cau_hoi("start", d, False)
    p = hoc.hoc_mot_cau_hoi("pass", _du_lieu(2000, hat=2), False)
    f = hoc.tao_file_mo_hinh("log-1", s[0]["logistic"], p[0]["logistic"])
    f = json.loads(json.dumps(f))  # phai serialize duoc
    assert f["kind"] == "logistic" and f["features"] == hoc.DAC_TRUNG
    assert len(f["start"]["coef"]) == len(hoc.DAC_TRUNG)
    # Ban dang chay doc lai tu file van cham duoc tren du lieu kiem.
    kq = hoc.cham_mo_hinh_co_san(f, "start", s[2])
    assert kq.auc == pytest.approx(s[0]["logistic"].auc)
    with pytest.raises(ValueError):
        hoc.tao_file_mo_hinh("x", s[0]["logistic"], hoc.KetQua("lightgbm", 0.5, 1.0))


def test_quyet_dinh_ab():
    dang = hoc.NhomAb(10_000, 1_000)
    tot = hoc.NhomAb(10_000, 1_200)
    te = hoc.NhomAb(10_000, 800)
    ngang = hoc.NhomAb(10_000, 1_010)
    assert hoc.kiem_dinh_hai_ti_le(dang, tot) > 1.96
    assert hoc.quyet_dinh_ab(dang, tot, so_ngay=15) == "promote"
    assert hoc.quyet_dinh_ab(dang, tot, so_ngay=5) == "keep"      # chua du 2 tuan
    assert hoc.quyet_dinh_ab(dang, te, so_ngay=3) == "drop"       # thua ro thi dung ngay
    assert hoc.quyet_dinh_ab(dang, ngang, so_ngay=20) == "keep"
    assert hoc.quyet_dinh_ab(dang, ngang, so_ngay=30) == "drop"   # qua 4 tuan khong phan thang thua
    assert hoc.quyet_dinh_ab(hoc.NhomAb(10, 1), hoc.NhomAb(10, 5), so_ngay=20) == "keep"  # chua du luot
