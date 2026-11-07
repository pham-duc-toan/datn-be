from app.consensus import Nhan, ThamSoGop, danh_gia

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

MO_TA = {"modality": "image", "tools": [{"name": "mo_ta", "kind": "freeText"}]}

KHUNG_VA_LOAI = {"modality": "image", "tools": [
    {"name": "loai", "kind": "classification", "classes": ["ngay", "dem"], "allowMultiple": False},
    {"name": "vat", "kind": "bbox", "classes": ["xe"]},
]}


def khung(x, y, w=40, h=40):
    return {"labelId": "xe", "x": x, "y": y, "w": w, "h": h}


def lop(*ds):
    return {"loai": {"labelIds": list(ds)}}


def nhan(i, data):
    return Nhan(f"a{i}", f"l{i}", data)


def test_da_so_tuyet_doi_thang_va_danh_dau_khop():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("cho")), nhan(3, lop("meo"))])

    assert kq.status == "agreed"
    assert kq.final == {"loai": {"labelIds": ["cho"]}}
    assert kq.agrees == {"a1": True, "a2": True, "a3": False}


def test_hoa_phieu_thi_tranh_chap():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("meo"))])

    assert kq.status == "disputed"
    assert kq.final == {"loai": None}
    # Cong cu tranh chap khong dung de cham khop / lech.
    assert kq.agrees == {"a1": None, "a2": None}


def test_nguoi_thu_ba_pha_the_hoa():
    kq = danh_gia(PHAN_LOAI, [nhan(1, lop("cho")), nhan(2, lop("meo")), nhan(3, lop("meo"))])

    assert kq.status == "agreed"
    assert kq.final == {"loai": {"labelIds": ["meo"]}}
    assert kq.agrees["a1"] is False


def test_nhieu_lop_xet_tung_lop_doc_lap():
    ds = [nhan(1, {"the": {"labelIds": ["a", "b"]}}), nhan(2, {"the": {"labelIds": ["a"]}}), nhan(3, {"the": {"labelIds": ["a", "c"]}})]
    kq = danh_gia(NHIEU_LOP, ds)

    assert kq.final == {"the": {"labelIds": ["a"]}}
    # Khop = dung TAP lop chot.
    assert kq.agrees == {"a1": False, "a2": True, "a3": False}


def test_pairwise_va_cong_cu_tuy_chon_bo_trong():
    ds = [
        nhan(1, {"tot_hon": {"choice": "a"}}),
        nhan(2, {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}}),
        nhan(3, {"tot_hon": {"choice": "tie"}}),
    ]
    kq = danh_gia(CAP, ds)

    assert kq.status == "agreed"
    assert kq.final == {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}}
    assert kq.agrees == {"a1": True, "a2": True, "a3": False}


def test_tap_nhan_chi_co_cau_tra_loi_mo_thi_not_applicable():
    ds = [nhan(1, {"mo_ta": {"text": "mot con meo"}}), nhan(2, {"mo_ta": {"text": "con meo nam ngu"}})]
    kq = danh_gia(MO_TA, ds)

    assert kq.status == "notApplicable"
    assert kq.agrees == {"a1": None, "a2": None}


def test_khung_trung_nhau_thi_dong_thuan_va_lay_trung_vi():
    ds = [nhan(1, {"vat": [khung(10, 10)]}), nhan(2, {"vat": [khung(12, 10)]}), nhan(3, {"vat": [khung(11, 14)]})]
    kq = danh_gia(KHUNG, ds)

    assert kq.status == "agreed"
    assert kq.final == {"vat": [{"x": 11, "y": 10, "w": 40, "h": 40, "labelId": "xe"}]}
    assert kq.agrees == {"a1": True, "a2": True, "a3": True}


def test_nguoi_thu_ba_pha_the_hoa_va_nguoi_ve_lech_bi_cham_khong_khop():
    ds = [nhan(1, {"vat": [khung(10, 10)]}), nhan(2, {"vat": [khung(150, 150)]}), nhan(3, {"vat": [khung(11, 11)]})]
    kq = danh_gia(KHUNG, ds)

    assert kq.status == "agreed"
    assert kq.final == {"vat": [{"x": 10.5, "y": 10.5, "w": 40, "h": 40, "labelId": "xe"}]}
    assert kq.agrees == {"a1": True, "a2": False, "a3": True}


def test_khong_ai_ve_khung_nao_van_la_dong_thuan():
    kq = danh_gia(KHUNG, [nhan(1, {"vat": []}), nhan(2, {"vat": []})])

    assert kq.status == "agreed"
    assert kq.final == {"vat": []}
    assert kq.agrees == {"a1": True, "a2": True}


def test_mot_nhan_khop_khi_khop_moi_cong_cu_da_co_ket_qua():
    ds = [
        nhan(1, {"loai": {"labelIds": ["ngay"]}, "vat": [khung(10, 10)]}),
        nhan(2, {"loai": {"labelIds": ["ngay"]}, "vat": [khung(11, 10)]}),
        nhan(3, {"loai": {"labelIds": ["dem"]}, "vat": [khung(10, 11)]}),    # dung khung, sai loai
        nhan(4, {"loai": {"labelIds": ["ngay"]}, "vat": [khung(200, 200)]}),  # dung loai, sai khung
    ]
    kq = danh_gia(KHUNG_VA_LOAI, ds)

    assert kq.status == "agreed"
    assert kq.agrees == {"a1": True, "a2": True, "a3": False, "a4": False}


def test_cong_cu_tranh_chap_khong_dung_de_cham_nhung_cong_cu_khac_van_cham():
    ds = [
        nhan(1, {"loai": {"labelIds": ["ngay"]}, "vat": [khung(10, 10)]}),
        nhan(2, {"loai": {"labelIds": ["dem"]}, "vat": [khung(10, 10)]}),
    ]
    kq = danh_gia(KHUNG_VA_LOAI, ds)

    assert kq.status == "disputed"
    assert kq.final["loai"] is None
    assert kq.final["vat"] == [{"x": 10, "y": 10, "w": 40, "h": 40, "labelId": "xe"}]
    # Loai tranh chap (khong cham); khung co ket qua va ca hai deu khop.
    assert kq.agrees == {"a1": True, "a2": True}


def test_nguong_va_f1_lay_tu_setting():
    ds = [nhan(1, {"vat": [khung(10, 10)]}), nhan(2, {"vat": [khung(25, 10)]})]   # IoU 25 / 55 = 0.45

    assert danh_gia(KHUNG, ds).status == "disputed"                                             # mac dinh 0.5
    assert danh_gia(KHUNG, ds, tham_so=ThamSoGop(nguong={"bbox": 0.4})).status == "agreed"      # setting ha nguong
    # matchThreshold cua cong cu luon thang setting.
    chat = {"modality": "image", "tools": [{"name": "vat", "kind": "bbox", "classes": ["xe"], "matchThreshold": 0.9}]}
    assert danh_gia(chat, ds, tham_so=ThamSoGop(nguong={"bbox": 0.4})).status == "disputed"
