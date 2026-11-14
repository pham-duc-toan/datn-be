"""Watermark noi: chu mo goc duoi phai, khong lem ra ngoai khung, chu dai tu thu nho, khung nho khong ve."""

from pathlib import Path

import cv2
import numpy as np
import pytest

from app import chu_noi

ANH = cv2.imread(str(Path(__file__).parent / "data" / "astronaut.jpg"))
CHU = "nguồn: 0192f3a4-7c1e-7a2b-9d10-5e6f7a8b9c0d - Công ty TNHH Văn Phong - gannhan.vn"


def test_co_font_co_dau_tieng_viet():
    if chu_noi.duong_dan_font() is None:
        pytest.skip("may nay khong co font TrueType thuong gap (image Docker cai fonts-dejavu-core)")
    assert Path(chu_noi.duong_dan_font()).exists()


@pytest.mark.parametrize("rong,cao", [(1280, 720), (512, 512), (320, 240), (100, 80)])
def test_lop_nam_goc_duoi_phai_trong_khung(rong, cao):
    lop = chu_noi.lop_chu(CHU, rong, cao)
    assert lop is not None
    h, w = lop.alpha.shape[:2]
    assert lop.x >= 0 and lop.y >= 0
    assert lop.x + w <= rong and lop.y + h <= cao
    # Sat goc duoi phai: phan con lai ben phai / ben duoi chi la le.
    assert rong - (lop.x + w) <= max(chu_noi.LE_MIN, round(min(rong, cao) * chu_noi.TI_LE_LE))
    assert lop.y + h > cao * 0.85
    assert 0 < float(lop.alpha.max()) <= chu_noi.DO_MO + 1e-6


def test_chu_dai_thu_nho_cho_vua_chieu_rong():
    lop = chu_noi.lop_chu(CHU, 640, 360)
    assert lop is not None
    assert lop.alpha.shape[1] <= 640 * chu_noi.RONG_TOI_DA + 2


def test_khung_qua_nho_hoac_chu_rong_thi_khong_ve():
    assert chu_noi.lop_chu(CHU, 50, 50) is None
    assert chu_noi.lop_chu("   ", 640, 480) is None
    nho = ANH[:50, :50].copy()
    assert np.array_equal(chu_noi.phu_chu(nho, CHU), nho)


def test_chi_doi_vung_goc_duoi_phai():
    moi = chu_noi.phu_chu(ANH, CHU)
    assert moi.shape == ANH.shape
    khac = np.any(moi != ANH, axis=2)
    ys, xs = np.nonzero(khac)
    assert len(ys) > 0
    assert ys.min() > ANH.shape[0] * 0.85
    # Anh goc khong bi sua (phu_chu tra ban sao).
    assert not np.array_equal(moi, ANH)


def test_png_toan_khung_dung_kich_thuoc_va_trong_suot_ngoai_chu():
    du = chu_noi.png_toan_khung("nguồn: Công ty A - gannhan.vn", 640, 360)
    anh = cv2.imdecode(np.frombuffer(du, np.uint8), cv2.IMREAD_UNCHANGED)
    assert anh.shape == (360, 640, 4)
    assert anh[:300, :, 3].max() == 0
    assert anh[300:, :, 3].max() > 0
