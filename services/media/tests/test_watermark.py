"""Watermark chim nhung THANG nguoi xem: khong nhin thay, ben voi nen JPEG / thu nho / doi do sang;
khoa khac hoac anh sach thi khong ra ai; doi do manh van giai duoc anh cu."""

import os
import uuid
from pathlib import Path

import cv2
import numpy as np
import pytest

from app import watermark as wm

ANH = cv2.imread(str(Path(__file__).parent / "data" / "astronaut.jpg"))
ANH_NHO = cv2.resize(ANH, (300, 200), interpolation=cv2.INTER_AREA)
KHOA = bytes(range(32))
BUOC = 24
NGUOI = wm.NguoiXem(wm.LOAI_TAI_KHOAN, uuid.UUID("01a13200-0000-7000-8000-0000000000ab"))
KHACH = wm.NguoiXem(wm.LOAI_KHACH, uuid.UUID("01a13200-0000-7000-8000-0000000000cd"))
MOI_BUOC = wm.thu_tu_buoc(BUOC)


def _jpeg(anh, q):
    return cv2.imdecode(cv2.imencode(".jpg", anh, [cv2.IMWRITE_JPEG_QUALITY, q])[1], cv2.IMREAD_COLOR)


def _thu_nho_roi_phong_lai(anh):
    h, w = anh.shape[:2]
    nho = cv2.resize(anh, (w * 6 // 10, h * 6 // 10), interpolation=cv2.INTER_AREA)
    return cv2.resize(nho, (w, h), interpolation=cv2.INTER_CUBIC)


def _sang_hon(anh):
    return np.clip(anh.astype(np.int16) + 15, 0, 255).astype(np.uint8)


def test_dong_goi_va_mo_goi():
    for n in (NGUOI, KHACH):
        assert wm.mo_goi(wm.dong_goi(n)) == n
    bit = wm.dong_goi(NGUOI)
    bit[10] ^= 1
    assert wm.mo_goi(bit) is None


@pytest.mark.parametrize("anh", [ANH, ANH_NHO], ids=["512x512", "300x200"])
def test_khong_nhin_thay(anh):
    assert wm.psnr(anh, wm.nhung(anh, NGUOI, KHOA, BUOC)) > 40


@pytest.mark.parametrize("anh", [ANH, ANH_NHO], ids=["512x512", "300x200"])
@pytest.mark.parametrize("bien_doi", [
    lambda a: a,
    lambda a: _jpeg(a, 90),
    lambda a: _jpeg(a, 70),
    lambda a: _jpeg(a, 50),
    _thu_nho_roi_phong_lai,
    _sang_hon,
], ids=["nguyen", "jpeg90", "jpeg70", "jpeg50", "thu_nho", "sang_hon"])
def test_giai_ra_dung_nguoi_sau_bien_doi(anh, bien_doi):
    kq = wm.giai(bien_doi(wm.nhung(anh, NGUOI, KHOA, BUOC)), KHOA, MOI_BUOC)
    assert kq.nguoi == NGUOI and kq.buoc == BUOC and kq.tin_cay > 0.6


def test_khach_cong_link():
    kq = wm.giai(_jpeg(wm.nhung(ANH, KHACH, KHOA, BUOC), 80), KHOA, MOI_BUOC)
    assert kq.nguoi == KHACH and kq.nguoi.la_khach


def test_doi_do_manh_van_giai_duoc_anh_cu():
    cu = wm.nhung(ANH, NGUOI, KHOA, 40)
    # Setting hien tai la 24, anh duoc nhung luc setting con 40: bo giai thu het khoang.
    kq = wm.giai(_jpeg(cu, 80), KHOA, wm.thu_tu_buoc(24))
    # kq.buoc = buoc dau tien giai ra (khong nhat thiet la 40: bo phieu tren nhieu khoi doi khi giai
    # duoc ca o buoc gan dung); CRC-32 dam bao nguoi xem doc ra la dung.
    assert kq.nguoi == NGUOI


def test_khoa_khac_hoac_anh_sach_thi_khong_ra_ai():
    moi = wm.nhung(ANH, NGUOI, KHOA, BUOC)
    assert wm.giai(moi, os.urandom(32), MOI_BUOC).nguoi is None
    assert wm.giai(ANH, KHOA, MOI_BUOC).nguoi is None


def test_anh_qua_nho_giu_nguyen():
    nho = ANH[:64, :64].copy()
    assert not wm.du_cho(nho)
    assert np.array_equal(wm.nhung(nho, NGUOI, KHOA, BUOC), nho)
    assert wm.giai(nho, KHOA, MOI_BUOC).nguoi is None
