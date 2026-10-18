"""Cham ca du an (docs 3.18) — ham thuan app/cham.py, khong can DB."""

from app.cham import Bai, cham_du_an, ti_le

TAP_NHAN = {
    "modality": "image",
    "tools": [{"name": "loai", "kind": "classification", "classes": ["cho", "meo", "ga"]}],
}

HAI_CONG_CU = {
    "modality": "image",
    "tools": [
        {"name": "loai", "kind": "classification", "classes": ["cho", "meo"]},
        {"name": "mau", "kind": "classification", "classes": ["den", "trang"]},
        {"name": "ghi_chu", "kind": "freeText", "required": False},
    ],
}

CAU = ("c1", "c2", "c3", "c4", "c5")


def lop(*ds: str) -> dict:
    return {"loai": {"labelIds": list(ds)}}


def bai(ma: str, vang_dung: int = 2, vang_tong: int = 2, cau: tuple[str, ...] = CAU) -> Bai:
    return Bai(ma, "lab-" + ma, cau, vang_tong, vang_dung)


def tra_loi(ma: str, dap_an: dict[str, str | None]) -> dict[tuple[str, str], dict]:
    return {(ma, c): lop(a) for c, a in dap_an.items() if a is not None}


def test_ti_le_lam_tron_xuong_va_khong_cau_nao_so_duoc_la_100():
    assert ti_le(2, 3) == 66
    assert ti_le(3, 3) == 100
    assert ti_le(0, 0) == 100


def test_bai_giong_da_so_du_nguong_va_dung_vang_thi_dat():
    nhan = {}
    nhan |= tra_loi("a", {"c1": "cho", "c2": "cho", "c3": "meo", "c4": "ga", "c5": "cho"})
    nhan |= tra_loi("b", {"c1": "cho", "c2": "cho", "c3": "meo", "c4": "ga", "c5": "meo"})
    nhan |= tra_loi("c", {"c1": "meo", "c2": "meo", "c3": "ga", "c4": "cho", "c5": "meo"})

    kq = cham_du_an(TAP_NHAN, [bai("a"), bai("b"), bai("c")], nhan, {}, set(), 80)

    assert kq.dung_da_so
    theo = {b.attempt_id: b for b in kq.bai}
    # c1, c2: cho (a, b); c3: meo (a, b); c4: ga (a, b); c5: meo (b, c) — moi cau deu co da so.
    assert (theo["a"].so_duoc, theo["a"].giong, theo["a"].phan_tram, theo["a"].dat) == (5, 4, 80, True)
    assert (theo["b"].giong, theo["b"].phan_tram, theo["b"].dat) == (5, 100, True)
    assert (theo["c"].giong, theo["c"].phan_tram, theo["c"].dat) == (1, 20, False)
    assert {c.status for c in kq.cau} == {"agreed"}


def test_sai_cau_vang_thi_truot_du_giong_da_so():
    nhan = {}
    for ma in ("a", "b", "c"):
        nhan |= tra_loi(ma, {c: "cho" for c in CAU})

    kq = cham_du_an(TAP_NHAN, [bai("a"), bai("b", vang_dung=1), bai("c")], nhan, {}, set(), 60)

    theo = {b.attempt_id: b for b in kq.bai}
    assert theo["b"].phan_tram == 100
    assert not theo["b"].dat
    assert theo["a"].dat and theo["c"].dat


def test_cau_bo_trong_tinh_la_khong_giong():
    nhan = {}
    nhan |= tra_loi("a", {"c1": "cho", "c2": "cho", "c3": None, "c4": None, "c5": "cho"})
    nhan |= tra_loi("b", {c: "cho" for c in CAU})
    nhan |= tra_loi("c", {c: "cho" for c in CAU})

    kq = cham_du_an(TAP_NHAN, [bai("a"), bai("b"), bai("c")], nhan, {}, set(), 80)

    a = next(b for b in kq.bai if b.attempt_id == "a")
    assert (a.so_duoc, a.giong, a.phan_tram, a.da_tra_loi, a.dat) == (5, 3, 60, 3, False)


def test_cau_hoa_phieu_va_cau_bi_loai_khong_tinh_cho_ai():
    nhan = {}
    nhan |= tra_loi("a", {"c1": "cho", "c2": "cho", "c3": "cho", "c4": "cho", "c5": "cho"})
    nhan |= tra_loi("b", {"c1": "meo", "c2": "cho", "c3": "cho", "c4": "cho", "c5": "cho"})
    nhan |= tra_loi("c", {"c1": "ga", "c2": "cho", "c3": "cho", "c4": "cho", "c5": "meo"})

    # c1: ba nguoi ba lop → khong co da so; c5 bi loai (bao loi duoc chap nhan).
    kq = cham_du_an(TAP_NHAN, [bai("a"), bai("b"), bai("c")], nhan, {}, {"c5"}, 80)

    for b in kq.bai:
        assert b.so_duoc == 3
        assert b.giong == 3
        assert b.dat
    trang_thai = {c.sample_id: c.status for c in kq.cau}
    assert trang_thai["c1"] == "disputed"
    assert "c5" not in trang_thai


def test_nhieu_cong_cu_chi_giong_khi_khop_moi_cong_cu_co_ket_qua():
    def n(loai: str, mau: str) -> dict:
        return {"loai": {"labelIds": [loai]}, "mau": {"labelIds": [mau]}, "ghi_chu": "tuy y"}

    nhan = {("a", "c1"): n("cho", "den"), ("b", "c1"): n("cho", "den"), ("c", "c1"): n("cho", "trang")}
    ds = [bai("a", cau=("c1",)), bai("b", cau=("c1",)), bai("c", cau=("c1",))]

    kq = cham_du_an(HAI_CONG_CU, ds, nhan, {}, set(), 100)

    theo = {b.attempt_id: b for b in kq.bai}
    assert theo["a"].giong == 1 and theo["b"].giong == 1
    # c dung "loai" nhung lech "mau" → khong giong.
    assert theo["c"].giong == 0 and theo["c"].so_duoc == 1


def test_it_hon_ba_bai_chi_xet_cau_vang():
    nhan = tra_loi("a", {"c1": "cho"}) | tra_loi("b", {"c1": "meo"})

    kq = cham_du_an(TAP_NHAN, [bai("a"), bai("b", vang_dung=0)], nhan, {}, set(), 95)

    assert not kq.dung_da_so
    assert kq.cau == []
    theo = {b.attempt_id: b for b in kq.bai}
    assert (theo["a"].phan_tram, theo["a"].dat, theo["a"].da_tra_loi) == (100, True, 1)
    assert not theo["b"].dat


def test_khong_co_bai_nao():
    kq = cham_du_an(TAP_NHAN, [], {}, {}, set(), 80)
    assert kq.bai == [] and not kq.dung_da_so


def test_khong_cau_vang_thi_coi_nhu_dung_het():
    nhan = {}
    for ma in ("a", "b", "c"):
        nhan |= tra_loi(ma, {c: "cho" for c in CAU})

    kq = cham_du_an(TAP_NHAN, [bai(m, vang_dung=0, vang_tong=0) for m in ("a", "b", "c")], nhan, {}, set(), 90)

    assert all(b.dat for b in kq.bai)
