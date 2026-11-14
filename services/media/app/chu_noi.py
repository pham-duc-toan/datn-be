"""Watermark NOI cho anh / video (FP-02): chu mo goc duoi phai, VD "nguồn: <nguoi xem> - <doanh nghiep> - <domain>".

Chu do project-svc / gate-svc dung san (Crowd.BuildingBlocks.Storage.ChuWatermark) va gui qua token / event —
media-svc chi VE, khong tu ghep noi dung.

Cach ve:
  - Pillow + font TrueType co dau tieng Viet (DejaVu Sans trong image Docker, Arial tren Windows dev). Chu cao
    ~TI_LE_CAO_CHU chieu cao khung (toi thieu CO_CHU_MIN px); chu dai qua RONG_TOI_DA chieu rong thi thu nho lai.
  - Chu trang do mo DO_MO, vien toi mong de doc duoc tren moi nen. Cach mep phai / duoi TI_LE_LE canh ngan.
  - Lop chu (mau + do trong suot) tinh MOT lan cho moi (chu, rong, cao) roi tron vao tung khung bang numpy —
    video vai nghin khung khong ve lai chu moi khung.
  - Anh nho hon CANH_NHO_NHAT px thi khong ve (chu doc khong duoc, che het noi dung).

Ham thuan tren numpy — test khong can MinIO.
"""

import io
import logging
from dataclasses import dataclass
from functools import lru_cache

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from app.config import settings

log = logging.getLogger(__name__)

TI_LE_CAO_CHU = 0.03
CO_CHU_MIN = 10
RONG_TOI_DA = 0.92
TI_LE_LE = 0.02
LE_MIN = 4
DO_MO = 0.55
DO_MO_VIEN = 0.45
CANH_NHO_NHAT = 64

# Thu lan luot: cau hinh (MEDIA_FONT_PATH) → font co dau tieng Viet thuong gap tren Linux / Windows / macOS.
FONT_THUONG_GAP = (
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/usr/share/fonts/dejavu/DejaVuSans.ttf",
    "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
    "C:/Windows/Fonts/arial.ttf",
    "C:/Windows/Fonts/segoeui.ttf",
    "/Library/Fonts/Arial.ttf",
    "/System/Library/Fonts/Supplemental/Arial.ttf",
)


@dataclass(frozen=True)
class LopChu:
    """Lop chu da ve san: dat tai (x, y) tren khung; mau BGR va do trong suot [0, 1] theo tung pixel."""
    x: int
    y: int
    mau: np.ndarray      # (h, w, 3) float32, BGR
    alpha: np.ndarray    # (h, w, 1) float32


@lru_cache
def duong_dan_font() -> str | None:
    ung_vien = [settings().font_path] if settings().font_path else []
    for p in [*ung_vien, *FONT_THUONG_GAP]:
        try:
            ImageFont.truetype(p, 12)
            return p
        except OSError:
            continue
    log.warning("Khong tim thay font TrueType co dau tieng Viet — dung font mac dinh cua Pillow (dau co the loi)")
    return None


@lru_cache(maxsize=32)
def _font(co: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    p = duong_dan_font()
    if p is None:
        return ImageFont.load_default(size=co)
    return ImageFont.truetype(p, co)


def _do_chu(chu: str, co: int) -> tuple[int, int, int, int]:
    font = _font(co)
    vien = max(1, co // 12)
    return ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), chu, font=font, stroke_width=vien)


@lru_cache(maxsize=512)
def lop_chu(chu: str, rong: int, cao: int) -> LopChu | None:
    """Lop chu cho khung rong x cao. None = khong ve (chu rong / khung qua nho)."""
    chu = (chu or "").strip()
    if not chu or rong < CANH_NHO_NHAT or cao < CANH_NHO_NHAT:
        return None

    co = max(CO_CHU_MIN, round(cao * TI_LE_CAO_CHU))
    trai, tren, phai, duoi = _do_chu(chu, co)
    if phai - trai > rong * RONG_TOI_DA and co > CO_CHU_MIN:
        co = max(CO_CHU_MIN, int(co * rong * RONG_TOI_DA / (phai - trai)))
        trai, tren, phai, duoi = _do_chu(chu, co)

    vien = max(1, co // 12)
    w, h = phai - trai, duoi - tren
    anh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(anh).text((-trai, -tren), chu, font=_font(co), fill=(255, 255, 255, 255),
                             stroke_width=vien, stroke_fill=(0, 0, 0, 255))
    rgba = np.asarray(anh, dtype=np.float32) / 255.0
    # Phan than chu trang (do sang cao) mo DO_MO, phan vien toi mo DO_MO_VIEN.
    sang = rgba[:, :, :3].mean(axis=2, keepdims=True)
    alpha = rgba[:, :, 3:4] * (DO_MO_VIEN + (DO_MO - DO_MO_VIEN) * sang)
    mau = (rgba[:, :, 2::-1] * 255.0).astype(np.float32)   # RGB → BGR

    le = max(LE_MIN, round(min(rong, cao) * TI_LE_LE))
    x, y = rong - le - w, cao - le - h
    if x < 0:
        # Chu van dai hon khung (khung rat hep): giu phan ben phai, cat phan dau.
        mau, alpha, x = mau[:, -x:], alpha[:, -x:], 0
    if y < 0:
        mau, alpha, y = mau[-y:], alpha[-y:], 0
    return LopChu(x, y, np.ascontiguousarray(mau), np.ascontiguousarray(alpha.astype(np.float32)))


def phu_len(khung_bgr: np.ndarray, lop: LopChu | None) -> np.ndarray:
    """Tron lop chu vao khung (uint8 BGR, sua TAI CHO va tra lai). lop None = giu nguyen."""
    if lop is None:
        return khung_bgr
    h, w = lop.alpha.shape[:2]
    vung = khung_bgr[lop.y:lop.y + h, lop.x:lop.x + w]
    h, w = vung.shape[:2]
    a = lop.alpha[:h, :w]
    tron = vung.astype(np.float32) * (1.0 - a) + lop.mau[:h, :w] * a
    vung[:] = np.clip(tron + 0.5, 0, 255).astype(np.uint8)
    return khung_bgr


def phu_chu(khung_bgr: np.ndarray, chu: str) -> np.ndarray:
    """Ban sao khung co chu noi."""
    ra = np.ascontiguousarray(khung_bgr).copy()
    return phu_len(ra, lop_chu(chu, ra.shape[1], ra.shape[0]))


def png_toan_khung(chu: str, rong: int, cao: int) -> bytes:
    """PNG trong suot dung kich thuoc khung, chu o goc duoi phai — ffmpeg overlay=0:0 luc cat HLS."""
    rgba = np.zeros((cao, rong, 4), dtype=np.uint8)
    lop = lop_chu(chu, rong, cao)
    if lop is not None:
        h, w = lop.alpha.shape[:2]
        rgba[lop.y:lop.y + h, lop.x:lop.x + w, :3] = np.clip(lop.mau[:, :, ::-1] + 0.5, 0, 255).astype(np.uint8)
        rgba[lop.y:lop.y + h, lop.x:lop.x + w, 3] = np.clip(lop.alpha[:, :, 0] * 255.0 + 0.5, 0, 255).astype(np.uint8)
    buf = io.BytesIO()
    Image.fromarray(rgba, "RGBA").save(buf, format="PNG")
    return buf.getvalue()
