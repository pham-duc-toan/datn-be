"""Watermark CHIM cho AM THANH (FP-02) — cung noi dung 168 bit (watermark.py). Anh / video dung chu noi (chu_noi.py, hls.py).

Am thanh — QIM tren he so DCT cua tung khung, CHI DOAN DAU + DOAN CUOI (moi doan ~gap doi so khung toi thieu de
chua du 168 bit, ~1-2 giay o 44,1 kHz):
  - Giai ma file (ffmpeg) ra PCM float [-1, 1], giu tan so lay mau va so kenh.
  - Chia khung KHUNG_AM mau, DCT truc chuan tung khung. He so mang bit: dai TAN_SO (1–5 kHz, nghe ro
    nhung khong phai dai nang luong lon nhat), cach BUOC_BIN he so lay mot — moi khung vai chuc bit.
  - Lam tron ve luoi buoc BUOC_AM, dither bi mat. Moi kenh nhung DOC LAP cung noi dung; giai thi cong phieu.
  - Ra FLAC (nen khong mat) — trinh duyet phat duoc, file tai ve van giai duoc.
Giai: doan dau, roi doan cuoi; khong ra thi ca file (ban nhung moi khung kieu cu).

Gioi han: cat bo doan dau VA doan cuoi, cat dau file lech khung, doi toc do / tan so lay mau, thu am lai qua loa
→ khong giai duoc. Muc tieu: chong lo "tai file ve gui nguyen", khong chong nguoi co y xoa.

Ham thuan tren numpy + goi ffmpeg / ffprobe (phai co trong PATH).
"""

import json
import subprocess
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np

from app import watermark
from app.watermark import HeSoGiai, KetQuaGiai, NguoiXem

KHUNG_AM = 2048
TAN_SO = (1000.0, 5000.0)
BUOC_BIN = 3
# Buoc QIM tren PCM float — nhieu them ~ -65 dBFS tren dai 1–5 kHz, khong nghe thay.
BUOC_AM = 0.006


class LoiMedia(Exception):
    """File khong doc / ghi duoc bang ffmpeg / PyAV, hoac qua ngan de mang watermark."""


# =====================================================================
# ffprobe / ffmpeg
# =====================================================================


@dataclass(frozen=True)
class ThongTinFile:
    co_video: bool
    rong: int
    cao: int
    fps: str
    co_am: bool
    tan_so_mau: int
    so_kenh: int


def tham_do(duong_dan: Path) -> ThongTinFile:
    r = subprocess.run(["ffprobe", "-v", "error", "-print_format", "json", "-show_streams", str(duong_dan)],
                       capture_output=True, timeout=60)
    if r.returncode != 0:
        raise LoiMedia("ffprobe khong doc duoc file")
    luong = json.loads(r.stdout or b"{}").get("streams", [])
    v = next((s for s in luong if s.get("codec_type") == "video" and not s.get("disposition", {}).get("attached_pic")), None)
    a = next((s for s in luong if s.get("codec_type") == "audio"), None)
    fps = (v or {}).get("avg_frame_rate") or (v or {}).get("r_frame_rate") or "25/1"
    if fps in ("0/0", "0/1"):
        fps = (v or {}).get("r_frame_rate") or "25/1"
    return ThongTinFile(
        co_video=v is not None, rong=int((v or {}).get("width") or 0), cao=int((v or {}).get("height") or 0), fps=fps,
        co_am=a is not None, tan_so_mau=int((a or {}).get("sample_rate") or 0), so_kenh=int((a or {}).get("channels") or 0))


def _doc_pcm(duong_dan: Path, tt: ThongTinFile) -> np.ndarray:
    r = subprocess.run(["ffmpeg", "-v", "error", "-i", str(duong_dan), "-map", "0:a:0", "-f", "f32le", "-acodec", "pcm_f32le",
                        "-ar", str(tt.tan_so_mau), "-ac", str(tt.so_kenh), "-"], capture_output=True, timeout=600)
    if r.returncode != 0:
        raise LoiMedia("ffmpeg khong giai ma duoc am thanh")
    return np.frombuffer(r.stdout, dtype=np.float32).reshape(-1, tt.so_kenh)


# =====================================================================
# Am thanh
# =====================================================================


def _bin_am(tan_so_mau: int) -> np.ndarray:
    tren = int(TAN_SO[0] * 2 * KHUNG_AM / tan_so_mau)
    duoi = min(int(TAN_SO[1] * 2 * KHUNG_AM / tan_so_mau), KHUNG_AM - 1)
    return np.arange(tren, duoi, BUOC_BIN)


def du_cho_am(so_mau: int, tan_so_mau: int) -> bool:
    """Moi bit lap lai it nhat ~8 lan (giong anh)."""
    return (so_mau // KHUNG_AM) * len(_bin_am(tan_so_mau)) >= watermark.SO_BIT * 8


def _khoa_am(khoa: bytes) -> bytes:
    # Ke hoach rieng cho am thanh: so vi tri trung voi mot anh nao do cung khong ra cung hoan vi.
    return khoa + b"|am-thanh"


def nhung_am(pcm: np.ndarray, tan_so_mau: int, nguoi: NguoiXem, khoa: bytes, buoc: float = BUOC_AM) -> np.ndarray:
    """pcm [so mau, so kenh] float → ban moi mang nguoi xem. Qua ngan thi tra nguyen (khong du cho)."""
    if not du_cho_am(pcm.shape[0], tan_so_mau):
        return pcm.copy()
    bins = _bin_am(tan_so_mau)
    so_khung = pcm.shape[0] // KHUNG_AM
    bit = watermark.dong_goi(nguoi)
    bit_cua, dither = watermark._ke_hoach(_khoa_am(khoa), so_khung * len(bins))
    lech = (dither * buoc + bit[bit_cua] * (buoc / 2.0)).reshape(so_khung, len(bins))

    ra = pcm.astype(np.float64)
    for k in range(pcm.shape[1]):
        khung = ra[:so_khung * KHUNG_AM, k].reshape(so_khung, KHUNG_AM).copy()
        he_so = cv2.dct(khung, flags=cv2.DCT_ROWS)
        cu = he_so[:, bins]
        he_so[:, bins] = np.round((cu - lech) / buoc) * buoc + lech
        ra[:so_khung * KHUNG_AM, k] = cv2.idct(he_so, flags=cv2.DCT_ROWS).reshape(-1)
    return np.clip(ra, -1.0, 1.0).astype(np.float32)


def he_so_am(pcm: np.ndarray, tan_so_mau: int, khoa: bytes) -> list[HeSoGiai]:
    """Moi kenh mot nguon phieu."""
    if not du_cho_am(pcm.shape[0], tan_so_mau):
        return []
    bins = _bin_am(tan_so_mau)
    so_khung = pcm.shape[0] // KHUNG_AM
    bit_cua, dither = watermark._ke_hoach(_khoa_am(khoa), so_khung * len(bins))
    ra = []
    for k in range(pcm.shape[1]):
        khung = pcm[:so_khung * KHUNG_AM, k].astype(np.float64).reshape(so_khung, KHUNG_AM)
        ra.append(HeSoGiai(cv2.dct(khung, flags=cv2.DCT_ROWS)[:, bins].reshape(-1), bit_cua, dither))
    return ra


def giai_am(pcm: np.ndarray, tan_so_mau: int, khoa: bytes) -> KetQuaGiai:
    return watermark.giai_nhieu(he_so_am(pcm, tan_so_mau, khoa), [BUOC_AM])


def so_mau_doan_am(tan_so_mau: int) -> int:
    """Do dai doan dau / doan cuoi mang watermark: gap doi so khung toi thieu de du 168 bit (44,1 kHz ~ 1 giay)."""
    toi_thieu = -(-watermark.SO_BIT * 8 // max(len(_bin_am(tan_so_mau)), 1))
    return 2 * toi_thieu * KHUNG_AM


def nhung_file_am(nguon: Path, dich: Path, nguoi: NguoiXem, khoa: bytes) -> None:
    """File am thanh bat ky → FLAC; CHI doan dau + doan cuoi (so_mau_doan_am) mang watermark.

    Giai ma → PCM chay qua pipe → ma hoa FLAC: doan dau nhung roi ghi, phan giua chuyen thang (khong xu ly,
    khong nap ca file vao RAM), doan cuoi giu trong hang doi roi nhung khi het file. Doan cuoi nhung nhu mot
    tin hieu rieng (khung tinh tu dau doan) — giai lay dung so_mau_doan_am mau cuoi la khop."""
    tt = tham_do(nguon)
    if not tt.co_am or tt.tan_so_mau <= 0 or tt.so_kenh <= 0:
        raise LoiMedia("file khong co am thanh")
    ch = tt.so_kenh
    doan = so_mau_doan_am(tt.tan_so_mau) * ch * 4   # byte float32

    def nhung(tho: bytes) -> bytes:
        pcm = np.frombuffer(tho[:len(tho) // (ch * 4) * ch * 4], dtype=np.float32).reshape(-1, ch)
        return nhung_am(pcm, tt.tan_so_mau, nguoi, khoa).tobytes()

    giai_ma = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(nguon), "-map", "0:a:0", "-f", "f32le", "-acodec", "pcm_f32le",
                                "-ar", str(tt.tan_so_mau), "-ac", str(ch), "-"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    ma_hoa = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(tt.tan_so_mau), "-ac", str(ch), "-i", "-",
                               "-c:a", "flac", "-sample_fmt", "s16", "-f", "flac", str(dich)],
                              stdin=subprocess.PIPE, stderr=subprocess.DEVNULL)
    try:
        assert giai_ma.stdout is not None and ma_hoa.stdin is not None
        dau = bytearray()
        while len(dau) < doan:
            tho = giai_ma.stdout.read(doan - len(dau))
            if not tho:
                break
            dau += tho
        ma_hoa.stdin.write(nhung(bytes(dau)))
        cuoi = bytearray()
        while True:
            tho = giai_ma.stdout.read(1 << 20)
            if not tho:
                break
            cuoi += tho
            if len(cuoi) > doan:
                thua = len(cuoi) - doan
                ma_hoa.stdin.write(bytes(cuoi[:thua]))
                del cuoi[:thua]
        ma_hoa.stdin.write(nhung(bytes(cuoi)) if len(cuoi) == doan else bytes(cuoi))
        ma_hoa.stdin.close()
        if ma_hoa.wait(timeout=600) != 0 or giai_ma.wait(timeout=60) != 0:
            raise LoiMedia("ffmpeg khong xu ly duoc am thanh")
    finally:
        for p in (giai_ma, ma_hoa):
            if p.poll() is None:
                p.kill()


def giai_file_am(duong_dan: Path, khoa: bytes) -> KetQuaGiai:
    tt = tham_do(duong_dan)
    if not tt.co_am:
        raise LoiMedia("file khong co am thanh")
    pcm = _doc_pcm(duong_dan, tt)
    n = so_mau_doan_am(tt.tan_so_mau)
    # Doan dau, roi doan cuoi; khong ra thi ca file (ban nhung moi khung kieu cu).
    for phan in (pcm[:n], pcm[-n:]):
        kq = giai_am(phan, tt.tan_so_mau, khoa)
        if kq.nguoi is not None:
            return kq
    return giai_am(pcm, tt.tan_so_mau, khoa)
