"""Goi ml-svc (noi bo, X-Internal-Key) nhung cau tra loi mo thanh vector (kien truc 3.19).

ml-svc sap / cham khong lam hong luong cham: worker phan_tich_mo thu lai vong sau; qua
quality.freetext_analysis_timeout thi du an duoc cham bang so chu.
"""

import httpx

from app.config import settings

# Gioi han cua ml-svc (ML_MAX_TEXTS / ML_MAX_CHARS).
TOI_DA_CAU = 256
TOI_DA_KY_TU = 5000


class ChuaCauHinh(Exception):
    """Thieu QUALITY_ML_URL / QUALITY_INTERNAL_API_KEY."""


def co_cau_hinh() -> bool:
    cfg = settings()
    return bool(cfg.ml_url and cfg.internal_api_key)


async def nhung(cau: list[str]) -> tuple[str, list[list[float]]]:
    """(ten mo hinh, vector theo dung thu tu). Loi mang / 4xx / 5xx → nem."""
    cfg = settings()
    if not co_cau_hinh():
        raise ChuaCauHinh()
    if len(cau) > TOI_DA_CAU:
        raise ValueError(f"Toi da {TOI_DA_CAU} cau mot lan.")
    async with httpx.AsyncClient(base_url=cfg.ml_url, timeout=120.0) as http:
        r = await http.post("/internal/embeddings", json={"texts": [c[:TOI_DA_KY_TU] for c in cau]},
                            headers={"X-Internal-Key": cfg.internal_api_key})
    r.raise_for_status()
    j = r.json()
    return j["model"], j["vectors"]
