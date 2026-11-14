"""Loi QIM cua watermark chim (dung cho am thanh): dong goi nguoi xem 168 bit + CRC, giai bo phieu."""

import uuid

import numpy as np

from app import watermark as wm

NGUOI = wm.NguoiXem(wm.LOAI_TAI_KHOAN, uuid.UUID("01a13200-0000-7000-8000-0000000000ab"))
KHACH = wm.NguoiXem(wm.LOAI_KHACH, uuid.UUID("01a13200-0000-7000-8000-0000000000cd"))
KHOA = bytes(range(32))


def test_dong_goi_va_mo_goi():
    for n in (NGUOI, KHACH):
        assert wm.mo_goi(wm.dong_goi(n)) == n
    bit = wm.dong_goi(NGUOI)
    bit[10] ^= 1
    assert wm.mo_goi(bit) is None


def test_giai_nhieu_khong_co_nguon_thi_khong_ra_ai():
    kq = wm.giai_nhieu([], [0.006])
    assert kq.nguoi is None and kq.tin_cay == 0.0


def test_ke_hoach_tat_dinh_theo_khoa():
    a1, d1 = wm._ke_hoach(KHOA, 1000)
    a2, d2 = wm._ke_hoach(KHOA, 1000)
    b1, _ = wm._ke_hoach(bytes(range(1, 33)), 1000)
    assert np.array_equal(a1, a2) and np.array_equal(d1, d2)
    assert not np.array_equal(a1, b1)
    assert a1.max() < wm.SO_BIT
