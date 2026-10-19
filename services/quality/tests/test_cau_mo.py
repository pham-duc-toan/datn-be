"""Cham cau tra loi mo (freeText) theo nghia — app/cau_mo.py + tich hop app/cham.py (kien truc 3.19)."""

import math

from app import cau_mo
from app.cham import Bai, ThamSoMoDuAn, cham_du_an

MO = {"modality": "image", "tools": [{"name": "mo_ta", "kind": "freeText"}]}
PHAN_LOAI_VA_MO = {"modality": "image", "tools": [
    {"name": "loai", "kind": "classification", "classes": ["cho", "meo"]},
    {"name": "mo_ta", "kind": "freeText", "required": False},
]}


def v(goc: float) -> list[float]:
    """Vector don vi 2 chieu theo goc (do): cosine hai vector = cos(hieu goc)."""
    r = math.radians(goc)
    return [math.cos(r), math.sin(r)]


def test_van_ban_doc_payload():
    assert cau_mo.van_ban({"text": "  Con meo  "}) == "Con meo"
    assert cau_mo.van_ban({"text": 5}) == "" and cau_mo.van_ban(None) == "" and cau_mo.van_ban({}) == ""


def test_theo_nghia_medoid_va_nguoi_lac_de():
    # Ba cau cung y (goc 0, 20, 35) + mot cau lac de (goc 90). Medoid = cau co TONG do giong lon nhat:
    # goc 35 (0,82 + 0,97 + 0,57 = 2,36) hon goc 20 (0,94 + 0,97 + 0,34 = 2,25).
    kq = cau_mo.gop_mo(["a", "b", "c", "d"], [v(0), v(20), v(35), v(90)], 0.8)
    assert kq["final"] == {"text": "c"} and not kq["disputed"]
    assert kq["agrees"] == [True, True, True, False]       # cos35 = 0,82; cos15 = 0,97; cos55 = 0,57
    assert kq["similarity"][3] < 0.6


def test_moi_nguoi_mot_y_la_tranh_chap():
    kq = cau_mo.gop_mo(["a", "b", "c"], [v(0), v(60), v(120)], 0.8)
    assert kq["disputed"] and kq["final"] is None
    assert sum(kq["agrees"]) == 1                            # chi medoid giong chinh no


def test_cau_rong_khong_bao_gio_giong_va_tinh_vao_mau_so():
    # 2 cau giong nhau + 2 nguoi bo trong: 2 ung ho / 4 nguoi khong qua nua → tranh chap.
    kq = cau_mo.gop_mo(["a", "", "b", ""], [v(0), None, v(5), None], 0.8)
    assert kq["agrees"] == [True, False, True, False] and kq["disputed"]
    assert cau_mo.gop_mo(["", ""], [None, None], 0.8)["disputed"]


def test_thieu_vector_thi_so_chu():
    cau = ["Con mèo đang ngủ trên ghế sofa", "Con mèo ngủ trên ghế sofa", "Giá vàng hôm nay tăng mạnh"]
    kq = cau_mo.gop_mo(cau, [v(0), None, v(10)], 0.38)     # mot vector thieu → ca cau hoi so chu
    assert kq["agrees"] == [True, True, False] and not kq["disputed"]
    assert cau_mo.gop_mo(cau, None, 0.38)["agrees"] == [True, True, False]


def test_giong_chu_khong_phan_biet_hoa_thuong_va_dau_cau():
    assert math.isclose(cau_mo.giong_chu("Con mèo, ngủ!", "con mèo ngủ"), 1.0)
    assert cau_mo.giong_chu("Con mèo đang ngủ", "Giá vàng tăng") < 0.15
    assert cau_mo.giong_chu("", "x") == 0.0


def _bai(ma: str, cau=("c1", "c2")) -> Bai:
    return Bai(ma, "lab-" + ma, cau, 0, 0)


def test_cham_du_an_chi_co_cau_mo_theo_nghia():
    nhan = {(a, c): {"mo_ta": {"text": f"{a}-{c}"}} for a in ("a", "b", "c") for c in ("c1", "c2")}
    vec = {("a", "c1", "mo_ta"): v(0), ("b", "c1", "mo_ta"): v(10), ("c", "c1", "mo_ta"): v(80),
           ("a", "c2", "mo_ta"): v(0), ("b", "c2", "mo_ta"): v(5), ("c", "c2", "mo_ta"): v(90)}
    kq = cham_du_an(MO, [_bai("a"), _bai("b"), _bai("c")], nhan, {}, set(), 60,
                    mo=ThamSoMoDuAn(cau_mo.THEO_NGHIA, 0.8, vec))
    theo = {b.attempt_id: b for b in kq.bai}
    assert kq.dung_da_so
    assert (theo["a"].so_duoc, theo["a"].giong, theo["a"].dat) == (2, 2, True)
    assert (theo["c"].giong, theo["c"].phan_tram, theo["c"].dat) == (0, 0, False)
    assert {c.status for c in kq.cau} == {"agreed"}


def test_khong_dua_tham_so_mo_thi_cau_mo_khong_tinh_cho_ai():
    # Hanh vi truoc 3.19 (va moi noi goi khac): bai chi co cau tra loi mo → khong cau nao so duoc → 100%.
    nhan = {(a, "c1"): {"mo_ta": {"text": "x"}} for a in ("a", "b", "c")}
    kq = cham_du_an(MO, [_bai("a", ("c1",)), _bai("b", ("c1",)), _bai("c", ("c1",))], nhan, {}, set(), 60)
    assert all(b.so_duoc == 0 and b.phan_tram == 100 for b in kq.bai)


def test_phan_loai_kem_cau_mo_phai_khop_ca_hai():
    nhan = {(a, "c1"): {"loai": {"labelIds": ["cho"]}, "mo_ta": {"text": a}} for a in ("a", "b", "c")}
    vec = {("a", "c1", "mo_ta"): v(0), ("b", "c1", "mo_ta"): v(5), ("c", "c1", "mo_ta"): v(90)}
    kq = cham_du_an(PHAN_LOAI_VA_MO, [_bai("a", ("c1",)), _bai("b", ("c1",)), _bai("c", ("c1",))], nhan, {}, set(), 60,
                    mo=ThamSoMoDuAn(cau_mo.THEO_NGHIA, 0.8, vec))
    theo = {b.attempt_id: b for b in kq.bai}
    assert theo["a"].giong == 1 and theo["b"].giong == 1
    assert theo["c"].giong == 0          # dung lop nhung mo ta lac de


def test_bo_trong_cong_cu_mo_khong_bat_buoc_thi_chi_cham_cong_cu_con_lai():
    nhan = {(a, "c1"): {"loai": {"labelIds": ["cho"]}} for a in ("a", "b", "c")}
    kq = cham_du_an(PHAN_LOAI_VA_MO, [_bai("a", ("c1",)), _bai("b", ("c1",)), _bai("c", ("c1",))], nhan, {}, set(), 60,
                    mo=ThamSoMoDuAn(cau_mo.THEO_NGHIA, 0.8, {}))
    assert all(b.giong == 1 for b in kq.bai)
