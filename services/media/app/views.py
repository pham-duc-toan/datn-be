"""Phat file mau qua link co token (FP-02, FP-03).

  original    — chu du an / admin: ban goc.
  watermarked — labeler / reviewer / khach cong link: anh mang watermark chim chua THANG nguoi xem
                (userId, hoac ma phien vuot link cua khach). Anh ro ri ra ngoai → admin giai ma → biet
                ai. Khong luu gi: nguoi xem nam trong token, giai ma doc nguoc tu anh.

Am thanh (watermarked): nhung ca file ra FLAC, luu bo dem dia theo (file, nguoi xem), phat co Range.
Video (watermarked): HAI tang bo dem —
  1. ban chuan (chuan_hoa_video) theo FILE GOC, dung chung moi nguoi xem: ma hoa lai mot lan;
  2. ban cua tung nguoi xem: chi nhung GOP dau + GOP cuoi (nhung_video_dau_cuoi), phan giua copy —
     vai giay, khong phu thuoc do dai video.
Ban goc (original) phat nguyen qua proxy.
"""

import asyncio
import hashlib
import logging
import mimetypes
import os
import tempfile
from functools import lru_cache
from pathlib import Path
from datetime import datetime, timezone
from pathlib import PurePosixPath

import cv2
import numpy as np
from fastapi import Request
from fastapi.responses import FileResponse, Response, StreamingResponse

from app import settings_store, watermark, watermark_av
from app.bo_dem import BoDem
from app.config import settings
from app.errors import LoiApi
from app.grant import QuyenXem, TokenKhongHopLe, doc_khoa, giai_ma
from app.storage import KhongCoFile, kho

log = logging.getLogger(__name__)

# Trinh duyet khong luu dia, khong doan kieu, hien tai cho (khong hop "Luu file").
HEADER_CHONG_LUU = {
    "Cache-Control": "private, no-store, max-age=0",
    "Pragma": "no-cache",
    "X-Content-Type-Options": "nosniff",
    "Content-Disposition": "inline",
    "Referrer-Policy": "no-referrer",
}


def khoa_grant() -> bytes:
    return doc_khoa(settings().grant_key)


def khoa_watermark() -> bytes:
    return doc_khoa(settings().watermark_key)


def doc_quyen(token: str) -> QuyenXem:
    try:
        return giai_ma(khoa_grant(), token)
    except TokenKhongHopLe as ex:
        raise LoiApi(403, "link_khong_hop_le", "Link xem khong hop le.") from ex


def nguoi_xem(q: QuyenXem) -> watermark.NguoiXem:
    if q.user_id is not None:
        return watermark.NguoiXem(watermark.LOAI_TAI_KHOAN, q.user_id)
    if q.guest_session_id is not None:
        return watermark.NguoiXem(watermark.LOAI_KHACH, q.guest_session_id)
    raise LoiApi(403, "link_khong_hop_le", "Link xem thieu nguoi xem.")


@lru_cache
def bo_dem() -> BoDem:
    cfg = settings()
    return BoDem(cfg.av_cache_dir, cfg.av_cache_ttl_seconds, cfg.av_cache_max_bytes)


def _loai_noi_dung(khoa: str) -> str:
    return mimetypes.guess_type(khoa)[0] or "application/octet-stream"


def gan_watermark(du_lieu: bytes, khoa_goc: str, nguoi: watermark.NguoiXem, buoc: int) -> tuple[bytes, str]:
    anh = cv2.imdecode(np.frombuffer(du_lieu, np.uint8), cv2.IMREAD_COLOR)
    if anh is None:
        raise ValueError("anh hong")
    moi = watermark.nhung(anh, nguoi, khoa_watermark(), buoc)
    if PurePosixPath(khoa_goc).suffix.lower() == ".png":
        _, buf = cv2.imencode(".png", moi)
        return buf.tobytes(), "image/png"
    _, buf = cv2.imencode(".jpg", moi, [cv2.IMWRITE_JPEG_QUALITY, 95])
    return buf.tobytes(), "image/jpeg"


async def phuc_vu(token: str, request: Request) -> Response:
    q = doc_quyen(token)
    if q.het_han(datetime.now(timezone.utc)):
        raise LoiApi(410, "link_het_han", "Link xem da het han — tai lai trang de lay link moi.")

    can_watermark = (q.modality in ("image", "audio", "video") and q.mode == "watermarked"
                     and settings_store.dung_sai(settings_store.WATERMARK_ENABLED))
    if not can_watermark:
        return await _phat_goc(q.storage_key, request)
    if q.modality in ("audio", "video"):
        return await _phat_av(q, request)

    try:
        du_lieu = await asyncio.to_thread(kho().doc, q.storage_key)
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_file", "File mau khong con.") from ex
    nguoi = nguoi_xem(q)
    buoc = settings_store.so_nguyen(settings_store.WATERMARK_STRENGTH)
    try:
        moi, loai = await asyncio.to_thread(gan_watermark, du_lieu, q.storage_key, nguoi, buoc)
    except ValueError as ex:
        raise LoiApi(409, "anh_hong", "Anh hong, khong hien thi duoc.") from ex
    return Response(moi, media_type=loai, headers=HEADER_CHONG_LUU)


async def _phat_av(q: QuyenXem, request: Request) -> Response:
    nguoi = nguoi_xem(q)
    buoc = settings_store.so_nguyen(settings_store.WATERMARK_STRENGTH)
    khoa_wm = khoa_watermark()
    la_video = q.modality == "video"
    # Khoa bo dem: file, nguoi xem, do manh, dau khoa watermark (doi khoa → ban cu vo hieu).
    khoa = BoDem.khoa(q.storage_key, str(nguoi.loai), str(nguoi.id), str(buoc) if la_video else "am",
                      hashlib.sha256(khoa_wm).hexdigest()[:16])

    def tai_goc(lam):
        def tao(dich: Path) -> None:
            fd, tam = tempfile.mkstemp(prefix="media-goc-", suffix=PurePosixPath(q.storage_key).suffix)
            os.close(fd)
            try:
                kho().tai_ve(q.storage_key, tam)
                lam(Path(tam), dich)
            finally:
                os.unlink(tam)
        return tao

    try:
        if la_video:
            # Tang 1: ban chuan theo file goc (moi nguoi xem dung chung). Tang 2: nhung dau + cuoi cho nguoi nay.
            chuan = await bo_dem().lay(BoDem.khoa(q.storage_key, "chuan-hoa-v1"), ".mp4", tai_goc(watermark_av.chuan_hoa_video))
            duong_dan = await bo_dem().lay(
                BoDem.khoa(khoa, "dau-cuoi-v1"), ".mp4",
                lambda dich: watermark_av.nhung_video_dau_cuoi(chuan, dich, nguoi, khoa_wm, buoc))
        else:
            duong_dan = await bo_dem().lay(khoa, ".flac", tai_goc(lambda nguon, dich: watermark_av.nhung_file_am(nguon, dich, nguoi, khoa_wm)))
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_file", "File mau khong con.") from ex
    except watermark_av.LoiMedia as ex:
        log.warning("Khong nhung duoc watermark cho %s: %s", q.storage_key, ex)
        raise LoiApi(409, "media_hong", "File hong, khong phat duoc.") from ex
    return _phat_tep(duong_dan, "video/mp4" if la_video else "audio/flac", request)


def _phat_tep(duong_dan: Path, loai: str, request: Request) -> Response:
    """Phat file tren dia, ho tro Range (giong _phat_goc)."""
    kich = duong_dan.stat().st_size
    headers = dict(HEADER_CHONG_LUU)
    headers["Accept-Ranges"] = "bytes"
    pham_vi = _doc_range(request.headers.get("Range"), kich)
    if pham_vi is None:
        return FileResponse(duong_dan, media_type=loai, headers=headers)

    dau, cuoi = pham_vi

    def doc():
        with open(duong_dan, "rb") as f:
            f.seek(dau)
            con = cuoi - dau + 1
            while con > 0:
                khuc = f.read(min(256 * 1024, con))
                if not khuc:
                    break
                con -= len(khuc)
                yield khuc

    headers["Content-Range"] = f"bytes {dau}-{cuoi}/{kich}"
    headers["Content-Length"] = str(cuoi - dau + 1)
    return StreamingResponse(doc(), status_code=206, media_type=loai, headers=headers)


async def _phat_goc(khoa: str, request: Request) -> Response:
    """Phat nguyen file, ho tro 'Range: bytes=a-b' (trinh duyet tua am thanh / video)."""
    try:
        tt = await asyncio.to_thread(kho().thong_tin, khoa)
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_file", "File mau khong con.") from ex

    loai = tt.content_type or _loai_noi_dung(khoa)
    if loai == "application/octet-stream":
        loai = _loai_noi_dung(khoa)
    headers = dict(HEADER_CHONG_LUU)
    headers["Accept-Ranges"] = "bytes"

    if tt.size == 0:
        return Response(b"", media_type=loai, headers=headers)

    pham_vi = _doc_range(request.headers.get("Range"), tt.size)
    if pham_vi is None:
        headers["Content-Length"] = str(tt.size)
        return StreamingResponse(kho().luong(khoa, 0, tt.size), media_type=loai, headers=headers)

    dau, cuoi = pham_vi
    headers["Content-Range"] = f"bytes {dau}-{cuoi}/{tt.size}"
    headers["Content-Length"] = str(cuoi - dau + 1)
    return StreamingResponse(kho().luong(khoa, dau, cuoi - dau + 1), status_code=206, media_type=loai, headers=headers)


def _doc_range(gia_tri: str | None, kich_thuoc: int) -> tuple[int, int] | None:
    """'bytes=a-b' | 'bytes=a-' | 'bytes=-n' → (dau, cuoi) bao gom; sai / nhieu doan thi phat ca file."""
    if not gia_tri or not gia_tri.startswith("bytes=") or "," in gia_tri or kich_thuoc == 0:
        return None
    a, _, b = gia_tri[len("bytes="):].strip().partition("-")
    try:
        if a == "":
            n = int(b)
            if n <= 0:
                return None
            return max(kich_thuoc - n, 0), kich_thuoc - 1
        dau = int(a)
        cuoi = int(b) if b else kich_thuoc - 1
    except ValueError:
        return None
    if dau > cuoi or dau >= kich_thuoc:
        return None
    return dau, min(cuoi, kich_thuoc - 1)
