from app.consensus import Nhan, danh_gia

PHAN_LOAI = {"modality": "image", "tools": [
    {"name": "loai", "kind": "classification", "classes": ["cho", "meo", "ga"], "allowMultiple": False},
]}

NHIEU_LOP = {"modality": "image", "tools": [
    {"name": "the", "kind": "classification", "classes": ["a", "b", "c"], "allowMultiple": True},
]}

CAP = {"modality": "pair", "tools": [
    {"name": "tot_hon", "kind": "pairwise", "allowTie": True},
    {"name": "an_toan", "kind": "classification", "classes": ["an_toan", "khong_an_toan"], "required": False},
]}

KHUNG = {"modality": "image", "tools": [{"name": "vat", "kind": "bbox", "classes": ["xe"]}]}


def lop(*ds):
    return {"loai": {"labelIds": list(ds)}}


def nhan(i, data):
    return Nhan(f"a{i}", f"l{i}", data)


def test_da_so_tuyet_doi_thang_va_danh_dau_khop():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("cho")), nhan(3, lop("meo"))], target=3, max_redundancy=3)

    assert kq.status == "agreed"
    assert kq.final == {"loai": {"labelIds": ["cho"]}}
    assert kq.agrees == {"a1": True, "a2": True, "a3": False}
    assert kq.xin_them is None


def test_hoa_phieu_con_tran_thi_xin_them_mot_nguoi():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("meo"))], target=2, max_redundancy=4)

    assert kq.status == "disputed"
    assert kq.xin_them == 3


def test_hoa_phieu_het_tran_thi_chot_tranh_chap():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("meo"))], target=2, max_redundancy=2)

    assert kq.status == "disputed"
    assert kq.xin_them is None
    assert kq.final == {"loai": None}
    # Cong cu tranh chap khong dung de cham khop / lech.
    assert kq.agrees == {"a1": None, "a2": None}


def test_vong_hai_sau_khi_xin_them_nguoi_thu_ba_pha_the_hoa():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("meo")), nhan(3, lop("meo"))], target=3, max_redundancy=4)

    assert kq.status == "agreed"
    assert kq.final == {"loai": {"labelIds": ["meo"]}}
    assert kq.agrees["a1"] is False


def test_nhieu_lop_xet_tung_lop_doc_lap():
    ds = [nhan(1, {"the": {"labelIds": ["a", "b"]}}), nhan(2, {"the": {"labelIds": ["a"]}}), nhan(3, {"the": {"labelIds": ["a", "c"]}})]
    kq = danh_gia(NHIEU_LOP, ds, target=3, max_redundancy=3)

    assert kq.final == {"the": {"labelIds": ["a"]}}
    # Khop = dung TAP lop chot.
    assert kq.agrees == {"a1": False, "a2": True, "a3": False}


def test_pairwise_va_cong_cu_tuy_chon_bo_trong():
    ds = [
        nhan(1, {"tot_hon": {"choice": "a"}}),
        nhan(2, {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}}),
        nhan(3, {"tot_hon": {"choice": "tie"}}),
    ]
    kq = danh_gia(CAP, ds, target=3, max_redundancy=3)

    assert kq.status == "agreed"
    assert kq.final == {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}}
    assert kq.agrees == {"a1": True, "a2": True, "a3": False}


def test_tap_nhan_khong_gop_duoc_thi_not_applicable():
    kq = danh_gia(KHUNG, [nhan(1, {"vat": []}), nhan(2, {"vat": []})], target=2, max_redundancy=5)

    assert kq.status == "notApplicable"
    assert kq.xin_them is None
    assert kq.agrees == {"a1": None, "a2": None}
