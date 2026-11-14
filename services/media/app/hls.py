"""HLS cho video co watermark noi (FP-02): moi DOAN MAU mot danh sach phat, phan giua dung chung moi nguoi xem.

Luc nap (ban_chuan.py, MOT lan moi doan mau [a, b] cua file):
  ffmpeg cat [a, b] cua file goc, ma hoa lai H.264 + AAC, chia mang mpegts tai cac moc:
      [0, DAU_CUOI]  [DAU_CUOI, +GIUA] ... [dai - DAU_CUOI, dai]
  - mang DAU va mang CUOI de SACH (khong chu): luc phat moi nguoi xem nhan ban rieng co chu
    "nguồn: <nguoi xem> - <doanh nghiep> - <domain>" (tao_mang_rieng — PyAV, chi vai chuc khung);
  - cac mang GIUA in chu dung chung "nguồn: <doanh nghiep> - <domain>" (ffmpeg overlay mot lan) — moi nguoi xem
    tai cung file, khong xu ly gi them;
  - doan mau ngan hon NGAN_NHAT_DE_CHIA giay: MOT mang, ca mang la mang rieng.
  Luu tren MinIO canh file goc: "<projectId>/_wm/hls-v1/<phan con lai>/<a ms>-<b ms>/c00000.ts…" + index.json
  (xoa du an xoa theo tien to "<projectId>/" la xoa luon).

Luc phat (views.py): danh sach phat VOD rieng cho token — mang dau / cuoi tro "p/dau.ts", "p/cuoi.ts" (rieng nguoi
xem, co #EXT-X-DISCONTINUITY vi ma hoa khac lan), mang giua tro "c/00001.ts"… (dung chung). Moc 0 = dau mau.
"""

import json
import logging
import math
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

import av

from app import chu_noi
from app.storage import KhongCoFile, kho
from app.watermark_av import LoiMedia, tham_do

log = logging.getLogger(__name__)

PHIEN_BAN = "hls-v1"
DAU_CUOI = 2.0          # giay — mang dau / cuoi mang chu rieng tung nguoi xem
GIUA = 6.0              # giay — mang giua dung chung
NGAN_NHAT_DE_CHIA = 2 * DAU_CUOI + 1.0
MANG_GIUA_NGAN_NHAT = 1.0
CRF = "21"
CRF_RIENG = "20"
LOAI_MANG = "video/mp2t"


@dataclass(frozen=True)
class Mang:
    ten: str          # "c00003.ts"
    dai: float        # giay


@dataclass(frozen=True)
class ChiMuc:
    mang: list[Mang]

    @property
    def mot_mang(self) -> bool:
        return len(self.mang) == 1

    def la_mang_rieng(self, i: int) -> bool:
        return i == 0 or i == len(self.mang) - 1


# =====================================================================
# Khoa MinIO
# =====================================================================


def _ms(giay: float) -> int:
    return int(round(giay * 1000))


def thu_muc(storage_key: str, a: float, b: float) -> str:
    """Thu muc MinIO cua mot doan mau — cung tien to du an voi file goc."""
    du_an, _, con_lai = storage_key.partition("/")
    goc = f"_wm/{PHIEN_BAN}/{storage_key}" if not con_lai else f"{du_an}/_wm/{PHIEN_BAN}/{con_lai}"
    return f"{goc}/{_ms(a)}-{_ms(b)}"


def khoa_chi_muc(storage_key: str, a: float, b: float) -> str:
    return thu_muc(storage_key, a, b) + "/index.json"


def khoa_mang(storage_key: str, a: float, b: float, ten: str) -> str:
    return thu_muc(storage_key, a, b) + "/" + ten


def ten_mang(i: int) -> str:
    return f"c{i:05d}.ts"


# =====================================================================
# Cat (luc nap)
# =====================================================================


def moc_cat(dai: float) -> list[float]:
    """Moc chia mang trong doan dai `dai` giay (tinh tu dau doan). Rong = mot mang."""
    if dai < NGAN_NHAT_DE_CHIA:
        return []
    moc = [DAU_CUOI]
    t = DAU_CUOI + GIUA
    while t < dai - DAU_CUOI - MANG_GIUA_NGAN_NHAT:
        moc.append(t)
        t += GIUA
    moc.append(round(dai - DAU_CUOI, 3))
    return moc


def _so(x: float) -> str:
    return f"{x:.3f}"


def _cat_mot_doan(goc: Path, a: float, b: float, chu_chung: str, ra: Path) -> ChiMuc:
    """ffmpeg cat [a, b] cua `goc` thanh cac mang trong thu muc `ra`. Tra chi muc (ten + do dai thuc)."""
    tt = tham_do(goc)
    if not tt.co_video or tt.rong <= 0 or tt.cao <= 0:
        raise LoiMedia("file khong co hinh")
    dai = b - a
    if dai <= 0:
        raise LoiMedia("doan mau rong")
    moc = moc_cat(dai)
    rong, cao = tt.rong + tt.rong % 2, tt.cao + tt.cao % 2

    lenh = ["ffmpeg", "-v", "error", "-y", "-ss", _so(a), "-t", _so(dai), "-i", str(goc)]
    if moc:
        lop = ra / "lop.png"
        lop.write_bytes(chu_noi.png_toan_khung(chu_chung, rong, cao))
        lenh += ["-i", str(lop), "-filter_complex",
                 f"[0:v]pad=ceil(iw/2)*2:ceil(ih/2)*2[v0];[v0][1:v]overlay=0:0:enable='between(t,{_so(moc[0])},{_so(moc[-1])})'[v]",
                 "-map", "[v]"]
    else:
        lenh += ["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-map", "0:v:0"]
    lenh += ["-map", "0:a:0?",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", CRF, "-profile:v", "high", "-pix_fmt", "yuv420p",
             "-bf", "0", "-sc_threshold", "0", "-force_key_frames", ",".join(["0"] + [_so(x) for x in moc]),
             "-c:a", "aac", "-b:a", "128k",
             "-f", "segment", "-segment_format", "mpegts", "-segment_list", str(ra / "ds.csv"), "-segment_list_type", "csv"]
    if moc:
        lenh += ["-segment_times", ",".join(_so(x) for x in moc)]
    else:
        lenh += ["-segment_time", "100000"]
    lenh.append(str(ra / "c%05d.ts"))

    r = subprocess.run(lenh, capture_output=True, timeout=3600)
    if r.returncode != 0:
        raise LoiMedia("khong cat duoc video: " + r.stderr.decode(errors="replace")[-300:])

    mang: list[Mang] = []
    for dong in (ra / "ds.csv").read_text().split():
        phan = dong.split(",")
        if len(phan) >= 3:
            mang.append(Mang(phan[0], max(float(phan[2]) - float(phan[1]), 0.001)))
    if not mang:
        raise LoiMedia("cat video khong ra mang nao")
    return ChiMuc(mang)


def _ghi_chi_muc(cm: ChiMuc) -> bytes:
    return json.dumps({"v": 1, "mang": [{"ten": m.ten, "dai": round(m.dai, 6)} for m in cm.mang]}).encode()


def doc_chi_muc(du_lieu: bytes) -> ChiMuc:
    d = json.loads(du_lieu)
    return ChiMuc([Mang(str(m["ten"]), float(m["dai"])) for m in d["mang"]])


def da_co(storage_key: str, a: float, b: float) -> bool:
    try:
        kho().thong_tin(khoa_chi_muc(storage_key, a, b))
        return True
    except KhongCoFile:
        return False


def cat_va_luu(goc: Path, storage_key: str, a: float, b: float, chu_chung: str) -> ChiMuc:
    """Cat mot doan mau tu file goc da tai ve roi ghi len MinIO (mang truoc, index.json SAU CUNG)."""
    ra = Path(tempfile.mkdtemp(prefix="media-hls-"))
    try:
        cm = _cat_mot_doan(goc, a, b, chu_chung, ra)
        for m in cm.mang:
            kho().tai_len(khoa_mang(storage_key, a, b, m.ten), str(ra / m.ten), LOAI_MANG)
        kho().ghi(khoa_chi_muc(storage_key, a, b), _ghi_chi_muc(cm), "application/json")
        return cm
    finally:
        shutil.rmtree(ra, ignore_errors=True)


def tai_goc(storage_key: str) -> Path:
    fd, duong = tempfile.mkstemp(prefix="media-goc-", suffix=PurePosixPath(storage_key).suffix)
    os.close(fd)
    try:
        kho().tai_ve(storage_key, duong)
    except BaseException:
        os.unlink(duong)
        raise
    return Path(duong)


def thoi_luong(duong_dan: Path) -> float:
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(duong_dan)],
                       capture_output=True, text=True, timeout=60)
    try:
        return float(r.stdout.strip())
    except ValueError as ex:
        raise LoiMedia("khong doc duoc thoi luong video") from ex


def cat_cac_doan(storage_key: str, doan: list[tuple[float, float | None]], chu_chung: str) -> int:
    """Viec nap: cat moi doan chua co (b None = het file). Tai file goc MOT lan. Tra so doan vua cat.
    Nem LoiMedia (file hong), KhongCoFile (file goc da xoa)."""
    goc: Path | None = None
    try:
        so = 0
        for a, b in doan:
            if b is not None and da_co(storage_key, a, b):
                continue
            if goc is None:
                goc = tai_goc(storage_key)
            if b is None:
                b = thoi_luong(goc)
                if da_co(storage_key, a, b):
                    continue
            cat_va_luu(goc, storage_key, a, b, chu_chung)
            so += 1
        return so
    finally:
        if goc is not None:
            goc.unlink(missing_ok=True)


def dam_bao_doan(storage_key: str, a: float, b: float, chu_chung: str) -> ChiMuc:
    """Luc phat: doc chi muc; chua co (video nap truoc ban nay / viec nap chua chay) thi cat tai cho."""
    try:
        return doc_chi_muc(kho().doc(khoa_chi_muc(storage_key, a, b)))
    except KhongCoFile:
        pass
    log.info("Chua co HLS cua %s [%s, %s] — cat tai cho", storage_key, a, b)
    goc = tai_goc(storage_key)
    try:
        return cat_va_luu(goc, storage_key, a, b, chu_chung)
    finally:
        goc.unlink(missing_ok=True)


# =====================================================================
# Phat
# =====================================================================


def danh_sach_phat(cm: ChiMuc) -> str:
    """Danh sach phat VOD. Duong dan TUONG DOI voi ".../media/v/<token>/index.m3u8"."""
    dong = ["#EXTM3U", "#EXT-X-VERSION:3", "#EXT-X-PLAYLIST-TYPE:VOD",
            f"#EXT-X-TARGETDURATION:{max(1, math.ceil(max(m.dai for m in cm.mang)))}",
            "#EXT-X-MEDIA-SEQUENCE:0", "#EXT-X-INDEPENDENT-SEGMENTS"]
    cuoi = len(cm.mang) - 1
    for i, m in enumerate(cm.mang):
        if cm.mot_mang:
            uri = "p/ca.ts"
        elif i == 0:
            uri = "p/dau.ts"
        elif i == cuoi:
            uri = "p/cuoi.ts"
        else:
            uri = f"c/{i:05d}.ts"
        # Mang rieng ma hoa khac lan voi mang dung chung → bao trinh phat khoi tao lai bo giai ma.
        if i > 0 and (cm.la_mang_rieng(i) or cm.la_mang_rieng(i - 1)):
            dong.append("#EXT-X-DISCONTINUITY")
        dong.append(f"#EXTINF:{m.dai:.3f},")
        dong.append(uri)
    dong.append("#EXT-X-ENDLIST")
    return "\n".join(dong) + "\n"


def chi_so_mang_rieng(cm: ChiMuc, ten: str) -> int | None:
    """"dau" | "cuoi" | "ca" → vi tri mang trong chi muc; khong hop voi chi muc → None."""
    if cm.mot_mang:
        return 0 if ten == "ca" else None
    if ten == "dau":
        return 0
    if ten == "cuoi":
        return len(cm.mang) - 1
    return None


def tao_mang_rieng(sach: Path, dich: Path, chu: str) -> None:
    """Ma hoa lai MOT mang sach (dau / cuoi) co chu cua nguoi xem — PyAV trong tien trinh, giu nguyen moc thoi gian;
    am thanh chep nguyen goi."""
    try:
        with av.open(str(sach)) as vao, av.open(str(dich), "w", format="mpegts") as ra:
            vs = vao.streams.video[0]
            am = vao.streams.audio[0] if vao.streams.audio else None
            ovs = ra.add_stream("libx264", rate=vs.average_rate or vs.guessed_rate or 25)
            ovs.width = vs.codec_context.width
            ovs.height = vs.codec_context.height
            ovs.pix_fmt = "yuv420p"
            ovs.time_base = vs.time_base
            ovs.codec_context.time_base = vs.time_base
            ovs.options = {"preset": "veryfast", "crf": CRF_RIENG, "bf": "0"}
            oam = ra.add_stream_from_template(am) if am is not None else None
            lop = chu_noi.lop_chu(chu, ovs.width, ovs.height)

            luong = [vs] if am is None else [vs, am]
            for goi in vao.demux(*luong):
                if goi.stream is vs:
                    for khung in goi.decode():
                        anh = khung.to_ndarray(format="bgr24")
                        chu_noi.phu_len(anh, lop)
                        moi = av.VideoFrame.from_ndarray(anh, format="bgr24")
                        moi.pts = khung.pts
                        moi.time_base = khung.time_base
                        for g in ovs.encode(moi):
                            ra.mux(g)
                elif oam is not None and goi.dts is not None:
                    goi.stream = oam
                    ra.mux(goi)
            for g in ovs.encode(None):
                ra.mux(g)
    except av.FFmpegError as ex:
        raise LoiMedia(f"khong ma hoa duoc mang rieng: {ex}") from ex
