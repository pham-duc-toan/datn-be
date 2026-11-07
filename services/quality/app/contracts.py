"""Payload cac event quality-svc nhan va phat.

PHAI khop tung truong voi record C# trong shared/contracts (anh chup:
contracts/payloads.snapshot.txt). Ben C# them truong ma ben nay chua khai thi
message bi tu choi → DLQ: dung y do (VD-D-12), lech hop dong phai lo ra ngay.
"""

import uuid
from datetime import datetime
from typing import Any

from app.messaging.envelope import HopDong, Payload


# =====================================================================
# NHAN VAO
# =====================================================================


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


class AttemptSubmitted(Payload):
    """v2 (kien truc 3.22): labeler nop CA BAI — chi la TIN HIEU. Cau tra loi, de bai, IP / thiet bi quality doc
    qua gRPC tu project-svc luc xet (viec_cho.py), khong di trong event."""

    EVENT_TYPE = "attempt.submitted"
    VERSION = 2

    attempt_id: uuid.UUID
    project_id: uuid.UUID
    labeler_id: uuid.UUID
    answered_count: int
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
    """project-svc dong thu bai (du suat / het han / ket thuc som). quality chi dung no lam yeu cau cham: de bai,
    bai nop, cau bi loai doc lai qua gRPC (cung nguon voi lan cham lai cua worker)."""

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
    VERSION = 2  # v2: + owner_id

    project_id: uuid.UUID
    owner_id: uuid.UUID
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
