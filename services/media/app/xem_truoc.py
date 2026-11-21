"""Ban xem truoc DUNG CHUNG tren the cho du an (kien truc 3.40).

Feed cho du an hien mot mau / du an cho rat nhieu nguoi luot. Ve watermark rieng tung nguoi moi luot (ma hoa lai anh,
PyAV mang video rieng, nhung am thanh rieng) ton CPU theo so nguoi xem — nen moi du an chi CO MOT ban xem truoc, tao
MOT LAN roi ai cung lay chung:
  - anh: thu nho canh dai CANH_ANH px + chu DUNG CHUNG "nguồn: <doanh nghiep> - <domain>";
  - video: clip CLIP_GIAY giay dau doan mau, ha ve toi da 720p + chu dung chung (MP4, faststart → tua bang Range);
  - am thanh: clip CLIP_GIAY giay, watermark chim mang MA DU AN (khong co nguoi nghe).
Chu cua NGUOI XEM do frontend ve de len (chup / quay man hinh van thay). Bai lam that van watermark rieng tung nguoi.

Tao khi: project-svc phat project.preview_requested (du an bat dau chay / bat lai xem truoc) — consumer o day; hoac
LUC PHAT ma chua co (du an chay tu truoc ban nay, event chua toi) — tao tai cho, moi khoa mot luong (`dam_bao`).
Luu MinIO "<projectId>/_preview/v1/<sampleId>/<file>" — khong DB, khoa tat dinh nen giao lai / tao trung la vo hai.
"""

import asyncio
import logging
import shutil
import subprocess
import tempfile
import uuid
from pathlib import Path

import aio_pika
import cv2
import numpy as np

from app import chu_noi, hls, settings_store, watermark, watermark_av
from app.config import settings
from app.contracts import ProjectPreviewRequested
from app.messaging import envelope as env_mod
from app.messaging import thu_lai
from app.messaging.envelope import LoiHopDong
from app.storage import KhongCoFile, kho

log = logging.getLogger(__name__)

PHIEN_BAN = "v1"
CANH_ANH = 1080
CAO_VIDEO = 720
CLIP_GIAY = 15.0
TEN_FILE = {"image": "anh.jpg", "video": "video.mp4", "audio": "am.flac"}
LOAI_FILE = {"image": "image/jpeg", "video": "video/mp4", "audio": "audio/flac"}
TEN_QUEUE = "media-svc.preview-requested"


def khoa(project_id: uuid.UUID, sample_id: uuid.UUID, modality: str) -> str:
    """Khoa MinIO cua ban xem truoc. Doi cach tao → tang PHIEN_BAN (ban cu tu bi bo qua)."""
    return f"{project_id}/_preview/{PHIEN_BAN}/{sample_id}/{TEN_FILE[modality]}"


def da_co(key: str) -> bool:
    try:
        kho().thong_tin(key)
        return True
    except KhongCoFile:
        return False


def anh_xem_truoc(du_lieu: bytes, chu: str | None) -> bytes:
    """Anh goc (bytes) → JPEG thu nho canh dai <= CANH_ANH, co chu dung chung (neu co). Ham thuan — de test."""
    anh = cv2.imdecode(np.frombuffer(du_lieu, np.uint8), cv2.IMREAD_COLOR)
    if anh is None:
        raise watermark_av.LoiMedia("anh hong")
    cao, rong = anh.shape[:2]
    ti_le = CANH_ANH / max(cao, rong)
    if ti_le < 1:
        anh = cv2.resize(anh, (max(1, round(rong * ti_le)), max(1, round(cao * ti_le))), interpolation=cv2.INTER_AREA)
    if chu:
        anh = chu_noi.phu_chu(anh, chu)
    ok, buf = cv2.imencode(".jpg", anh, [cv2.IMWRITE_JPEG_QUALITY, 85])
    if not ok:
        raise watermark_av.LoiMedia("khong ma hoa duoc anh")
    return buf.tobytes()


def kich_thuoc_video(rong: int, cao: int) -> tuple[int, int]:
    """Ha ve toi da CAO_VIDEO dong, giu ti le, canh chan (libx264 yuv420p)."""
    if cao > CAO_VIDEO:
        rong, cao = round(rong * CAO_VIDEO / cao), CAO_VIDEO
    return rong + rong % 2, cao + cao % 2


def doan_clip(a: float | None, b: float | None) -> tuple[float, float]:
    """[bat dau, do dai] cua clip: CLIP_GIAY giay dau doan mau (b None = toi het file, ffmpeg tu dung)."""
    dau = float(a or 0)
    dai = CLIP_GIAY if b is None else max(0.1, min(CLIP_GIAY, float(b) - dau))
    return dau, dai


def _chay(lenh: list[str], loi: str) -> None:
    r = subprocess.run(lenh, capture_output=True, timeout=600)
    if r.returncode != 0:
        raise watermark_av.LoiMedia(loi + ": " + r.stderr.decode(errors="replace")[-300:])


def _tao_video(goc: Path, a: float | None, b: float | None, chu: str | None, dich: Path) -> None:
    tt = watermark_av.tham_do(goc)
    if not tt.co_video or tt.rong <= 0 or tt.cao <= 0:
        raise watermark_av.LoiMedia("file khong co hinh")
    dau, dai = doan_clip(a, b)
    rong, cao = kich_thuoc_video(tt.rong, tt.cao)
    lenh = ["ffmpeg", "-v", "error", "-y", "-ss", f"{dau:.3f}", "-t", f"{dai:.3f}", "-i", str(goc)]
    if chu:
        lop = dich.parent / "lop.png"
        lop.write_bytes(chu_noi.png_toan_khung(chu, rong, cao))
        lenh += ["-i", str(lop), "-filter_complex", f"[0:v]scale={rong}:{cao},setsar=1[v0];[v0][1:v]overlay=0:0[v]", "-map", "[v]"]
    else:
        lenh += ["-vf", f"scale={rong}:{cao},setsar=1", "-map", "0:v:0"]
    lenh += ["-map", "0:a:0?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p",
             "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", str(dich)]
    _chay(lenh, "khong tao duoc clip video xem truoc")


def _tao_am(goc: Path, a: float | None, b: float | None, project_id: uuid.UUID, khoa_wm: bytes, dich: Path) -> None:
    dau, dai = doan_clip(a, b)
    tam = dich.parent / "clip.wav"
    _chay(["ffmpeg", "-v", "error", "-y", "-ss", f"{dau:.3f}", "-t", f"{dai:.3f}", "-i", str(goc), "-vn", "-map", "0:a:0",
           "-c:a", "pcm_s16le", str(tam)], "khong cat duoc clip am thanh xem truoc")
    try:
        # Watermark chim mang MA DU AN (dang "khach") — file lot ra van biet cua du an nao, khong gan voi nguoi nghe.
        watermark_av.nhung_file_am(tam, dich, watermark.NguoiXem(watermark.LOAI_KHACH, project_id), khoa_wm)
    except watermark_av.LoiMedia:
        # Clip qua ngan de mang watermark: giu clip tran (van la ban xem truoc ngan, khong phai ca file).
        _chay(["ffmpeg", "-v", "error", "-y", "-i", str(tam), "-c:a", "flac", "-sample_fmt", "s16", str(dich)],
              "khong ma hoa duoc clip am thanh")


def tao(project_id: uuid.UUID, sample_id: uuid.UUID, modality: str, storage_key: str,
        a: float | None, b: float | None, chu: str | None, khoa_wm: bytes) -> str:
    """Tao ban xem truoc va ghi MinIO (ghi de neu da co — noi dung tat dinh). Tra khoa MinIO.
    Nem KhongCoFile (file goc da xoa), LoiMedia (file hong)."""
    key = khoa(project_id, sample_id, modality)
    if modality == "image":
        kho().ghi(key, anh_xem_truoc(kho().doc(storage_key), chu), LOAI_FILE["image"])
        return key

    ra = Path(tempfile.mkdtemp(prefix="media-xt-"))
    goc: Path | None = None
    try:
        goc = hls.tai_goc(storage_key)
        dich = ra / TEN_FILE[modality]
        if modality == "video":
            _tao_video(goc, a, b, chu, dich)
        elif modality == "audio":
            _tao_am(goc, a, b, project_id, khoa_wm, dich)
        else:
            raise watermark_av.LoiMedia("loai du lieu khong co ban xem truoc")
        kho().tai_len(key, str(dich), LOAI_FILE[modality])
        return key
    finally:
        if goc is not None:
            goc.unlink(missing_ok=True)
        shutil.rmtree(ra, ignore_errors=True)


_dang_tao: dict[str, asyncio.Lock] = {}


async def dam_bao(project_id: uuid.UUID, sample_id: uuid.UUID, modality: str, storage_key: str,
                  a: float | None, b: float | None, chu: str | None, khoa_wm: bytes) -> str:
    """Luc phat: co roi thi tra khoa; chua co thi tao tai cho — moi khoa MOT luong tao, nguoi khac doi chung."""
    key = khoa(project_id, sample_id, modality)
    if await asyncio.to_thread(da_co, key):
        return key
    lock = _dang_tao.setdefault(key, asyncio.Lock())
    try:
        async with lock:
            if not await asyncio.to_thread(da_co, key):
                log.info("Chua co ban xem truoc %s — tao tai cho", key)
                await asyncio.to_thread(tao, project_id, sample_id, modality, storage_key, a, b, chu, khoa_wm)
    finally:
        if not lock.locked():
            _dang_tao.pop(key, None)
    return key


class XemTruocSan:
    """Consumer project.preview_requested: tao ban xem truoc ngay luc du an bat dau chay (moi lan MOT viec)."""

    async def bat_dau(self, ket_noi: aio_pika.abc.AbstractRobustConnection) -> None:
        cfg = settings()
        kenh = await ket_noi.channel()
        await kenh.set_qos(prefetch_count=1)
        exchange = await kenh.declare_exchange(cfg.exchange, aio_pika.ExchangeType.TOPIC, durable=True)
        dlx = await kenh.declare_exchange(cfg.dead_letter_exchange, aio_pika.ExchangeType.DIRECT, durable=True)
        dlq = await kenh.declare_queue(TEN_QUEUE + ".dlq", durable=True)
        await dlq.bind(dlx, routing_key=TEN_QUEUE)
        q = await kenh.declare_queue(TEN_QUEUE, durable=True, arguments={
            "x-queue-type": "quorum", "x-delivery-limit": cfg.delivery_limit,
            "x-dead-letter-exchange": cfg.dead_letter_exchange, "x-dead-letter-routing-key": TEN_QUEUE})
        await q.bind(exchange, routing_key=ProjectPreviewRequested.EVENT_TYPE)
        await q.consume(self._nhan)
        log.info("Consumer %s dang nghe %s", TEN_QUEUE, ProjectPreviewRequested.EVENT_TYPE)

    async def _nhan(self, message: aio_pika.abc.AbstractIncomingMessage) -> None:
        try:
            p = env_mod.doc(message.body, ProjectPreviewRequested).payload
        except LoiHopDong as ex:
            log.error("Queue %s: message sai hop dong → DLQ: %s", TEN_QUEUE, ex)
            await message.reject(requeue=False)
            return
        if p.modality not in TEN_FILE:
            await message.ack()
            return
        try:
            from app.views import khoa_watermark

            a = None if p.segment_start_ms is None else p.segment_start_ms / 1000
            b = None if p.segment_end_ms is None else p.segment_end_ms / 1000
            key = await asyncio.to_thread(tao, p.project_id, p.sample_id, p.modality, p.storage_key,
                                          a, b, p.watermark_text, khoa_watermark())
            log.info("Da tao ban xem truoc %s", key)
        except KhongCoFile:
            log.info("File %s khong con — bo qua ban xem truoc", p.storage_key)
        except watermark_av.LoiMedia as ex:
            # File hong: khong thu lai (lan phat sau cung se bao loi 409) — chi ghi lai.
            log.warning("Khong tao duoc ban xem truoc cho mau %s: %s", p.sample_id, ex)
        except Exception:  # noqa: BLE001 — MinIO / dia: thu lai, qua x-delivery-limit thi vao DLQ
            da = thu_lai.so_lan_da_that_bai(message.headers)
            if thu_lai.la_lan_cuoi(da, settings().delivery_limit):
                log.exception("Tao ban xem truoc mau %s that bai %d lan, chuyen vao DLQ", p.sample_id, da + 1)
            else:
                cho = thu_lai.cho_truoc_khi_thu_lai(da, settings_store.giay(settings_store.CONSUMERS_RETRY_MAX_DELAY))
                log.warning("Tao ban xem truoc mau %s loi (lan %d), thu lai sau %.0fs", p.sample_id, da + 1, cho, exc_info=True)
                await thu_lai.ngu(cho)
            await message.nack(requeue=True)
            return
        await message.ack()

