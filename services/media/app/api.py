"""API cua media-svc.

  GET  /media/v/{token}                — phat file mau (khong can dang nhap: token la quyen xem, song 5 phut)
  POST /media/admin/watermark/decode   — anh / am thanh / video nghi ro ri → giai ma watermark → ai da nhan (admin)
"""

import asyncio
import os
import tempfile
from pathlib import Path, PurePosixPath

import cv2
import numpy as np
from fastapi import APIRouter, Depends, File, Form, Request, UploadFile
from fastapi.responses import Response

from app import settings_store, watermark, watermark_av
from app.auth import NguoiGoi, can_vai_tro
from app.errors import LoiApi
from app.views import khoa_watermark, phuc_vu

router = APIRouter(prefix="/media")

KICH_THUOC_ANH_GIAI_MA_TOI_DA = 20 * 1024 * 1024
KICH_THUOC_AV_GIAI_MA_TOI_DA = 500 * 1024 * 1024
DUOI_AV = {".mp3", ".wav", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v"}
CANH_TOI_DA = 20000


@router.get("/v/{token}")
async def xem(token: str, request: Request) -> Response:
    return await phuc_vu(token, request)


@router.post("/admin/watermark/decode")
async def giai_ma_watermark(
    file: UploadFile = File(...), width: int | None = Form(None), height: int | None = Form(None),
    ng: NguoiGoi = Depends(can_vai_tro("admin")),
) -> dict:
    """
    Anh nghi ro ri (anh tai ve, anh chup lai) → nguoi xem nhung trong watermark. Khong tra bang nao:
    nguoi xem nam ngay trong anh (userId / ma phien cong link), CRC-32 xac nhan doc dung.
    width / height (tuy chon): kich thuoc GOC cua mau (metadata) — anh bi thu nho thi phong lai dung
    kich thuoc truoc khi giai. Thu buoc QIM hien tai truoc, roi moi buoc trong khoang cho phep.
    """
    if (width is None) != (height is None) or (width is not None and not (8 <= width <= CANH_TOI_DA and 8 <= height <= CANH_TOI_DA)):
        raise LoiApi(400, "kich_thuoc_khong_hop_le", "width và height phải đi cùng nhau, từ 8 đến 20000.")
    loai = (file.content_type or "").split(";")[0].strip().lower()
    if loai.startswith(("audio/", "video/")) or PurePosixPath(file.filename or "").suffix.lower() in DUOI_AV:
        return await _giai_av(file, width, height)

    du_lieu = await file.read(KICH_THUOC_ANH_GIAI_MA_TOI_DA + 1)
    if len(du_lieu) > KICH_THUOC_ANH_GIAI_MA_TOI_DA:
        raise LoiApi(413, "file_qua_lon", "Ảnh tối đa 20 MB.")
    anh = cv2.imdecode(np.frombuffer(du_lieu, np.uint8), cv2.IMREAD_COLOR)
    if anh is None:
        raise LoiApi(400, "anh_khong_hop_le", "Không đọc được ảnh.")

    ung_vien = [anh]
    if width is not None and (anh.shape[1], anh.shape[0]) != (width, height):
        ung_vien.append(cv2.resize(anh, (width, height), interpolation=cv2.INTER_CUBIC))

    cac_buoc = watermark.thu_tu_buoc(settings_store.so_nguyen(settings_store.WATERMARK_STRENGTH))
    khoa = khoa_watermark()
    tot_nhat = 0.0
    for a in ung_vien:
        kq = await asyncio.to_thread(watermark.giai, a, khoa, cac_buoc)
        tot_nhat = max(tot_nhat, kq.tin_cay)
        if kq.nguoi is not None:
            return _ket_qua(kq, "image")
    return {"found": False, "confidence": round(tot_nhat, 3), "viewer": None, "mediaType": "image"}


def _ket_qua(kq: watermark.KetQuaGiai, loai: str) -> dict:
    if kq.nguoi is None:
        return {"found": False, "confidence": round(kq.tin_cay, 3), "viewer": None, "mediaType": loai}
    return {
        "found": True, "confidence": round(kq.tin_cay, 3), "mediaType": loai,
        "viewer": {
            "type": "guest" if kq.nguoi.la_khach else "user",
            "userId": None if kq.nguoi.la_khach else str(kq.nguoi.id),
            "guestSessionId": str(kq.nguoi.id) if kq.nguoi.la_khach else None,
        },
    }


async def _giai_av(file: UploadFile, width: int | None, height: int | None) -> dict:
    """Am thanh / video: ghi ra file tam (ffmpeg doc tu dia), co hinh thi giai theo khung, khong thi theo am."""
    fd, tam = tempfile.mkstemp(prefix="media-giai-", suffix=PurePosixPath(file.filename or "").suffix)
    try:
        tong = 0
        with os.fdopen(fd, "wb") as f:
            while True:
                khuc = await file.read(1024 * 1024)
                if not khuc:
                    break
                tong += len(khuc)
                if tong > KICH_THUOC_AV_GIAI_MA_TOI_DA:
                    raise LoiApi(413, "file_qua_lon", "Âm thanh / video tối đa 500 MB.")
                f.write(khuc)
        khoa = khoa_watermark()
        try:
            tt = await asyncio.to_thread(watermark_av.tham_do, Path(tam))
            if tt.co_video:
                cac_buoc = watermark.thu_tu_buoc(settings_store.so_nguyen(settings_store.WATERMARK_STRENGTH))
                kq = await asyncio.to_thread(watermark_av.giai_file_video, Path(tam), khoa, cac_buoc, width, height)
                return _ket_qua(kq, "video")
            if tt.co_am:
                return _ket_qua(await asyncio.to_thread(watermark_av.giai_file_am, Path(tam), khoa), "audio")
        except watermark_av.LoiMedia as ex:
            raise LoiApi(400, "media_khong_hop_le", "Không đọc được file âm thanh / video.") from ex
        raise LoiApi(400, "media_khong_hop_le", "File không có hình hay âm thanh.")
    finally:
        os.unlink(tam)
