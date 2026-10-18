"""Luat chong gian lan (FM-07) — ham thuan, khong can DB."""

import uuid
from datetime import datetime, timezone

from app.contracts import AttemptSubmitted
from app.fraud import chuoi_dai_nhat, chuoi_mot_nhan, dap_an_phan_loai, ip_de_xet, la_qua_nhanh, nguong_qua_nhanh

TAP_NHAN = {
    "modality": "image",
    "tools": [
        {"name": "vat", "kind": "bbox", "classes": ["xe"]},
        {"name": "loai", "kind": "classification", "classes": ["cho", "meo"]},
    ],
}


def test_nguong_lay_max_toi_thieu_va_ti_le_trung_vi():
    assert nguong_qua_nhanh(None, 2.0, 0.25) == 2.0
    assert nguong_qua_nhanh(40.0, 2.0, 0.25) == 10.0
    assert nguong_qua_nhanh(4.0, 2.0, 0.25) == 2.0


def test_qua_nhanh_can_du_cua_so_va_ti_le():
    assert not la_qua_nhanh([0.1] * 4, 5, 2.0, 0.5)          # chua du cua so
    assert la_qua_nhanh([0.1, 0.1, 0.1, 9, 9], 5, 2.0, 0.5)   # 3/5 nhanh
    assert not la_qua_nhanh([0.1, 0.1, 9, 9, 9], 5, 2.0, 0.5)  # 2/5


def test_mot_nhan_lien_tuc_chi_khi_nguoi_khac_chon_da_dang():
    assert chuoi_mot_nhan(["cho"] * 5, 5, 2) == "cho"
    assert chuoi_mot_nhan(["cho"] * 5, 5, 1) is None           # du lieu dong deu that
    assert chuoi_mot_nhan(["cho"] * 4 + ["meo"], 5, 2) is None
    assert chuoi_mot_nhan([None] * 5, 5, 2) is None


def test_ip_noi_bo_bi_bo_qua_khi_bat_tuy_chon():
    assert ip_de_xet("127.0.0.1", True) is None
    assert ip_de_xet("10.88.0.1", True) is None
    assert ip_de_xet("127.0.0.1", False) == "127.0.0.1"
    assert ip_de_xet("203.113.5.9", True) == "203.113.5.9"
    assert ip_de_xet("khong-phai-ip", False) is None


def test_dap_an_lay_cong_cu_phan_loai_dau_tien():
    assert dap_an_phan_loai(TAP_NHAN, {"vat": [], "loai": {"labelIds": ["meo", "cho"]}}) == "cho,meo"
    assert dap_an_phan_loai(TAP_NHAN, {"vat": []}) is None
    assert dap_an_phan_loai({"tools": [{"name": "vat", "kind": "bbox"}]}, {"vat": []}) is None


def test_chuoi_dai_nhat_cung_dap_an_bo_qua_cau_trong():
    assert chuoi_dai_nhat(["cho", "cho", "meo", "meo", "meo", "cho"]) == ("meo", 3)
    assert chuoi_dai_nhat([None, None]) == (None, 0)
    assert chuoi_dai_nhat(["cho", None, "cho"]) == ("cho", 1)
    assert chuoi_dai_nhat([]) == (None, 0)


def test_hop_dong_attempt_submitted_doc_dung_camel_case():
    luc = datetime(2026, 10, 12, 8, 0, tzinfo=timezone.utc)
    p = AttemptSubmitted.model_validate({
        "attemptId": str(uuid.uuid4()), "projectId": str(uuid.uuid4()), "labelerId": str(uuid.uuid4()),
        "answers": [{"sampleId": str(uuid.uuid4()), "storageKey": None, "sampleContent": {"text": "a"},
                     "sampleMetadata": {"length": 1},
                     "labelPayload": {"taskType": "text", "schemaVersion": 1, "data": {"loai": {"labelIds": ["cho"]}}}}],
        "goldTotal": 0, "goldCorrect": 0,
        "startedAt": luc.isoformat(), "submittedAt": luc.isoformat(), "clientIp": "203.113.5.9", "deviceId": None,
    })
    assert p.client_ip == "203.113.5.9"
    assert p.answers[0].label_payload is not None
