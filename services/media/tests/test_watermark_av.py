"""Watermark am thanh / video: nhung → (ma hoa lai) → giai ra dung nguoi xem."""

import shutil
import subprocess
import uuid
from pathlib import Path

import numpy as np
import pytest

from app import watermark, watermark_av
from app.watermark import LOAI_KHACH, LOAI_TAI_KHOAN, NguoiXem

KHOA = bytes(range(32))
KHOA_KHAC = bytes(range(1, 33))
NGUOI = NguoiXem(LOAI_TAI_KHOAN, uuid.UUID("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"))
co_ffmpeg = pytest.mark.skipif(shutil.which("ffmpeg") is None, reason="can ffmpeg trong PATH")


def _am_mau(giay: float = 6.0, tan_so: int = 44100, kenh: int = 2) -> np.ndarray:
    rng = np.random.default_rng(3)
    t = np.arange(int(giay * tan_so)) / tan_so
    tin = 0.3 * np.sin(2 * np.pi * 440 * t) + 0.1 * np.sin(2 * np.pi * 2500 * t) + 0.02 * rng.standard_normal(len(t))
    return np.stack([tin, 0.8 * tin][:kenh], axis=1).astype(np.float32)


def test_am_nhung_va_giai_dung_nguoi():
    goc = _am_mau()
    moi = watermark_av.nhung_am(goc, 44100, NGUOI, KHOA)
    kq = watermark_av.giai_am(moi, 44100, KHOA)
    assert kq.nguoi == NGUOI
    # Nhieu them rat nho so voi tin hieu (SNR > 40 dB).
    snr = 10 * np.log10(np.mean(goc.astype(np.float64) ** 2) / np.mean((moi.astype(np.float64) - goc) ** 2))
    assert snr > 40


def test_am_khong_watermark_hoac_sai_khoa_khong_ra_ai():
    goc = _am_mau()
    assert watermark_av.giai_am(goc, 44100, KHOA).nguoi is None
    moi = watermark_av.nhung_am(goc, 44100, NGUOI, KHOA)
    assert watermark_av.giai_am(moi, 44100, KHOA_KHAC).nguoi is None


def test_am_chiu_nhieu_va_luong_tu_16_bit():
    moi = watermark_av.nhung_am(_am_mau(), 44100, NguoiXem(LOAI_KHACH, uuid.uuid4()), KHOA)
    rng = np.random.default_rng(9)
    hong = np.round((moi + 0.0005 * rng.standard_normal(moi.shape)) * 32767) / 32767
    kq = watermark_av.giai_am(hong.astype(np.float32), 44100, KHOA)
    assert kq.nguoi is not None and kq.nguoi.la_khach


def test_am_qua_ngan_tra_nguyen():
    ngan = _am_mau(0.2)
    assert np.array_equal(watermark_av.nhung_am(ngan, 44100, NGUOI, KHOA), ngan)
    assert watermark_av.giai_am(ngan, 44100, KHOA).nguoi is None


@co_ffmpeg
def test_file_am_qua_ffmpeg(tmp_path: Path):
    nguon = tmp_path / "goc.mp3"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=600:duration=5", "-f", "lavfi",
                    "-i", "anoisesrc=d=5:a=0.05", "-filter_complex", "amix=inputs=2", "-ac", "2", "-b:a", "192k", str(nguon)], check=True)
    dich = tmp_path / "wm.flac"
    watermark_av.nhung_file_am(nguon, dich, NGUOI, KHOA)
    assert watermark_av.tham_do(dich).co_am
    assert watermark_av.giai_file_am(dich, KHOA).nguoi == NGUOI
    assert watermark_av.giai_file_am(nguon, KHOA).nguoi is None


@co_ffmpeg
def test_file_video_qua_h264(tmp_path: Path):
    nguon = tmp_path / "goc.mp4"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=321x241:rate=10:duration=2",
                    "-f", "lavfi", "-i", "sine=frequency=300:duration=2", "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", str(nguon)], check=True)
    dich = tmp_path / "wm.mp4"
    watermark_av.nhung_file_video(nguon, dich, NGUOI, KHOA, 24)
    tt = watermark_av.tham_do(dich)
    assert tt.co_video and tt.co_am
    kq = watermark_av.giai_file_video(dich, KHOA, watermark.thu_tu_buoc(24))
    assert kq.nguoi == NGUOI and kq.buoc == 24
    assert watermark_av.giai_file_video(nguon, KHOA, [24]).nguoi is None
