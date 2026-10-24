"""Payload cac event media-svc nhan va phat: dong bo setting, video vua nap (dataset.ingested → video.prepared).

PHAI khop tung truong voi record C# trong shared/contracts (anh chup: contracts/payloads.snapshot.txt).
Ben C# them truong ma ben nay chua khai thi message bi tu choi → DLQ: lech hop dong phai lo ra ngay.
"""

import uuid
from datetime import datetime
from typing import Any

from app.messaging.envelope import HopDong, Payload

# =====================================================================
# SETTING (admin-svc)
# =====================================================================


class SettingChanged(Payload):
    EVENT_TYPE = "setting.changed"

    key: str
    value: Any
    setting_version: int
    changed_by: uuid.UUID | None
    changed_at: datetime


class SettingSnapshotItem(HopDong):
    key: str
    value: Any
    setting_version: int


class SettingsSnapshot(Payload):
    EVENT_TYPE = "settings.snapshot"

    items: list[SettingSnapshotItem]


class SettingsSnapshotRequested(Payload):
    EVENT_TYPE = "settings.snapshot_requested"

    service: str


# =====================================================================
# DU LIEU (project-svc) — chuan hoa san video vua nap (ban_chuan.py)
# =====================================================================


class IngestedSample(HopDong):
    sample_id: uuid.UUID
    modality: str
    storage_key: str | None
    content: Any | None
    metadata: Any


class DatasetIngested(Payload):
    EVENT_TYPE = "dataset.ingested"

    project_id: uuid.UUID
    dataset_id: uuid.UUID
    batch_index: int
    batch_count: int
    samples: list[IngestedSample]


class VideoPrepared(Payload):
    EVENT_TYPE = "video.prepared"

    project_id: uuid.UUID
    storage_key: str
    succeeded: bool
    error: str | None
