"""Watermark chim cho AM THANH va VIDEO (FP-02) — cung noi dung 168 bit voi anh (watermark.py).

Am thanh — QIM tren he so DCT cua tung khung:
  - Giai ma file (ffmpeg) ra PCM float [-1, 1], giu tan so lay mau va so kenh.
  - Chia khung KHUNG_AM mau, DCT truc chuan tung khung. He so mang bit: dai TAN_SO (1–5 kHz, nghe ro
    nhung khong phai dai nang luong lon nhat), cach BUOC_BIN he so lay mot — moi khung vai chuc bit.
  - Nhung / giai giong anh (lam tron ve luoi buoc BUOC_AM, dither bi mat). Moi kenh nhung DOC LAP cung
    noi dung; giai thi cong phieu cua moi kenh.
  - Ra FLAC (nen khong mat) — trinh duyet phat duoc, file tai ve van giai duoc.

Video — moi khung hinh nhung bang watermark anh (kenh do sang, DCT 8x8, do manh = media.watermark_strength),
ma hoa lai H.264 chat luong cao (CRF thap) + faststart de tua duoc. Giai: lay mau toi da SO_KHUNG_GIAI
khung, cong phieu cua moi khung — nen video lam hong tung khung van doc duoc tong the.

Gioi han (ghi ro nhu anh): cat dau file am thanh (lech khung), doi toc do / tan so lay mau, thu am lai
qua loa, cat xen / doi kich thuoc video → khong giai duoc.

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
SO_KHUNG_GIAI = 48
CRF_VIDEO = "18"


class LoiMedia(Exception):
    """File khong doc / ghi duoc bang ffmpeg, hoac qua ngan / qua nho de mang watermark."""


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


def nhung_file_am(nguon: Path, dich: Path, nguoi: NguoiXem, khoa: bytes) -> None:
    """File am thanh bat ky → FLAC mang watermark."""
    tt = tham_do(nguon)
    if not tt.co_am or tt.tan_so_mau <= 0 or tt.so_kenh <= 0:
        raise LoiMedia("file khong co am thanh")
    pcm = nhung_am(_doc_pcm(nguon, tt), tt.tan_so_mau, nguoi, khoa)
    r = subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(tt.tan_so_mau), "-ac", str(tt.so_kenh), "-i", "-",
                        "-c:a", "flac", "-sample_fmt", "s16", "-f", "flac", str(dich)],
                       input=pcm.tobytes(), capture_output=True, timeout=600)
    if r.returncode != 0:
        raise LoiMedia("ffmpeg khong ma hoa duoc FLAC")


# =====================================================================
# Video
# =====================================================================


def nhung_file_video(nguon: Path, dich: Path, nguoi: NguoiXem, khoa: bytes, buoc: int) -> None:
    """Moi khung hinh nhung watermark anh → H.264 mp4 (giu am thanh, ma hoa lai AAC)."""
    tt = tham_do(nguon)
    if not tt.co_video or tt.rong <= 0 or tt.cao <= 0:
        raise LoiMedia("file khong co hinh")
    rong, cao = tt.rong, tt.cao
    kich = rong * cao * 3
    giai_ma = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(nguon), "-map", "0:v:0", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    ma_hoa = subprocess.Popen(
        ["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{rong}x{cao}", "-r", tt.fps, "-i", "-",
         "-i", str(nguon), "-map", "0:v:0", "-map", "1:a:0?",
         # yuv420p can canh chan: dem them 1 pixel ben phai / duoi (luoi khoi 8x8 neo goc tren trai nen khong lech).
         "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "veryfast", "-crf", CRF_VIDEO,
         "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", "-f", "mp4", str(dich)],
        stdin=subprocess.PIPE, stderr=subprocess.DEVNULL)
    try:
        assert giai_ma.stdout is not None and ma_hoa.stdin is not None
        while True:
            tho = giai_ma.stdout.read(kich)
            if len(tho) < kich:
                break
            khung = np.frombuffer(tho, np.uint8).reshape(cao, rong, 3)
            ma_hoa.stdin.write(watermark.nhung(khung, nguoi, khoa, buoc).tobytes())
        ma_hoa.stdin.close()
        if ma_hoa.wait(timeout=600) != 0 or giai_ma.wait(timeout=60) != 0:
            raise LoiMedia("ffmpeg khong xu ly duoc video")
    finally:
        for p in (giai_ma, ma_hoa):
            if p.poll() is None:
                p.kill()


def giai_file_video(duong_dan: Path, khoa: bytes, cac_buoc: list[int], rong: int | None = None, cao: int | None = None) -> KetQuaGiai:
    """Lay mau toi da SO_KHUNG_GIAI khung, cong phieu. rong / cao: kich thuoc GOC neu video ro ri bi thu nho."""
    tt = tham_do(duong_dan)
    if not tt.co_video:
        raise LoiMedia("file khong co hinh")
    r_ra, c_ra = (rong, cao) if rong and cao else (tt.rong, tt.cao)
    # Lay deu: moi khung thu n (n theo so khung uoc luong); ffmpeg tu doi kich thuoc neu can.
    so_khung = _dem_khung(duong_dan)
    nhay = max(1, so_khung // SO_KHUNG_GIAI)
    r = subprocess.run(["ffmpeg", "-v", "error", "-i", str(duong_dan), "-map", "0:v:0",
                        "-vf", f"select=not(mod(n\\,{nhay})),scale={r_ra}:{c_ra}:flags=bicubic", "-fps_mode", "passthrough",
                        "-frames:v", str(SO_KHUNG_GIAI), "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                       capture_output=True, timeout=600)
    if r.returncode != 0:
        raise LoiMedia("ffmpeg khong giai ma duoc video")
    kich = r_ra * c_ra * 3
    nguon = []
    for i in range(len(r.stdout) // kich):
        h = watermark.he_so_giai(np.frombuffer(r.stdout[i * kich:(i + 1) * kich], np.uint8).reshape(c_ra, r_ra, 3), khoa)
        if h is not None:
            nguon.append(h)
    return watermark.giai_nhieu(nguon, cac_buoc)


def _dem_khung(duong_dan: Path) -> int:
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_packets", "-show_entries",
                        "stream=nb_read_packets", "-of", "csv=p=0", str(duong_dan)], capture_output=True, text=True, timeout=120)
    try:
        return int(r.stdout.strip().split(",")[0])
    except ValueError:
        return SO_KHUNG_GIAI


def giai_file_am(duong_dan: Path, khoa: bytes) -> KetQuaGiai:
    tt = tham_do(duong_dan)
    if not tt.co_am:
        raise LoiMedia("file khong co am thanh")
    return giai_am(_doc_pcm(duong_dan, tt), tt.tan_so_mau, khoa)
