"""Goi project-svc (noi bo, X-Internal-Key) lay dieu khoan luc publish cua du an.

Dung khi quality-svc nhan nhan cua mot du an ma CHUA co ban sao (du an publish truoc khi quality-svc
ton tai, hoac event project.published bi mat) — thay vi bo qua dong thuan vinh vien.
"""

import logging
import uuid
from datetime import datetime

import httpx

from app.config import settings
from app.contracts import ProjectPublished

log = logging.getLogger(__name__)


class ChuaCauHinh(Exception):
    """Thieu QUALITY_PROJECT_URL / QUALITY_INTERNAL_API_KEY — khong bu duoc."""


async def lay_dieu_khoan(project_id: uuid.UUID) -> tuple[ProjectPublished, datetime] | None:
    """(dieu khoan, luc publish); None = du an chua publish (404). Loi mang / 5xx → nem (consumer thu lai)."""
    cfg = settings()
    if not cfg.project_url or not cfg.internal_api_key:
        raise ChuaCauHinh()
    async with httpx.AsyncClient(base_url=cfg.project_url, timeout=10.0) as http:
        r = await http.get(f"/internal/projects/{project_id}/published-terms",
                           headers={"X-Internal-Key": cfg.internal_api_key})
    if r.status_code == 404:
        return None
    r.raise_for_status()
    body = r.json()
    return ProjectPublished.model_validate(body["terms"]), datetime.fromisoformat(body["publishedAt"])
