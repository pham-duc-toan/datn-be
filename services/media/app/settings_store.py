"""Setting dong (admin-svc quan ly) — ban chep tu quality-svc (cung khuon voi Crowd.Settings ben C#).

Kieu, mac dinh, gioi han: doc tu shared/settings/catalog.json (test C# xuat tu SettingCatalog).
Gia tri admin dat: settings_listener.py nhan qua RabbitMQ va ghi vao bo nho o day (media-svc khong
co DB). Moi thao tac doc gia tri MOI lan dung.
"""

import json
import logging
import math
import threading
from pathlib import Path
from typing import Any

from app.config import settings

log = logging.getLogger(__name__)

# services/media/app/settings_store.py → goc repo la parents[3].
CATALOG_MAC_DINH = Path(__file__).resolve().parents[3] / "shared" / "settings" / "catalog.json"

# Khoa media-svc dung (ten giong SettingKeys ben C#).
WATERMARK_ENABLED = "media.watermark_enabled"
WATERMARK_STRENGTH = "media.watermark_strength"
CONSUMERS_RECONNECT_DELAY = "consumers.reconnect_delay"


def _nap_catalog() -> dict[str, dict[str, Any]]:
    duong_dan = Path(settings().settings_catalog_path) if settings().settings_catalog_path else CATALOG_MAC_DINH
    ds = json.loads(duong_dan.read_text(encoding="utf-8"))
    return {d["key"]: d for d in ds}


_catalog: dict[str, dict[str, Any]] | None = None
_gia_tri: dict[str, tuple[Any, int]] = {}
_khoa = threading.Lock()


def catalog() -> dict[str, dict[str, Any]]:
    global _catalog
    if _catalog is None:
        _catalog = _nap_catalog()
    return _catalog


def kiem_gia_tri(key: str, gia_tri: Any) -> str | None:
    """None = hop le; nguoc lai la ly do sai. Cung luat voi SettingDefinition.KiemGiaTri (C#)."""
    d = catalog().get(key)
    if d is None:
        return "khoa khong co trong catalog"

    kieu = d["type"]
    if kieu == "bool":
        return None if isinstance(gia_tri, bool) else "phai la true hoac false"
    if kieu == "text":
        if not isinstance(gia_tri, str):
            return "phai la chuoi"
        if d["max"] is not None and len(gia_tri) > d["max"]:
            return "qua dai"
        if d.get("choices") is not None and gia_tri not in d["choices"]:
            return "phai la mot trong " + ", ".join(d["choices"])
        return None

    if isinstance(gia_tri, bool) or not isinstance(gia_tri, (int, float)) or not math.isfinite(gia_tri):
        return "phai la so"
    if kieu in ("int", "long") and float(gia_tri) != math.floor(gia_tri):
        return "phai la so nguyen"
    if d["min"] is not None and gia_tri < d["min"]:
        return "nho hon toi thieu"
    if d["max"] is not None and gia_tri > d["max"]:
        return "lon hon toi da"
    return None


def ap_dung(key: str, gia_tri_json: str, version: int) -> bool:
    """Ghi vao bo nho neu version MOI hon va gia tri hop le. Tra ve co ghi hay khong."""
    if key not in catalog():
        log.warning("Setting %s khong co trong catalog cua media-svc — bo qua", key)
        return False

    gia_tri = json.loads(gia_tri_json)
    loi = kiem_gia_tri(key, gia_tri)
    if loi is not None:
        log.warning("Setting %s = %s khong hop le (%s) — giu gia tri cu", key, gia_tri_json, loi)
        return False

    with _khoa:
        cu = _gia_tri.get(key)
        if cu is not None and cu[1] >= version:
            return False
        _gia_tri[key] = (gia_tri, version)
    return True


def _lay(key: str) -> Any:
    cu = _gia_tri.get(key)
    if cu is not None:
        return cu[0]
    d = catalog().get(key)
    if d is None:
        raise KeyError("Setting " + key + " khong co trong catalog")
    return d["default"]


def dung_sai(key: str) -> bool:
    return bool(_lay(key))


def so_nguyen(key: str) -> int:
    return int(_lay(key))


def so_thuc(key: str) -> float:
    return float(_lay(key))


def giay(key: str) -> float:
    """Setting kieu durationSeconds."""
    return float(_lay(key))


def xoa_het_cho_test() -> None:
    with _khoa:
        _gia_tri.clear()
