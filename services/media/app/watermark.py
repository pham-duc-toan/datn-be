"""Watermark CHIM (FP-02) — loi QIM dung cho AM THANH (watermark_av.py): nhung THANG nguoi nghe, khong luu gi.

Anh / video nay mang watermark NOI (chu "nguồn: …" hien tren hinh — chu_noi.py), khong dung watermark chim nua.

Noi dung nhung (168 bit): 1 byte loai (1 = tai khoan, 2 = khach cong link) + UUID 16 byte (userId /
ma phien vuot link) + CRC-32 4 byte. CRC-32 loai ket qua sai (file khong co watermark, khoa khac, bien
doi qua nang): xac suat mot lan giai "trung" ngau nhien ~2^-32.

Cach lam — QIM (Chen & Wornell 2001) tren he so mang bit (he so DCT cua tung khung am thanh):
  - Moi he so duoc gan MOT bit (168 bit lap lai tren nhieu he so), gan theo hoan vi bi mat sinh tu khoa
    watermark — khong co khoa thi khong biet he so nao mang bit nao.
  - Nhung bit b: lam tron he so ve luoi buoc `buoc`, lech d + b*buoc/2 (d: dither bi mat).
  - Giai ma: moi he so bo phieu MEM (gan luoi 0 hay luoi 1 hon), cong theo bit (nhieu kenh cong chung).

Ham THUAN tren numpy — test khong can MinIO.
"""

import hashlib
import uuid
import zlib
from dataclasses import dataclass

import numpy as np

LOAI_TAI_KHOAN = 1
LOAI_KHACH = 2
SO_BYTE = 1 + 16 + 4
SO_BIT = SO_BYTE * 8


@dataclass(frozen=True)
class NguoiXem:
    loai: int
    id: uuid.UUID

    @property
    def la_khach(self) -> bool:
        return self.loai == LOAI_KHACH


def dong_goi(nguoi: NguoiXem) -> np.ndarray:
    if nguoi.loai not in (LOAI_TAI_KHOAN, LOAI_KHACH):
        raise ValueError("loai nguoi xem khong hop le")
    than = bytes([nguoi.loai]) + nguoi.id.bytes
    du = than + zlib.crc32(than).to_bytes(4, "big")
    return np.array([(b >> (7 - i)) & 1 for b in du for i in range(8)], dtype=np.int64)


def mo_goi(bit: np.ndarray) -> NguoiXem | None:
    """None neu CRC sai hoac loai la."""
    du = bytes(int("".join(str(int(x)) for x in bit[i:i + 8]), 2) for i in range(0, SO_BIT, 8))
    than, crc = du[:-4], du[-4:]
    if zlib.crc32(than).to_bytes(4, "big") != crc or than[0] not in (LOAI_TAI_KHOAN, LOAI_KHACH):
        return None
    return NguoiXem(than[0], uuid.UUID(bytes=than[1:]))


def _ke_hoach(khoa: bytes, so_vi_tri: int) -> tuple[np.ndarray, np.ndarray]:
    """Bit nao o vi tri nao + dither — tat dinh theo (khoa, so vi tri) nen nhung va giai khop nhau."""
    hat = int.from_bytes(hashlib.sha256(khoa + b"crowd-watermark-plan-v2").digest()[:8], "big")
    rng = np.random.default_rng(hat)
    return rng.permutation(so_vi_tri) % SO_BIT, rng.random(so_vi_tri)


@dataclass(frozen=True)
class KetQuaGiai:
    nguoi: NguoiXem | None
    tin_cay: float
    buoc: int | None


@dataclass(frozen=True)
class HeSoGiai:
    """He so mang bit cua mot kenh am thanh + bit nao, dither nao o tung he so."""
    gt: np.ndarray
    bit_o: np.ndarray
    d_o: np.ndarray


class PhieuBit:
    """Cong phieu mem tren nhieu nguon (nhieu kenh, doan dau / cuoi am thanh) roi moi chot bit."""

    def __init__(self) -> None:
        self.diem = np.zeros(SO_BIT)
        self.phieu_1 = np.zeros(SO_BIT)
        self.tong = np.zeros(SO_BIT)

    def cong(self, h: HeSoGiai, buoc: float) -> None:
        d = h.d_o * buoc
        du0 = np.mod(h.gt - d, buoc)
        cach0 = np.minimum(du0, buoc - du0)
        du1 = np.mod(h.gt - d - buoc / 2.0, buoc)
        cach1 = np.minimum(du1, buoc - du1)
        phieu = (cach0 - cach1) / (buoc / 2.0)
        self.diem += np.bincount(h.bit_o, weights=phieu, minlength=SO_BIT)
        self.phieu_1 += np.bincount(h.bit_o, weights=(phieu > 0).astype(np.float64), minlength=SO_BIT)
        self.tong += np.bincount(h.bit_o, minlength=SO_BIT).astype(np.float64)

    def chot(self) -> tuple[NguoiXem | None, float]:
        bit = (self.diem > 0).astype(np.int64)
        tin_cay = float(np.where(bit == 1, self.phieu_1, self.tong - self.phieu_1).sum() / max(self.tong.sum(), 1))
        return mo_goi(bit), tin_cay


def giai_nhieu(nguon: list[HeSoGiai], cac_buoc: list[float]) -> KetQuaGiai:
    """Thu lan luot cac buoc, moi buoc cong phieu cua MOI nguon; dung o buoc dau tien ra nguoi xem hop le."""
    if not nguon:
        return KetQuaGiai(None, 0.0, None)
    tot_nhat = 0.0
    for buoc in cac_buoc:
        p = PhieuBit()
        for h in nguon:
            p.cong(h, buoc)
        nguoi, tin_cay = p.chot()
        tot_nhat = max(tot_nhat, tin_cay)
        if nguoi is not None:
            return KetQuaGiai(nguoi, tin_cay, buoc)
    return KetQuaGiai(None, tot_nhat, None)
