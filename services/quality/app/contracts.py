"""Payload cac event quality-svc nhan va phat.

PHAI khop tung truong voi record C# trong shared/contracts (anh chup:
contracts/payloads.snapshot.txt). Ben C# them truong ma ben nay chua khai thi
message bi tu choi → DLQ: dung y do (VD-D-12), lech hop dong phai lo ra ngay.
"""

import uuid
from datetime import datetime
from typing import Any

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
    """v2 — du an lam trung tam (docs 3.18): thuong / suat / nguong dat cho CA du an."""

    EVENT_TYPE = "project.published"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    modality: str
    label_schema: dict[str, Any]
    reward_vnd: int
    platform_fee_vnd: int
    slots: int
    pass_threshold_percent: int
    # Lap suat trong — khong bat buoc (ban tin cu / null = khong lap).
    fill_slots_min_percent: int | None = None
    question_count: int
    deadline: datetime
    allow_professional: bool
    allow_link_gateway: bool
    allow_collaborative: bool
    gate_unit_price_vnd: int | None = None
    gate_platform_fee_vnd: int | None = None
    gate_budget_vnd: int | None = None
    is_private: bool
    min_level: int | None
    min_reputation: int | None
    require_entrance_test: bool
    sample_count: int
    # Noi dung nhay cam (muc 2.9) — khong bat buoc.
    sensitive_content: bool | None = None


class ProjectCompleted(Payload):
    """Du an xong — moc bat dau dem project.closed_data_retention."""

    EVENT_TYPE = "project.completed"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    passed_attempt_count: int


class ProjectCancelled(Payload):
    EVENT_TYPE = "project.cancelled"

    project_id: uuid.UUID
    owner_id: uuid.UUID
    reason: str | None
    was_escrowed: bool


class AttemptAnswer(HopDong):
    """Mot cau THAT trong bai nop; label_payload None = labeler bo trong cau nay."""

    sample_id: uuid.UUID
    storage_key: str | None = None
    sample_content: Any | None = None
    sample_metadata: Any
    label_payload: LabelPayload | None = None


class AttemptSubmitted(Payload):
    """task-svc: labeler nop CA BAI. Cau vang chi mang so dung / tong (khong lo cau nao la vang).
    StartedAt / SubmittedAt, ClientIp / DeviceId de chong gian lan (FM-07)."""

    EVENT_TYPE = "attempt.submitted"

    attempt_id: uuid.UUID
    project_id: uuid.UUID
    labeler_id: uuid.UUID
    answers: list[AttemptAnswer]
    gold_total: int
    gold_correct: int
    started_at: datetime
    submitted_at: datetime
    client_ip: str | None = None
    device_id: str | None = None


class SubmittedAttemptRef(HopDong):
    attempt_id: uuid.UUID
    labeler_id: uuid.UUID
    gold_total: int
    gold_correct: int


class ProjectCollectionFinished(Payload):
    """task-svc dong thu bai (du suat / het han / ket thuc som): danh sach bai PHAI cham + cau bi loai."""

    EVENT_TYPE = "project.collection_finished"

    project_id: uuid.UUID
    reason: str
    attempts: list[SubmittedAttemptRef]
    excluded_sample_ids: list[uuid.UUID]
    finished_at: datetime


# =====================================================================
# PHAT RA
# =====================================================================


class GradedAttempt(HopDong):
    attempt_id: uuid.UUID
    labeler_id: uuid.UUID
    comparable_count: int
    matched_count: int
    agreement_percent: int
    gold_total: int
    gold_correct: int
    passed: bool
    rewarded: bool
    answered_count: int


class ProjectGraded(Payload):
    """Cham xong ca du an: bai dat (ti le cau giong da so >= nguong VA dung het cau vang) duoc thuong."""

    EVENT_TYPE = "project.graded"

    project_id: uuid.UUID
    pass_threshold_percent: int
    majority_used: bool
    attempts: list[GradedAttempt]
    graded_at: datetime


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
