"""Payload cac event media-svc nhan va phat: video vua nap (samples.ingested → video.prepared).

PHAI khop tung truong voi record C# trong shared/contracts (anh chup: contracts/payloads.snapshot.txt).
Ben C# them truong ma ben nay chua khai thi message bi tu choi → DLQ: lech hop dong phai lo ra ngay.
"""

import uuid
from typing import Any

from app.messaging.envelope import HopDong, Payload

# =====================================================================
# SETTING (admin-svc)
# =====================================================================


# Kien truc 3.32: media-svc khong nghe setting.changed / settings.snapshot nua — doc view v_settings (settings_sync.py).


# =====================================================================
# DU LIEU (project-svc) — cat san HLS video vua nap (ban_chuan.py)
# =====================================================================


class IngestedSample(HopDong):
    sample_id: uuid.UUID
    modality: str
    storage_key: str | None
    content: Any | None
    metadata: Any


class SamplesIngested(Payload):
    EVENT_TYPE = "samples.ingested"
    VERSION = 2

    project_id: uuid.UUID
    samples: list[IngestedSample]
    # Chu watermark noi in vao phan giua video (dung chung moi nguoi xem) — project-svc dung san.
    watermark_text: str


class VideoPrepared(Payload):
    EVENT_TYPE = "video.prepared"

    project_id: uuid.UUID
    storage_key: str
    succeeded: bool
    error: str | None
