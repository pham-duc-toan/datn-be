"""Watermark chim cho AM THANH va VIDEO (FP-02) — cung noi dung 168 bit voi anh (watermark.py).

Am thanh — QIM tren he so DCT cua tung khung:
  - Giai ma file (ffmpeg) ra PCM float [-1, 1], giu tan so lay mau va so kenh.
  - Chia khung KHUNG_AM mau, DCT truc chuan tung khung. He so mang bit: dai TAN_SO (1–5 kHz, nghe ro
    nhung khong phai dai nang luong lon nhat), cach BUOC_BIN he so lay mot — moi khung vai chuc bit.
  - Nhung / giai giong anh (lam tron ve luoi buoc BUOC_AM, dither bi mat). Moi kenh nhung DOC LAP cung
    noi dung; giai thi cong phieu cua moi kenh.
  - Ra FLAC (nen khong mat) — trinh duyet phat duoc, file tai ve van giai duoc.

Video — CHI NHUNG KHUNG DAU TIEN + KHUNG CUOI CUNG (nhanh, khong phu thuoc do dai video):
  1. chuan_hoa_video: MOT lan moi file goc (dung chung moi nguoi xem) ma hoa lai H.264 voi GOP CO DINH
     GOP_GIAY, khong B-frame, kich thuoc chan — de cat / ghep theo keyframe ma khong lech khung.
  2. nhung_video_dau_cuoi (moi nguoi xem): cat ban chuan thanh GOP dau | phan giua | GOP cuoi bang
     `-c copy`; chi GOP dau va GOP cuoi duoc giai ma, nhung watermark anh vao SO_KHUNG_NHUNG khung (dau GOP
     dau, cuoi GOP cuoi) roi ma hoa lai CUNG thong so; phan giua giu nguyen → ghep lai (concat, copy).
  Gioi han CO CHU DICH (chon de doi lay toc do): cat bo vai giay dau va cuoi, hoac quay man hinh tu giua
  video, la mat watermark — chong lo "tai file ve gui nguyen", khong chong nguoi co y xoa.
  Moi video deu vay, khong phan biet kich thuoc / do dai (video chi 1–2 khung thi ca hai khung deu la dau / cuoi).
Giai: doc SO_KHUNG_NHUNG khung dau va cuoi, cong phieu; khong ra thi lay mau deu ca video (ban nhung moi
khung kieu cu van giai duoc).

Gioi han (ghi ro nhu anh): cat dau file am thanh (lech khung), doi toc do / tan so lay mau, thu am lai
qua loa, cat xen / doi kich thuoc video → khong giai duoc.

Ham thuan tren numpy + goi ffmpeg / ffprobe (phai co trong PATH).
"""

import json
import subprocess
from collections import deque
from concurrent.futures import ThreadPoolExecutor
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
# Dau + cuoi: do dai mot GOP cua ban chuan, so khung mang watermark o moi dau.
GOP_GIAY = 1.0
SO_KHUNG_NHUNG = 1   # chi khung DAU TIEN va khung CUOI CUNG


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


# =====================================================================
# VIDEO: chuan hoa mot lan + nhung dau / cuoi cho tung nguoi xem
# =====================================================================


def _fps(fps: str) -> float:
    try:
        tu, _, mau = fps.partition("/")
        f = float(tu) / float(mau or 1)
    except (ValueError, ZeroDivisionError):
        f = 25.0
    return f if f > 0 else 25.0


def _so_khung_gop(fps: str) -> int:
    return max(1, round(_fps(fps) * GOP_GIAY))


def _thoi_luong(duong_dan: Path) -> float:
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(duong_dan)],
                       capture_output=True, text=True, timeout=60)
    try:
        return float(r.stdout.strip())
    except ValueError:
        return 0.0


def _x264(g: int) -> list[str]:
    """Thong so ma hoa DUNG CHUNG cho ban chuan va doan nhung lai — khac la ghep bi giat hinh."""
    return ["-c:v", "libx264", "-preset", "veryfast", "-crf", CRF_VIDEO, "-profile:v", "high", "-pix_fmt", "yuv420p",
            "-g", str(g), "-keyint_min", str(g), "-sc_threshold", "0", "-bf", "0", "-x264-params", "open-gop=0"]


def _chay(lenh: list[str], timeout: int, loi: str) -> None:
    r = subprocess.run(lenh, capture_output=True, timeout=timeout)
    if r.returncode != 0:
        raise LoiMedia(loi + ": " + r.stderr.decode(errors="replace")[-300:])


def chuan_hoa_video(nguon: Path, dich: Path) -> None:
    """Ban chuan dung chung moi nguoi xem: H.264 khong B-frame, AAC, faststart. Keyframe: GOP_GIAY mot lan, va
    EP them ngay sau SO_KHUNG_NHUNG khung dau + ngay truoc SO_KHUNG_NHUNG khung cuoi — doan dau / cuoi cat ra chi
    dung SO_KHUNG_NHUNG khung (moi nguoi xem chi giai ma / ma hoa lai 2 x SO_KHUNG_NHUNG khung)."""
    tt = tham_do(nguon)
    if not tt.co_video or tt.rong <= 0 or tt.cao <= 0:
        raise LoiMedia("file khong co hinh")
    f = _fps(tt.fps)
    dai = _thoi_luong(nguon)
    ep = ["0"]
    if dai > 2 * SO_KHUNG_NHUNG / f:
        ep += [f"{SO_KHUNG_NHUNG / f:.6f}", f"{dai - SO_KHUNG_NHUNG / f:.6f}"]
    _chay(["ffmpeg", "-v", "error", "-y", "-i", str(nguon), "-map", "0:v:0", "-map", "0:a:0?",
           "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", *_x264(_so_khung_gop(tt.fps)), "-force_key_frames", ",".join(ep),
           "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", "-f", "mp4", str(dich)], 3600, "khong chuan hoa duoc video")


def _moc_keyframe(duong_dan: Path) -> list[float]:
    """Thoi diem cac keyframe — doc CO cua packet, khong giai ma (video 2 phut 1080p: 0,3 s thay vi 2 s)."""
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time,flags",
                        "-of", "csv=p=0", str(duong_dan)], capture_output=True, text=True, timeout=300)
    moc = []
    for dong in r.stdout.split():
        phan = dong.strip().split(",")
        if len(phan) >= 2 and "K" in phan[1]:
            try:
                moc.append(float(phan[0]))
            except ValueError:
                pass
    return sorted(set(moc))


def _nhung_doan(nguon: Path, dich: Path, nguoi: NguoiXem, khoa: bytes, buoc: int, g: int, dau: bool) -> None:
    """Ma hoa lai MOT doan (GOP dau / cuoi): SO_KHUNG_NHUNG khung dau (hoac cuoi) mang watermark, am thanh copy."""
    tt = tham_do(nguon)
    rong, cao = tt.rong, tt.cao
    kich = rong * cao * 3
    giai_ma = subprocess.Popen(["ffmpeg", "-v", "error", "-i", str(nguon), "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    ma_hoa = subprocess.Popen(
        ["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{rong}x{cao}", "-r", tt.fps, "-i", "-",
         "-i", str(nguon), "-map", "0:v:0", "-map", "1:a:0?", *_x264(g), "-c:a", "copy",
         # TS khong tre mux (mac dinh 0,7 s) — doan 1 khung moi tinh dung thoi luong khi ghep.
         "-muxdelay", "0", "-muxpreload", "0", "-f", "mpegts", str(dich)],
        stdin=subprocess.PIPE, stderr=subprocess.DEVNULL)
    try:
        assert giai_ma.stdout is not None and ma_hoa.stdin is not None

        def nhung(tho: bytes) -> bytes:
            return watermark.nhung(np.frombuffer(tho, np.uint8).reshape(cao, rong, 3), nguoi, khoa, buoc).tobytes()

        def nhung_ca_lo(lo: list[bytes]) -> None:
            # Cac khung doc lap: nhung song song (numpy nha GIL khi nhan ma tran) roi ghi DUNG thu tu.
            with ThreadPoolExecutor(max_workers=len(lo) or 1) as nhom:
                for tho in nhom.map(nhung, lo):
                    ma_hoa.stdin.write(tho)

        # Giu SO_KHUNG_NHUNG khung: GOP dau → SO_KHUNG_NHUNG khung dau tien; GOP cuoi → khong biet truoc so
        # khung, khung bi day ra khoi hang doi ghi nguyen, het doan thi cac khung con lai chinh la cac khung cuoi.
        cho: deque[bytes] = deque()
        da_nhung_dau = False
        while True:
            tho = giai_ma.stdout.read(kich)
            if len(tho) < kich:
                break
            if dau and da_nhung_dau:
                ma_hoa.stdin.write(tho)
                continue
            cho.append(tho)
            if dau and len(cho) == SO_KHUNG_NHUNG:
                nhung_ca_lo(list(cho))
                cho.clear()
                da_nhung_dau = True
            elif not dau and len(cho) > SO_KHUNG_NHUNG:
                ma_hoa.stdin.write(cho.popleft())
        nhung_ca_lo(list(cho))
        ma_hoa.stdin.close()
        if ma_hoa.wait(timeout=600) != 0 or giai_ma.wait(timeout=60) != 0:
            raise LoiMedia("ffmpeg khong xu ly duoc doan video")
    finally:
        for p in (giai_ma, ma_hoa):
            if p.poll() is None:
                p.kill()


def nhung_video_dau_cuoi(chuan: Path, dich: Path, nguoi: NguoiXem, khoa: bytes, buoc: int) -> None:
    """Tu BAN CHUAN (chuan_hoa_video): chi ma hoa lai GOP dau + GOP cuoi, phan giua copy nguyen."""
    tt = tham_do(chuan)
    g = _so_khung_gop(tt.fps)
    moc = _moc_keyframe(chuan)
    if len(moc) < 3:
        # Video 1–2 khung: moi khung deu la khung dau / cuoi.
        nhung_file_video(chuan, dich, nguoi, khoa, buoc)
        return

    thu_muc = dich.parent / f".{dich.stem}-doan"
    thu_muc.mkdir(parents=True, exist_ok=True)
    try:
        _chay(["ffmpeg", "-v", "error", "-y", "-i", str(chuan), "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy",
               "-f", "segment", "-segment_times", f"{moc[1]:.6f},{moc[-1]:.6f}", "-segment_format", "mpegts",
               "-muxdelay", "0", "-muxpreload", "0", "-reset_timestamps", "1", str(thu_muc / "p%d.ts")],
              600, "khong cat duoc video")
        dau, giua, cuoi = (thu_muc / f"p{i}.ts" for i in range(3))
        if not (dau.exists() and giua.exists() and cuoi.exists()):
            raise LoiMedia("cat video khong ra du ba doan")
        # GOP dau va GOP cuoi doc lap → song song.
        with ThreadPoolExecutor(max_workers=2) as nhom:
            viec = [nhom.submit(_nhung_doan, dau, thu_muc / "dau.ts", nguoi, khoa, buoc, g, True),
                    nhom.submit(_nhung_doan, cuoi, thu_muc / "cuoi.ts", nguoi, khoa, buoc, g, False)]
            for v in viec:
                v.result()
        ds = thu_muc / "ghep.txt"
        # Ghi RO thoi luong tung doan: doan chi 1 khung thi bo ghep khong tu suy ra dung (sinh khung thua, de khung nhung).
        tong = _thoi_luong(chuan)
        mot_khung = 1 / _fps(tt.fps)
        doan = [(thu_muc / "dau.ts", moc[1]), (giua, moc[-1] - moc[1]), (thu_muc / "cuoi.ts", max(tong - moc[-1], mot_khung))]
        ds.write_text("".join(f"file '{p.as_posix()}'\nduration {d:.6f}\n" for p, d in doan))
        _chay(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(ds), "-map", "0:v:0", "-map", "0:a:0?",
               "-c", "copy", "-movflags", "+faststart", "-f", "mp4", str(dich)], 600, "khong ghep duoc video")
    finally:
        for f in thu_muc.glob("*"):
            f.unlink(missing_ok=True)
        thu_muc.rmdir()


def _khung_giai(duong_dan: Path, r_ra: int, c_ra: int, khoa: bytes, truoc: list[str], sau: list[str]) -> list[HeSoGiai]:
    r = subprocess.run(["ffmpeg", "-v", "error", *truoc, "-i", str(duong_dan), "-map", "0:v:0",
                        "-vf", f"scale={r_ra}:{c_ra}:flags=bicubic", "-fps_mode", "passthrough", *sau,
                        "-f", "rawvideo", "-pix_fmt", "bgr24", "-"], capture_output=True, timeout=600)
    if r.returncode != 0:
        return []
    kich = r_ra * c_ra * 3
    ds = []
    for i in range(len(r.stdout) // kich):
        h = watermark.he_so_giai(np.frombuffer(r.stdout[i * kich:(i + 1) * kich], np.uint8).reshape(c_ra, r_ra, 3), khoa)
        if h is not None:
            ds.append(h)
    return ds


def giai_file_video(duong_dan: Path, khoa: bytes, cac_buoc: list[int], rong: int | None = None, cao: int | None = None) -> KetQuaGiai:
    """Thu SO_KHUNG_NHUNG khung dau, roi khung cuoi; khong ra thi lay mau deu ca video (ban nhung moi khung).
    rong / cao: kich thuoc GOC neu video ro ri bi thu nho."""
    tt = tham_do(duong_dan)
    if not tt.co_video:
        raise LoiMedia("file khong co hinh")
    r_ra, c_ra = (rong, cao) if rong and cao else (tt.rong, tt.cao)
    dau = _khung_giai(duong_dan, r_ra, c_ra, khoa, [], ["-frames:v", str(SO_KHUNG_NHUNG)])
    kq = watermark.giai_nhieu(dau, cac_buoc)
    if kq.nguoi is not None:
        return kq
    # Khung cuoi: giai ma vai giay cuoi, giu SO_KHUNG_NHUNG khung sau cung.
    cuoi = _khung_giai(duong_dan, r_ra, c_ra, khoa, ["-sseof", f"-{GOP_GIAY * 3 + 1:.1f}"], [])[-SO_KHUNG_NHUNG:]
    kq = watermark.giai_nhieu(cuoi, cac_buoc)
    if kq.nguoi is not None:
        return kq
    return _giai_lay_mau_deu(duong_dan, r_ra, c_ra, khoa, cac_buoc)


def _giai_lay_mau_deu(duong_dan: Path, r_ra: int, c_ra: int, khoa: bytes, cac_buoc: list[int]) -> KetQuaGiai:
    """Ban nhung moi khung (kieu cu, hoac video qua ngan): lay mau toi da SO_KHUNG_GIAI khung, cong phieu."""
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
    pcm = _doc_pcm(duong_dan, tt)
    n = so_mau_doan_am(tt.tan_so_mau)
    # Doan dau, roi doan cuoi; khong ra thi ca file (ban nhung moi khung kieu cu).
    for phan in (pcm[:n], pcm[-n:]):
        kq = giai_am(phan, tt.tan_so_mau, khoa)
        if kq.nguoi is not None:
            return kq
    return giai_am(pcm, tt.tan_so_mau, khoa)
