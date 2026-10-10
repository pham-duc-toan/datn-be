import pytest

from app import redundancy as r
from app.consensus import ChinhSach, Nhan, danh_gia


def test_hau_nghiem_mot_dong_xu():
    # Hai labeler 0.9 cung chon "a" (nhi phan): P(a) = 0.81 / (0.81 + 0.01) ≈ 0.9878.
    h = r.hau_nghiem(["a", "a"], [0.9, 0.9], ["a", "b"])
    assert h["a"] == pytest.approx(0.81 / 0.82)
    # Nguoi gioi (0.95) thang hai nguoi kem (0.6) chon nguoc lai.
    h = r.hau_nghiem(["a", "b", "b"], [0.95, 0.6, 0.6], ["a", "b"])
    assert h["a"] > h["b"]


def test_labeler_te_hon_ngau_nhien_khong_bi_dao_nguoc():
    # Do chinh xac 0.2 bi kep ve ~ngau nhien: nhan cua ho gan nhu khong doi hau nghiem.
    h = r.hau_nghiem(["a"], [0.2], ["a", "b"])
    assert h["a"] == pytest.approx(0.5, abs=0.01)


def test_gia_tri_thong_tin_giam_khi_da_chac_chan():
    chua_chac = r.gia_tri_thong_tin({"a": 0.6, "b": 0.4}, 0.8)
    da_chac = r.gia_tri_thong_tin({"a": 0.99, "b": 0.01}, 0.8)
    assert chua_chac > da_chac >= 0
    # Nhan tu nguoi ngau nhien khong co gia tri.
    assert r.gia_tri_thong_tin({"a": 0.6, "b": 0.4}, 0.5) == pytest.approx(0, abs=1e-2)


def test_ba_chinh_sach_dung_dung_luc():
    lc = ["a", "b"]
    assert not r.quyet_dinh_majority(["a", "b"], 5).dung
    assert r.quyet_dinh_majority(["a", "a"], 5).dung
    assert r.quyet_dinh_majority(["a", "b"], 2).dung            # cham tran

    assert not r.quyet_dinh_posterior(["a"], [0.9], lc, 7, 0.95).dung
    assert r.quyet_dinh_posterior(["a", "a"], [0.9, 0.9], lc, 7, 0.95).dung

    # voi: ti le gia tri thap → mot nhan la du; cao → mua them.
    assert r.quyet_dinh_voi(["a"], [0.9], lc, 7, 0.8, 2).dung
    assert not r.quyet_dinh_voi(["a"], [0.9], lc, 7, 0.8, 1000).dung


def test_nhin_truoc_mot_buoc_danh_gia_thap_gia_tri():
    # P(a) = 0.9: mot nhan 0.8 khong lat duoc quyet dinh → gia tri 1 buoc = 0,
    # nhung hai nhan cung "b" thi lat → gia tri 2 buoc > 0.
    hau = {"a": 0.9, "b": 0.1}
    assert r.gia_tri_thong_tin(hau, 0.8, 1) == pytest.approx(0, abs=1e-12)
    assert r.gia_tri_thong_tin(hau, 0.8, 2) > 0
    assert r.dang_mua_them(hau, 0.8, 1000, 6)


SCHEMA = {"tools": [{"name": "loai", "kind": "classification", "classes": ["cho", "meo"], "allowMultiple": False}]}


def _n(i, lab, lop):
    return Nhan(f"a{i}", lab, {"loai": {"labelIds": [lop]}})


def test_dong_thuan_posterior_theo_do_chinh_xac():
    nhan = [_n(1, "gioi", "cho"), _n(2, "kem1", "meo"), _n(3, "kem2", "meo")]
    cs = ChinhSach(ten="posterior", nguong=0.9, do_chinh_xac={"gioi": 0.98, "kem1": 0.55, "kem2": 0.55})
    kq = danh_gia(SCHEMA, nhan, 3, 5, cs)
    # Majority se chon "meo"; posterior tin nguoi gioi.
    assert kq.status == "agreed" and kq.final == {"loai": {"labelIds": ["cho"]}}
    assert danh_gia(SCHEMA, nhan, 3, 5).final == {"loai": {"labelIds": ["meo"]}}


def test_dong_thuan_posterior_chua_du_tin_cay_thi_xin_them_hoac_tranh_chap_o_tran():
    nhan = [_n(1, "x", "cho"), _n(2, "y", "cho")]
    cs = ChinhSach(ten="posterior", nguong=0.99, mac_dinh=0.7)
    kq = danh_gia(SCHEMA, nhan, 2, 3, cs)
    assert kq.status == "disputed" and kq.xin_them == 3
    kq = danh_gia(SCHEMA, nhan, 3, 3, cs)
    assert kq.status == "disputed" and kq.xin_them is None and kq.final == {"loai": None}
