"""API noi bo cua ml-svc. KHONG qua gateway: chi service trong mang noi bo goi, kem X-Internal-Key.

POST /internal/embeddings {"texts": [...]} → {"model", "dim", "vectors": [[...]], "cached": n}
    Vector chuan hoa L2 (tich vo huong = cosine), cung thu tu voi texts. Cau da nhung (cung mo hinh,
    cung noi dung sau khi bo khoang trang thua) lay tu bo dem ml_db, khong nhung lai.
GET /internal/model → mo hinh dang dung, da nap xong chua.

Sai / thieu khoa → 404 (giong endpoint noi bo cua cac service C#: khong lo la endpoint ton tai).
"""

import asyncio
import hmac

from fastapi import APIRouter, Header, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from app.config import settings
from app.embedder import khoa_bam


class YeuCauNhung(BaseModel):
    texts: list[str]


router = APIRouter()


def _loi(ma: int, code: str, chi_tiet: str) -> JSONResponse:
    return JSONResponse(status_code=ma, content={"title": code, "status": ma, "detail": chi_tiet, "code": code})


def _dung_khoa(khoa: str | None) -> bool:
    mong = settings().internal_api_key
    return bool(mong) and khoa is not None and hmac.compare_digest(khoa.encode(), mong.encode())


@router.get("/internal/model")
async def mo_hinh(request: Request, x_internal_key: str | None = Header(default=None)):
    if not _dung_khoa(x_internal_key):
        return _loi(404, "khong_tim_thay", "Not Found")
    mh = request.app.state.mo_hinh
    return {"model": mh.ten, "ready": mh.san_sang()}


@router.post("/internal/embeddings")
async def nhung(body: YeuCauNhung, request: Request, x_internal_key: str | None = Header(default=None)):
    if not _dung_khoa(x_internal_key):
        return _loi(404, "khong_tim_thay", "Not Found")
    cfg = settings()
    if not body.texts:
        return _loi(400, "thieu_van_ban", "Can it nhat mot cau.")
    if len(body.texts) > cfg.max_texts:
        return _loi(400, "qua_nhieu_van_ban", f"Toi da {cfg.max_texts} cau mot lan.")
    for i, t in enumerate(body.texts):
        if not t.strip():
            return _loi(400, "van_ban_rong", f"Cau thu {i} rong.")
        if len(t) > cfg.max_chars:
            return _loi(400, "van_ban_qua_dai", f"Cau thu {i} dai hon {cfg.max_chars} ky tu.")

    mh = request.app.state.mo_hinh
    kho = request.app.state.kho
    khoa = [khoa_bam(mh.ten, t) for t in body.texts]
    co_san = await kho.lay(list(set(khoa)))

    # Moi noi dung chua co nhung MOT lan (nhieu nguoi chep y het, hoac gui lai).
    can: dict[bytes, str] = {}
    for k, t in zip(khoa, body.texts):
        if k not in co_san and k not in can:
            can[k] = t
    if can:
        moi = await asyncio.to_thread(mh.nhung, list(can.values()))
        ban_ghi = dict(zip(can.keys(), moi))
        await kho.luu(mh.ten, ban_ghi)
        co_san.update(ban_ghi)

    vectors = [co_san[k] for k in khoa]
    return {"model": mh.ten, "dim": len(vectors[0]), "vectors": vectors, "cached": len(body.texts) - len(can)}
