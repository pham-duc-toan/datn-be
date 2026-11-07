"""gop_mau — ket qua giao doanh nghiep (project-svc goi qua gRPC LabelAggregation, kien truc 3.24).

Chuyen tu test C# cu (ResultAggregatorTests, LabelPayloadTests.Gop_theo_tung_cong_cu) khi bo ban gop C#.
"""

from app import gop

TAP_NHAN = {"modality": "image", "tools": [
    {"name": "loai", "kind": "classification", "classes": ["cho", "meo", "ga"], "allowMultiple": True},
    {"name": "vat", "kind": "bbox", "classes": ["mat"], "required": False},
]}
ANH = {"width": 100, "height": 100}


def lop(*ds):
    return {"loai": {"labelIds": list(ds)}}


def test_da_so_tuyet_doi_thang():
    r = gop.gop_mau(TAP_NHAN, [lop("cho"), lop("cho"), lop("meo")], ANH)

    assert r["loai"]["final"] == {"labelIds": ["cho"]}
    assert r["loai"]["votes"] == {"cho": 2, "meo": 1}
    assert r["loai"]["disputed"] is False
    assert r["loai"]["kind"] == "classification"
    # Khong ai lam cong cu khung (tuy chon) → "none".
    assert r["vat"] == {"method": "none", "kind": "bbox"}


def test_hoa_phieu_la_tranh_chap_FB_22():
    r = gop.gop_mau(TAP_NHAN, [lop("cho"), lop("meo")], ANH)

    assert r["loai"]["disputed"] is True
    assert r["loai"]["final"] is None


def test_multi_label_xet_tung_lop_doc_lap():
    r = gop.gop_mau(TAP_NHAN, [lop("cho", "meo"), lop("cho"), lop("cho", "ga")], ANH)

    assert r["loai"]["final"] == {"labelIds": ["cho"]}


def test_khung_duoc_gop_theo_cum():
    a = {"loai": {"labelIds": ["cho"]}, "vat": [{"labelId": "mat", "x": 1, "y": 1, "w": 10, "h": 10},
                                               {"labelId": "mat", "x": 50, "y": 50, "w": 10, "h": 10}]}
    b = lop("cho")   # khong lam cong cu khung (tuy chon) — khong tinh vao so nguoi cua cong cu do
    c = {"loai": {"labelIds": ["cho"]}, "vat": [{"labelId": "mat", "x": 2, "y": 1, "w": 10, "h": 10},
                                               {"labelId": "mat", "x": 50, "y": 51, "w": 10, "h": 10},
                                               {"labelId": "mat", "x": 80, "y": 80, "w": 5, "h": 5}]}

    r = gop.gop_mau(TAP_NHAN, [a, b, c], ANH)

    # Hai khung ca hai nguoi cung ve duoc giu (trung vi toa do); khung thu ba chi mot nguoi ve bi bo.
    assert r["vat"]["method"] == "cluster"
    assert len(r["vat"]["final"]) == 2
    assert r["vat"]["final"][0]["x"] == 1.5
    assert r["vat"]["final"][1]["y"] == 50.5
    assert r["vat"]["supports"] == [2, 2]
    # Nguoi ve thua mot khung: F1 = 2 x 2 / (3 + 2) = 0.8 — van tinh la khop.
    assert r["vat"]["agrees"] == [True, True]
    assert r["vat"]["disputed"] is False


def test_hai_nguoi_ve_khung_o_hai_cho_khac_nhau_la_tranh_chap():
    a = {"loai": {"labelIds": ["cho"]}, "vat": [{"labelId": "mat", "x": 1, "y": 1, "w": 10, "h": 10}]}
    b = {"loai": {"labelIds": ["cho"]}, "vat": [{"labelId": "mat", "x": 60, "y": 60, "w": 10, "h": 10}]}

    r = gop.gop_mau(TAP_NHAN, [a, b], ANH)

    assert r["vat"]["disputed"] is True
    assert r["vat"]["final"] is None
    # Cong cu phan loai cua cung mau van co ket qua.
    assert r["loai"]["final"] == {"labelIds": ["cho"]}


def test_khong_ai_ve_khung_nao_la_danh_sach_rong_khong_tranh_chap():
    r = gop.gop_mau(TAP_NHAN, [{**lop("cho"), "vat": []}, {**lop("cho"), "vat": []}], ANH)

    assert r["vat"]["final"] == []
    assert r["vat"]["disputed"] is False


def test_cap_hoa_phieu_va_cau_tra_loi_mo_khong_gop():
    tap = {"modality": "pair", "tools": [{"name": "tot_hon", "kind": "pairwise"},
                                         {"name": "mo_ta", "kind": "freeText", "required": False}]}
    r = gop.gop_mau(tap, [{"tot_hon": {"choice": "a"}, "mo_ta": {"text": "x"}}, {"tot_hon": {"choice": "b"}}])

    assert r["tot_hon"]["disputed"] is True
    assert r["tot_hon"]["votes"] == {"a": 1, "b": 1}
    assert r["mo_ta"] == {"method": "none", "kind": "freeText"}


def test_nguong_theo_loai_cong_cu():
    hai = [{"vat": [{"labelId": "mat", "x": 10, "y": 10, "w": 40, "h": 40}]},
           {"vat": [{"labelId": "mat", "x": 25, "y": 10, "w": 40, "h": 40}]}]   # IoU 25 / 55 = 0.45

    assert gop.gop_mau(TAP_NHAN, hai, ANH)["vat"]["disputed"] is True                      # mac dinh 0.5
    assert gop.gop_mau(TAP_NHAN, hai, ANH, {"bbox": 0.4})["vat"]["disputed"] is False      # setting ha nguong
