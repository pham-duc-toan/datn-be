"""Payload cac event quality-svc nhan va phat.

PHAI khop tung truong voi record C# trong shared/contracts (anh chup:
contracts/payloads.snapshot.txt). Ben C# them truong ma ben nay chua khai thi
message bi tu choi → DLQ: dung y do (VD-D-12), lech hop dong phai lo ra ngay.
"""

import uuid
from datetime import datetime
from typing import Any, Literal

from app.messaging.envelope import HopDong, Payload


class LabelPayload(HopDong):
    """Nhan dinh dang chung (Crowd.Labeling): taskType = loai du lieu, data theo ten cong cu."""

    task_type: str
    schema_version: int
    data: dict[str, Any]


# =====================================================================
# NHAN VAO
# =====================================================================


class ProjectPublished(Payload):
    EVENT_TYPE = "project.published"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    modality: str
    label_schema: dict[str, Any]
    unit_price_vnd: int
    platform_fee_vnd: int
    redundancy: int
    max_redundancy: int
    gold_check_percent: int
    deadline: datetime
    allow_professional: bool
    allow_link_gateway: bool
    allow_collaborative: bool
    is_private: bool
    min_level: int | None
    min_reputation: int | None
    require_entrance_test: bool
    sample_count: int
    # Noi dung nhay cam (muc 2.9) — khong bat buoc, ban tin cu khong co.
    sensitive_content: bool | None = None


class ProjectCompleted(Payload):
    """Du an xong — moc bat dau dem project.closed_data_retention."""

    EVENT_TYPE = "project.completed"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    approved_annotation_count: int | None


class ProjectCancelled(Payload):
    EVENT_TYPE = "project.cancelled"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    reason: str | None
    was_escrowed: bool
    approved_annotation_count: int | None


class AnnotationSubmitted(Payload):
    EVENT_TYPE = "annotation.submitted"

    annotation_id: uuid.UUID
    task_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    labeler_id: uuid.UUID | None
    label_payload: LabelPayload
    # Metadata cua mau (width / height...) — de so do lech diem moc / duong gap khuc khi gop nhan.
    # Co mac dinh: message do ban truoc phat (chua co truong nay) van doc duoc luc nang cap.
    sample_metadata: Any = None
    source: Literal["professional", "linkGateway", "collaborative"]


class AssignmentSubmitted(Payload):
    """task-svc → annotation-svc (va quality-svc nghe them de chong gian lan, FM-07): co LeasedAt /
    SubmittedAt de do toc do lam bai, ClientIp / DeviceId de phat hien nhieu tai khoan chung may."""

    EVENT_TYPE = "assignment.submitted"

    assignment_id: uuid.UUID
    task_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    storage_key: str | None
    sample_content: Any | None
    sample_metadata: Any
    labeler_id: uuid.UUID
    label_payload: LabelPayload
    leased_at: datetime
    submitted_at: datetime
    client_ip: str | None
    device_id: str | None


class TaskRedundancyReached(Payload):
    EVENT_TYPE = "task.redundancy_reached"

    task_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    redundancy: int


class GoldAnswered(Payload):
    EVENT_TYPE = "gold.answered"

    assignment_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    labeler_id: uuid.UUID
    correct: bool
    answered_at: datetime


# =====================================================================
# PHAT RA
# =====================================================================


class RedundancyIncreaseRequested(Payload):
    EVENT_TYPE = "redundancy.increase_requested"

    task_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    new_redundancy: int
    reason: str


class ConsensusVote(HopDong):
    annotation_id: uuid.UUID
    labeler_id: uuid.UUID
    agrees: bool | None


class ConsensusReached(Payload):
    EVENT_TYPE = "consensus.reached"

    task_id: uuid.UUID
    project_id: uuid.UUID
    sample_id: uuid.UUID
    status: Literal["agreed", "disputed", "notApplicable"]
    final: dict[str, Any] | None
    votes: list[ConsensusVote]


class FraudCaseConfirmed(Payload):
    """Admin xac nhan ho so gian lan (FM-07); block_accounts → identity khoa cac tai khoan."""

    EVENT_TYPE = "fraud.case_confirmed"

    case_id: uuid.UUID
    user_ids: list[uuid.UUID]
    reason: str
    block_accounts: bool


class ReputationChanged(Payload):
    """Chu so huu diem uy tin: quality-svc (identity-svc chua luu diem nay)."""

    EVENT_TYPE = "reputation.changed"

    user_id: uuid.UUID
    reputation: int


# =====================================================================
# SETTING (admin-svc)
# =====================================================================


class SettingChanged(Payload):
    EVENT_TYPE = "setting.changed"

    key: str
    # JSON vo huong (so, true/false, chuoi) — kieu nam o shared/settings/catalog.json.
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
