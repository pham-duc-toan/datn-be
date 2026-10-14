"""Watermark chim (FP-02): nhung THANG nguoi xem vao anh — khong luu gi, giai ma doc nguoc ra nguoi.

Noi dung nhung (168 bit): 1 byte loai (1 = tai khoan, 2 = khach cong link) + UUID 16 byte (userId /
ma phien vuot link) + CRC-32 4 byte. CRC-32 loai ket qua sai (anh khong co watermark, khoa khac, anh
bien doi qua nang): xac suat mot lan giai "trung" ngau nhien ~2^-32.

Cach lam — QIM tren he so DCT (Chen & Wornell 2001):
  - Kenh do sang Y (YCrCb), chia khoi 8x8, DCT truc chuan tung khoi.
  - Moi khoi mang 5 he so tan so trung binh (HE_SO). Moi he so duoc gan MOT bit (168 bit lap lai
    tren nhieu khoi), gan theo hoan vi bi mat sinh tu khoa watermark — khong co khoa thi khong biet
    he so nao mang bit nao.
  - Nhung bit b: lam tron he so ve luoi buoc `buoc`, lech d + b*buoc/2 (d: dither bi mat).
  - Giai ma: moi he so bo phieu MEM (gan luoi 0 hay luoi 1 hon), cong theo bit → chiu duoc nen JPEG,
    nhieu, doi do sang. Buoc la so nguyen trong [BUOC_MIN, BUOC_MAX]: bo giai tinh DCT MOT lan roi thu
    lan luot moi buoc (buoc hien tai truoc) — anh phat truoc khi admin doi do manh van giai duoc.

Gioi han: cat xen (crop) lam lech luoi khoi → khong giai duoc; thu nho thi phai phong lai DUNG kich
thuoc goc truoc khi giai (API nhan width / height); anh nho hon ~130x130 khong du cho → khong nhung.

Ham THUAN tren numpy — test khong can MinIO.
"""

import hashlib
import uuid
import zlib
from dataclasses import dataclass

import cv2
import numpy as np

LOAI_TAI_KHOAN = 1
LOAI_KHACH = 2
SO_BYTE = 1 + 16 + 4
SO_BIT = SO_BYTE * 8
KICH_THUOC_KHOI = 8
# (hang, cot) trong khoi 8x8: tan so trung binh — it lo hon tan so thap, ben voi JPEG hon tan so cao.
HE_SO = ((1, 2), (2, 1), (2, 2), (1, 3), (3, 1))
# Gioi han cua setting media.watermark_strength (catalog) — bo giai thu het khoang nay.
BUOC_MIN = 6
BUOC_MAX = 80


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


def _ma_tran_dct() -> np.ndarray:
    n = KICH_THUOC_KHOI
    d = np.zeros((n, n), dtype=np.float64)
    for k in range(n):
        he = np.sqrt(1.0 / n) if k == 0 else np.sqrt(2.0 / n)
        for i in range(n):
            d[k, i] = he * np.cos(np.pi * (2 * i + 1) * k / (2 * n))
    return d


DCT = _ma_tran_dct()


def _ke_hoach(khoa: bytes, so_vi_tri: int) -> tuple[np.ndarray, np.ndarray]:
    """Bit nao o vi tri nao + dither — tat dinh theo (khoa, so vi tri) nen nhung va giai khop nhau."""
    hat = int.from_bytes(hashlib.sha256(khoa + b"crowd-watermark-plan-v2").digest()[:8], "big")
    rng = np.random.default_rng(hat)
    return rng.permutation(so_vi_tri) % SO_BIT, rng.random(so_vi_tri)


def _khoi_dct(y: np.ndarray) -> tuple[np.ndarray, int, int]:
    h8 = (y.shape[0] // KICH_THUOC_KHOI) * KICH_THUOC_KHOI
    w8 = (y.shape[1] // KICH_THUOC_KHOI) * KICH_THUOC_KHOI
    bh, bw = h8 // KICH_THUOC_KHOI, w8 // KICH_THUOC_KHOI
    khoi = y[:h8, :w8].reshape(bh, KICH_THUOC_KHOI, bw, KICH_THUOC_KHOI).transpose(0, 2, 1, 3)
    return DCT @ khoi @ DCT.T, h8, w8


def du_cho(anh: np.ndarray) -> bool:
    """Anh du khoi de moi bit lap lai it nhat ~8 lan."""
    so_khoi = (anh.shape[0] // KICH_THUOC_KHOI) * (anh.shape[1] // KICH_THUOC_KHOI)
    return so_khoi * len(HE_SO) >= SO_BIT * 8


def nhung(anh_bgr: np.ndarray, nguoi: NguoiXem, khoa: bytes, buoc: int) -> np.ndarray:
    """Tra anh moi (uint8 BGR) mang nguoi xem. Anh qua nho thi tra nguyen anh (khong du cho)."""
    if not du_cho(anh_bgr):
        return anh_bgr.copy()

    ycrcb = cv2.cvtColor(anh_bgr, cv2.COLOR_BGR2YCrCb)
    y = ycrcb[:, :, 0].astype(np.float64)
    he_so, h8, w8 = _khoi_dct(y)
    bh, bw = he_so.shape[0], he_so.shape[1]
    so_khoi = bh * bw
    bit = dong_goi(nguoi)
    bit_cua, dither = _ke_hoach(khoa, so_khoi * len(HE_SO))

    for j, (r, c) in enumerate(HE_SO):
        vi_tri = np.arange(so_khoi) * len(HE_SO) + j
        lech = dither[vi_tri] * buoc + bit[bit_cua[vi_tri]] * (buoc / 2.0)
        cu = he_so[:, :, r, c].reshape(-1)
        he_so[:, :, r, c] = (np.round((cu - lech) / buoc) * buoc + lech).reshape(bh, bw)

    y[:h8, :w8] = (DCT.T @ he_so @ DCT).transpose(0, 2, 1, 3).reshape(h8, w8)
    ycrcb[:, :, 0] = np.clip(np.round(y), 0, 255).astype(np.uint8)
    return cv2.cvtColor(ycrcb, cv2.COLOR_YCrCb2BGR)


@dataclass(frozen=True)
class KetQuaGiai:
    nguoi: NguoiXem | None
    tin_cay: float
    buoc: int | None


def giai(anh_bgr: np.ndarray, khoa: bytes, cac_buoc: list[int]) -> KetQuaGiai:
    """Thu lan luot cac buoc; dung o buoc dau tien ra nguoi xem hop le (CRC dung)."""
    if not du_cho(anh_bgr):
        return KetQuaGiai(None, 0.0, None)

    y = cv2.cvtColor(anh_bgr, cv2.COLOR_BGR2YCrCb)[:, :, 0].astype(np.float64)
    he_so, _, _ = _khoi_dct(y)
    so_khoi = he_so.shape[0] * he_so.shape[1]
    bit_cua, dither = _ke_hoach(khoa, so_khoi * len(HE_SO))
    # Lay he so + vi tri mot lan cho moi buoc thu.
    gt = np.stack([he_so[:, :, r, c].reshape(-1) for r, c in HE_SO], axis=1).reshape(-1)
    vi_tri = (np.arange(so_khoi)[:, None] * len(HE_SO) + np.arange(len(HE_SO))[None, :]).reshape(-1)
    bit_o = bit_cua[vi_tri]
    d_o = dither[vi_tri]

    tot_nhat = 0.0
    for buoc in cac_buoc:
        d = d_o * buoc
        du0 = np.mod(gt - d, buoc)
        cach0 = np.minimum(du0, buoc - du0)
        du1 = np.mod(gt - d - buoc / 2.0, buoc)
        cach1 = np.minimum(du1, buoc - du1)
        phieu = (cach0 - cach1) / (buoc / 2.0)
        diem = np.bincount(bit_o, weights=phieu, minlength=SO_BIT)
        bit = (diem > 0).astype(np.int64)
        phieu_1 = np.bincount(bit_o, weights=(phieu > 0).astype(np.float64), minlength=SO_BIT)
        tong = np.bincount(bit_o, minlength=SO_BIT).astype(np.float64)
        tin_cay = float(np.where(bit == 1, phieu_1, tong - phieu_1).sum() / max(tong.sum(), 1))
        tot_nhat = max(tot_nhat, tin_cay)
        nguoi = mo_goi(bit)
        if nguoi is not None:
            return KetQuaGiai(nguoi, tin_cay, buoc)
    return KetQuaGiai(None, tot_nhat, None)


def thu_tu_buoc(hien_tai: int) -> list[int]:
    """Buoc hien tai truoc, roi moi buoc con lai trong khoang setting cho phep."""
    return [hien_tai] + [b for b in range(BUOC_MIN, BUOC_MAX + 1) if b != hien_tai]


def psnr(goc: np.ndarray, moi: np.ndarray) -> float:
    """Do khac biet (dB) — cang cao cang kho thay; > 38 dB coi nhu khong nhin ra."""
    mse = float(np.mean((goc.astype(np.float64) - moi.astype(np.float64)) ** 2))
    return float("inf") if mse == 0 else 10.0 * np.log10(255.0 ** 2 / mse)
