"""Phat file mau qua link co token (FP-02, FP-03).

  original    — chu du an / admin: ban goc, phat nguyen qua proxy (co Range).
  watermarked — labeler / reviewer / khach cong link:
    anh        → chu NOI mo goc duoi phai "nguồn: <nguoi xem> - <doanh nghiep> - <domain>" (chu dung san trong token),
                 ve moi lan phat (vai ms);
    video      → danh sach phat HLS cua DOAN MAU (hls.py): mang giua dung chung (chu doanh nghiep + domain in san luc
                 nap), mang dau + cuoi rieng nguoi xem (PyAV ve chu cua nguoi xem, luu bo dem dia). Frontend ve them chu
                 cua nguoi xem de len trinh phat (quay man hinh van thay);
    am thanh   → watermark CHIM ma nguoi xem o doan dau + doan cuoi (watermark_av.nhung_file_am), FLAC, bo dem dia.

Duong dan:
  GET /media/v/{token}                 anh / am thanh / ban goc
  GET /media/v/{token}/index.m3u8      danh sach phat video (token con han)
  GET /media/v/{token}/c/{so}.ts       mang giua dung chung   } duoc tai them hls_grace_seconds sau khi token het han
  GET /media/v/{token}/p/{ten}.ts      mang rieng dau / cuoi  } (labeler tam dung roi xem tiep)
"""

import asyncio
import hashlib
import logging
import mimetypes
import os
import tempfile
from datetime import datetime, timedelta, timezone
from functools import lru_cache
from pathlib import Path, PurePosixPath

import cv2
import numpy as np
from fastapi import Request
from fastapi.responses import FileResponse, Response, StreamingResponse

from app import chu_noi, hls, settings_store, watermark, watermark_av
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
LOAI_DANH_SACH = "application/vnd.apple.mpegurl"


def khoa_grant() -> bytes:
    return doc_khoa(settings().grant_key)


def khoa_watermark() -> bytes:
    return doc_khoa(settings().watermark_key)


def doc_quyen(token: str) -> QuyenXem:
    try:
        return giai_ma(khoa_grant(), token)
    except TokenKhongHopLe as ex:
        raise LoiApi(403, "link_khong_hop_le", "Link xem không hợp lệ.") from ex


def nguoi_xem(q: QuyenXem) -> watermark.NguoiXem:
    if q.user_id is not None:
        return watermark.NguoiXem(watermark.LOAI_TAI_KHOAN, q.user_id)
    if q.guest_session_id is not None:
        return watermark.NguoiXem(watermark.LOAI_KHACH, q.guest_session_id)
    raise LoiApi(403, "link_khong_hop_le", "Link xem thiếu người xem.")


def chu_cua(q: QuyenXem) -> str:
    """Chu noi cua nguoi xem (C# dung san). Token thieu (kieu cu) → chi ma nguoi xem."""
    if q.chu_ca_nhan:
        return q.chu_ca_nhan
    return f"nguồn: {nguoi_xem(q).id}"


@lru_cache
def bo_dem() -> BoDem:
    cfg = settings()
    return BoDem(cfg.av_cache_dir, cfg.av_cache_ttl_seconds, cfg.av_cache_max_bytes)


def _loai_noi_dung(khoa: str) -> str:
    return mimetypes.guess_type(khoa)[0] or "application/octet-stream"


def gan_chu_noi(du_lieu: bytes, khoa_goc: str, chu: str) -> tuple[bytes, str]:
    anh = cv2.imdecode(np.frombuffer(du_lieu, np.uint8), cv2.IMREAD_COLOR)
    if anh is None:
        raise ValueError("anh hong")
    moi = chu_noi.phu_chu(anh, chu)
    if PurePosixPath(khoa_goc).suffix.lower() == ".png":
        _, buf = cv2.imencode(".png", moi)
        return buf.tobytes(), "image/png"
    _, buf = cv2.imencode(".jpg", moi, [cv2.IMWRITE_JPEG_QUALITY, 95])
    return buf.tobytes(), "image/jpeg"


def _bat_watermark(q: QuyenXem) -> bool:
    return (q.modality in ("image", "audio", "video") and q.mode == "watermarked"
            and settings_store.dung_sai(settings_store.WATERMARK_ENABLED))


async def phuc_vu(token: str, request: Request) -> Response:
    q = doc_quyen(token)
    if q.het_han(datetime.now(timezone.utc)):
        raise LoiApi(410, "link_het_han", "Link xem đã hết hạn — tải lại trang để lấy link mới.")

    if not _bat_watermark(q):
        return await _phat_goc(q.storage_key, request)
    if q.modality == "video":
        # Video co watermark chi phat qua danh sach HLS (.../index.m3u8) — link kieu cu / vua bat lai watermark.
        raise LoiApi(410, "link_het_han", "Link xem đã hết hạn — tải lại trang để lấy link mới.")
    if q.modality == "audio":
        return await _phat_am(q, request)

    try:
        du_lieu = await asyncio.to_thread(kho().doc, q.storage_key)
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_file", "File mẫu không còn.") from ex
    try:
        moi, loai = await asyncio.to_thread(gan_chu_noi, du_lieu, q.storage_key, chu_cua(q))
    except ValueError as ex:
        raise LoiApi(409, "anh_hong", "Ảnh hỏng, không hiển thị được.") from ex
    return Response(moi, media_type=loai, headers=HEADER_CHONG_LUU)


async def _phat_am(q: QuyenXem, request: Request) -> Response:
    nguoi = nguoi_xem(q)
    khoa_wm = khoa_watermark()
    # Khoa bo dem: file, nguoi xem, dau khoa watermark (doi khoa → ban cu vo hieu).
    khoa = BoDem.khoa(q.storage_key, str(nguoi.loai), str(nguoi.id), "am", hashlib.sha256(khoa_wm).hexdigest()[:16])

    def tao(dich: Path) -> None:
        fd, tam = tempfile.mkstemp(prefix="media-goc-", suffix=PurePosixPath(q.storage_key).suffix)
        os.close(fd)
        try:
            kho().tai_ve(q.storage_key, tam)
            watermark_av.nhung_file_am(Path(tam), dich, nguoi, khoa_wm)
        finally:
            os.unlink(tam)

    try:
        duong_dan = await bo_dem().lay(khoa, ".flac", tao)
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_file", "File mẫu không còn.") from ex
    except watermark_av.LoiMedia as ex:
        log.warning("Khong nhung duoc watermark cho %s: %s", q.storage_key, ex)
        raise LoiApi(409, "media_hong", "File hỏng, không phát được.") from ex
    return _phat_tep(duong_dan, "audio/flac", request)


# =====================================================================
# Video: HLS
# =====================================================================


_khoa_cat: dict[tuple[str, float, float], asyncio.Lock] = {}


def _quyen_hls(token: str, cho_gia_han: bool) -> QuyenXem:
    q = doc_quyen(token)
    if q.modality != "video" or q.mode != "watermarked":
        raise LoiApi(404, "khong_co_danh_sach_phat", "Link này không phải video phát theo đoạn.")
    bay_gio = datetime.now(timezone.utc)
    if cho_gia_han:
        if bay_gio >= q.expires_at + timedelta(seconds=settings().hls_grace_seconds):
            raise LoiApi(410, "link_het_han", "Link xem đã hết hạn — tải lại trang để lấy link mới.")
    elif q.het_han(bay_gio):
        raise LoiApi(410, "link_het_han", "Link xem đã hết hạn — tải lại trang để lấy link mới.")
    if q.bat_dau is None or q.ket_thuc is None or q.ket_thuc <= q.bat_dau:
        raise LoiApi(409, "thieu_doan_mau", "Link xem thiếu đoạn của mẫu — tải lại trang để lấy link mới.")
    return q


@lru_cache(maxsize=2048)
def _chi_muc_da_luu(storage_key: str, a: float, b: float) -> hls.ChiMuc:
    """Chi muc khong doi sau khi ghi → nho trong RAM. Chua co thi KhongCoFile (khong bi nho)."""
    return hls.doc_chi_muc(kho().doc(hls.khoa_chi_muc(storage_key, a, b)))


async def _chi_muc(q: QuyenXem) -> hls.ChiMuc:
    key, a, b = q.storage_key, float(q.bat_dau), float(q.ket_thuc)
    try:
        return await asyncio.to_thread(_chi_muc_da_luu, key, a, b)
    except KhongCoFile:
        pass
    # Chua cat san (video nap truoc ban nay): cat tai cho, moi doan mot lan trong tien trinh.
    khoa = _khoa_cat.setdefault((key, a, b), asyncio.Lock())
    async with khoa:
        try:
            return await asyncio.to_thread(_chi_muc_da_luu, key, a, b)
        except KhongCoFile:
            pass
        chu_chung = q.chu_chung or "nguồn:"
        try:
            await asyncio.to_thread(hls.dam_bao_doan, key, a, b, chu_chung)
        except KhongCoFile as ex:
            raise LoiApi(404, "khong_co_file", "File mẫu không còn.") from ex
        except watermark_av.LoiMedia as ex:
            log.warning("Khong cat duoc HLS cho %s: %s", key, ex)
            raise LoiApi(409, "media_hong", "File hỏng, không phát được.") from ex
        finally:
            _khoa_cat.pop((key, a, b), None)
        return await asyncio.to_thread(_chi_muc_da_luu, key, a, b)


async def phuc_vu_danh_sach(token: str) -> Response:
    q = _quyen_hls(token, cho_gia_han=False)
    cm = await _chi_muc(q)
    return Response(hls.danh_sach_phat(cm), media_type=LOAI_DANH_SACH, headers=HEADER_CHONG_LUU)


async def phuc_vu_mang_chung(token: str, so: int) -> Response:
    q = _quyen_hls(token, cho_gia_han=True)
    cm = await _chi_muc(q)
    # Chi mang GIUA: mang dau / cuoi ban sach khong bao gio phat cho nguoi xem.
    if so <= 0 or so >= len(cm.mang) - 1:
        raise LoiApi(404, "khong_co_doan", "Không có đoạn này.")
    khoa = hls.khoa_mang(q.storage_key, float(q.bat_dau), float(q.ket_thuc), cm.mang[so].ten)
    try:
        tt = await asyncio.to_thread(kho().thong_tin, khoa)
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_doan", "Không có đoạn này.") from ex
    headers = dict(HEADER_CHONG_LUU)
    headers["Content-Length"] = str(tt.size)
    return StreamingResponse(kho().luong(khoa, 0, tt.size), media_type=hls.LOAI_MANG, headers=headers)


async def phuc_vu_mang_rieng(token: str, ten: str, request: Request) -> Response:
    q = _quyen_hls(token, cho_gia_han=True)
    cm = await _chi_muc(q)
    i = hls.chi_so_mang_rieng(cm, ten)
    if i is None:
        raise LoiApi(404, "khong_co_doan", "Không có đoạn này.")
    key, a, b = q.storage_key, float(q.bat_dau), float(q.ket_thuc)
    ten_mang = cm.mang[i].ten
    chu = chu_cua(q)

    def tai_sach(dich: Path) -> None:
        kho().tai_ve(hls.khoa_mang(key, a, b, ten_mang), str(dich))

    try:
        # Mang sach dung chung moi nguoi xem (tai tu MinIO mot lan), mang co chu rieng tung (nguoi xem, chu).
        sach = await bo_dem().lay(BoDem.khoa(key, f"{a}-{b}", ten_mang, "sach", hls.PHIEN_BAN), ".ts", tai_sach)
        duong_dan = await bo_dem().lay(
            BoDem.khoa(key, f"{a}-{b}", ten_mang, hashlib.sha256(chu.encode()).hexdigest(), hls.PHIEN_BAN), ".ts",
            lambda dich: hls.tao_mang_rieng(sach, dich, chu))
    except KhongCoFile as ex:
        raise LoiApi(404, "khong_co_doan", "Không có đoạn này.") from ex
    except watermark_av.LoiMedia as ex:
        log.warning("Khong tao duoc mang rieng cho %s: %s", key, ex)
        raise LoiApi(409, "media_hong", "File hỏng, không phát được.") from ex
    return _phat_tep(duong_dan, hls.LOAI_MANG, request)


# =====================================================================
# Phat file
# =====================================================================


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
        raise LoiApi(404, "khong_co_file", "File mẫu không còn.") from ex

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
