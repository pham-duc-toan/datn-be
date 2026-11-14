"""API cua media-svc.

  GET  /media/v/{token}                — phat file mau (khong can dang nhap: token la quyen xem, song 5 phut)
  GET  /media/v/{token}/index.m3u8     — video co watermark: danh sach phat HLS cua doan mau
  GET  /media/v/{token}/c/{so}.ts      — mang giua dung chung
  GET  /media/v/{token}/p/{ten}.ts     — mang dau / cuoi rieng nguoi xem (dau | cuoi | ca)
  POST /media/admin/watermark/decode   — am thanh nghi ro ri → giai ma watermark chim → ai da nghe (admin).
                                         Anh / video mang chu NOI (doc thang tren hinh) — khong can giai ma.
"""

import asyncio
import os
import tempfile
from pathlib import Path, PurePosixPath

from fastapi import APIRouter, Depends, File, Request, UploadFile
from fastapi.responses import Response

from app import watermark, watermark_av
from app.auth import NguoiGoi, can_vai_tro
from app.errors import LoiApi
from app.views import khoa_watermark, phuc_vu, phuc_vu_danh_sach, phuc_vu_mang_chung, phuc_vu_mang_rieng

router = APIRouter(prefix="/media")

KICH_THUOC_AM_GIAI_MA_TOI_DA = 500 * 1024 * 1024
DUOI_AM = {".mp3", ".wav", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac"}
DUOI_HINH = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v"}
MANG_RIENG = {"dau", "cuoi", "ca"}


@router.get("/v/{token}")
async def xem(token: str, request: Request) -> Response:
    return await phuc_vu(token, request)


@router.get("/v/{token}/index.m3u8")
async def danh_sach_phat(token: str) -> Response:
    return await phuc_vu_danh_sach(token)


@router.get("/v/{token}/c/{so}.ts")
async def mang_chung(token: str, so: int) -> Response:
    return await phuc_vu_mang_chung(token, so)


@router.get("/v/{token}/p/{ten}.ts")
async def mang_rieng(token: str, ten: str, request: Request) -> Response:
    if ten not in MANG_RIENG:
        raise LoiApi(404, "khong_co_doan", "Không có đoạn này.")
    return await phuc_vu_mang_rieng(token, ten, request)


@router.post("/admin/watermark/decode")
async def giai_ma_watermark(file: UploadFile = File(...), ng: NguoiGoi = Depends(can_vai_tro("admin"))) -> dict:
    """
    File am thanh nghi ro ri → nguoi nghe nhung trong watermark chim (doan dau / cuoi). Khong tra bang nao: nguoi
    nghe nam ngay trong file (userId / ma phien cong link), CRC-32 xac nhan doc dung.
    """
    loai = (file.content_type or "").split(";")[0].strip().lower()
    duoi = PurePosixPath(file.filename or "").suffix.lower()
    if loai.startswith(("image/", "video/")) or duoi in DUOI_HINH:
        raise LoiApi(400, "khong_can_giai_ma",
                     "Ảnh và video mang chữ “nguồn: …” hiện ngay trên hình (góc dưới bên phải) — đọc trực tiếp, không cần giải mã.")
    if not (loai.startswith("audio/") or duoi in DUOI_AM):
        raise LoiApi(400, "media_khong_hop_le", "Chỉ giải mã được file âm thanh.")

    fd, tam = tempfile.mkstemp(prefix="media-giai-", suffix=duoi)
    try:
        tong = 0
        with os.fdopen(fd, "wb") as f:
            while True:
                khuc = await file.read(1024 * 1024)
                if not khuc:
                    break
                tong += len(khuc)
                if tong > KICH_THUOC_AM_GIAI_MA_TOI_DA:
                    raise LoiApi(413, "file_qua_lon", "Âm thanh tối đa 500 MB.")
                f.write(khuc)
        try:
            tt = await asyncio.to_thread(watermark_av.tham_do, Path(tam))
            if not tt.co_am:
                raise LoiApi(400, "media_khong_hop_le", "File không có âm thanh.")
            kq = await asyncio.to_thread(watermark_av.giai_file_am, Path(tam), khoa_watermark())
        except watermark_av.LoiMedia as ex:
            raise LoiApi(400, "media_khong_hop_le", "Không đọc được file âm thanh.") from ex
        return _ket_qua(kq)
    finally:
        os.unlink(tam)


def _ket_qua(kq: watermark.KetQuaGiai) -> dict:
    if kq.nguoi is None:
        return {"found": False, "confidence": round(kq.tin_cay, 3), "viewer": None, "mediaType": "audio"}
    return {
        "found": True, "confidence": round(kq.tin_cay, 3), "mediaType": "audio",
        "viewer": {
            "type": "guest" if kq.nguoi.la_khach else "user",
            "userId": None if kq.nguoi.la_khach else str(kq.nguoi.id),
            "guestSessionId": str(kq.nguoi.id) if kq.nguoi.la_khach else None,
        },
    }
