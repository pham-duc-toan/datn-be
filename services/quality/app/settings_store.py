"""Setting dong (admin-svc quan ly) — ban Python cua Crowd.Settings.SettingsStore.

Kieu, mac dinh, gioi han: doc tu shared/settings/catalog.json — file do test C#
(SettingCatalogTests) xuat tu SettingCatalog, nen hai ben khong lech nhau.
Gia tri admin dat: nam trong bang settings_replica (settings_sync.py ghi), nap
vao bo nho o day; doc setting KHONG cham DB.

Moi thao tac doc gia tri MOI lan dung (khong giu lau) — admin doi la thao tac
tiep theo dung gia tri moi.
"""

import json
import logging
import math
import threading
from pathlib import Path
from typing import Any

from app.config import settings

log = logging.getLogger(__name__)

# services/quality/app/settings_store.py → goc repo la parents[3].
CATALOG_MAC_DINH = Path(__file__).resolve().parents[3] / "shared" / "settings" / "catalog.json"

# Khoa quality-svc dung (ten giong SettingKeys ben C#).
DS_INTERVAL = "quality.ds_interval"
DS_MIN_LABELS = "quality.ds_min_labels"
REPUTATION_GOLD_WEIGHT = "quality.reputation_gold_weight"
REPUTATION_PRIOR = "quality.reputation_prior"
REPUTATION_PRIOR_STRENGTH = "quality.reputation_prior_strength"
OUTBOX_POLL_INTERVAL = "outbox.poll_interval"
OUTBOX_BATCH_SIZE = "outbox.batch_size"
OUTBOX_PUBLISH_TIMEOUT = "outbox.publish_timeout"
OUTBOX_RETRY_MAX_DELAY = "outbox.retry_max_delay"
CONSUMERS_PREFETCH_COUNT = "consumers.prefetch_count"
SETTINGS_RELOAD_INTERVAL = "settings.reload_interval"


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
        log.warning("Setting %s khong co trong catalog cua quality-svc — bo qua", key)
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


def phien_ban(key: str) -> int:
    cu = _gia_tri.get(key)
    return 0 if cu is None else cu[1]


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
