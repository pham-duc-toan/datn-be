import datetime

from google.protobuf import timestamp_pb2 as _timestamp_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class ProjectRequest(_message.Message):
    __slots__ = ("project_id",)
    PROJECT_ID_FIELD_NUMBER: _ClassVar[int]
    project_id: str
    def __init__(self, project_id: _Optional[str] = ...) -> None: ...

class ProjectInfo(_message.Message):
    __slots__ = ("project_id", "owner_id", "modality", "label_schema_json", "pass_threshold_percent", "slots", "fill_slots_min_percent", "question_count", "status", "published_at", "finish_reason", "collection_finished_at", "excluded_sample_ids")
    PROJECT_ID_FIELD_NUMBER: _ClassVar[int]
    OWNER_ID_FIELD_NUMBER: _ClassVar[int]
    MODALITY_FIELD_NUMBER: _ClassVar[int]
    LABEL_SCHEMA_JSON_FIELD_NUMBER: _ClassVar[int]
    PASS_THRESHOLD_PERCENT_FIELD_NUMBER: _ClassVar[int]
    SLOTS_FIELD_NUMBER: _ClassVar[int]
    FILL_SLOTS_MIN_PERCENT_FIELD_NUMBER: _ClassVar[int]
    QUESTION_COUNT_FIELD_NUMBER: _ClassVar[int]
    STATUS_FIELD_NUMBER: _ClassVar[int]
    PUBLISHED_AT_FIELD_NUMBER: _ClassVar[int]
    FINISH_REASON_FIELD_NUMBER: _ClassVar[int]
    COLLECTION_FINISHED_AT_FIELD_NUMBER: _ClassVar[int]
    EXCLUDED_SAMPLE_IDS_FIELD_NUMBER: _ClassVar[int]
    project_id: str
    owner_id: str
    modality: str
    label_schema_json: str
    pass_threshold_percent: int
    slots: int
    fill_slots_min_percent: int
    question_count: int
    status: str
    published_at: _timestamp_pb2.Timestamp
    finish_reason: str
    collection_finished_at: _timestamp_pb2.Timestamp
    excluded_sample_ids: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, project_id: _Optional[str] = ..., owner_id: _Optional[str] = ..., modality: _Optional[str] = ..., label_schema_json: _Optional[str] = ..., pass_threshold_percent: _Optional[int] = ..., slots: _Optional[int] = ..., fill_slots_min_percent: _Optional[int] = ..., question_count: _Optional[int] = ..., status: _Optional[str] = ..., published_at: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., finish_reason: _Optional[str] = ..., collection_finished_at: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., excluded_sample_ids: _Optional[_Iterable[str]] = ...) -> None: ...

class AttemptInfo(_message.Message):
    __slots__ = ("attempt_id", "labeler_id", "sample_ids", "label_count", "answered_total", "gold_total", "gold_correct", "started_at", "submitted_at", "graded", "comparable_count", "matched_count", "agreement_percent", "passed", "rewarded", "appeal_state", "client_ip", "device_id")
    ATTEMPT_ID_FIELD_NUMBER: _ClassVar[int]
    LABELER_ID_FIELD_NUMBER: _ClassVar[int]
    SAMPLE_IDS_FIELD_NUMBER: _ClassVar[int]
    LABEL_COUNT_FIELD_NUMBER: _ClassVar[int]
    ANSWERED_TOTAL_FIELD_NUMBER: _ClassVar[int]
    GOLD_TOTAL_FIELD_NUMBER: _ClassVar[int]
    GOLD_CORRECT_FIELD_NUMBER: _ClassVar[int]
    STARTED_AT_FIELD_NUMBER: _ClassVar[int]
    SUBMITTED_AT_FIELD_NUMBER: _ClassVar[int]
    GRADED_FIELD_NUMBER: _ClassVar[int]
    COMPARABLE_COUNT_FIELD_NUMBER: _ClassVar[int]
    MATCHED_COUNT_FIELD_NUMBER: _ClassVar[int]
    AGREEMENT_PERCENT_FIELD_NUMBER: _ClassVar[int]
    PASSED_FIELD_NUMBER: _ClassVar[int]
    REWARDED_FIELD_NUMBER: _ClassVar[int]
    APPEAL_STATE_FIELD_NUMBER: _ClassVar[int]
    CLIENT_IP_FIELD_NUMBER: _ClassVar[int]
    DEVICE_ID_FIELD_NUMBER: _ClassVar[int]
    attempt_id: str
    labeler_id: str
    sample_ids: _containers.RepeatedScalarFieldContainer[str]
    label_count: int
    answered_total: int
    gold_total: int
    gold_correct: int
    started_at: _timestamp_pb2.Timestamp
    submitted_at: _timestamp_pb2.Timestamp
    graded: bool
    comparable_count: int
    matched_count: int
    agreement_percent: int
    passed: bool
    rewarded: bool
    appeal_state: str
    client_ip: str
    device_id: str
    def __init__(self, attempt_id: _Optional[str] = ..., labeler_id: _Optional[str] = ..., sample_ids: _Optional[_Iterable[str]] = ..., label_count: _Optional[int] = ..., answered_total: _Optional[int] = ..., gold_total: _Optional[int] = ..., gold_correct: _Optional[int] = ..., started_at: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., submitted_at: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., graded: bool = ..., comparable_count: _Optional[int] = ..., matched_count: _Optional[int] = ..., agreement_percent: _Optional[int] = ..., passed: bool = ..., rewarded: bool = ..., appeal_state: _Optional[str] = ..., client_ip: _Optional[str] = ..., device_id: _Optional[str] = ...) -> None: ...

class AttemptList(_message.Message):
    __slots__ = ("attempts",)
    ATTEMPTS_FIELD_NUMBER: _ClassVar[int]
    attempts: _containers.RepeatedCompositeFieldContainer[AttemptInfo]
    def __init__(self, attempts: _Optional[_Iterable[_Union[AttemptInfo, _Mapping]]] = ...) -> None: ...

class LabelsRequest(_message.Message):
    __slots__ = ("project_id", "attempt_ids")
    PROJECT_ID_FIELD_NUMBER: _ClassVar[int]
    ATTEMPT_IDS_FIELD_NUMBER: _ClassVar[int]
    project_id: str
    attempt_ids: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, project_id: _Optional[str] = ..., attempt_ids: _Optional[_Iterable[str]] = ...) -> None: ...

class LabelRow(_message.Message):
    __slots__ = ("attempt_id", "sample_id", "labeler_id", "data_json", "sample_metadata_json", "submitted_at")
    ATTEMPT_ID_FIELD_NUMBER: _ClassVar[int]
    SAMPLE_ID_FIELD_NUMBER: _ClassVar[int]
    LABELER_ID_FIELD_NUMBER: _ClassVar[int]
    DATA_JSON_FIELD_NUMBER: _ClassVar[int]
    SAMPLE_METADATA_JSON_FIELD_NUMBER: _ClassVar[int]
    SUBMITTED_AT_FIELD_NUMBER: _ClassVar[int]
    attempt_id: str
    sample_id: str
    labeler_id: str
    data_json: str
    sample_metadata_json: str
    submitted_at: _timestamp_pb2.Timestamp
    def __init__(self, attempt_id: _Optional[str] = ..., sample_id: _Optional[str] = ..., labeler_id: _Optional[str] = ..., data_json: _Optional[str] = ..., sample_metadata_json: _Optional[str] = ..., submitted_at: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class SharedSubmittersRequest(_message.Message):
    __slots__ = ("client_ip", "device_id", "since")
    CLIENT_IP_FIELD_NUMBER: _ClassVar[int]
    DEVICE_ID_FIELD_NUMBER: _ClassVar[int]
    SINCE_FIELD_NUMBER: _ClassVar[int]
    client_ip: str
    device_id: str
    since: _timestamp_pb2.Timestamp
    def __init__(self, client_ip: _Optional[str] = ..., device_id: _Optional[str] = ..., since: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ...) -> None: ...

class SharedSubmittersResponse(_message.Message):
    __slots__ = ("labeler_ids",)
    LABELER_IDS_FIELD_NUMBER: _ClassVar[int]
    labeler_ids: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, labeler_ids: _Optional[_Iterable[str]] = ...) -> None: ...

class LabelerStatsRequest(_message.Message):
    __slots__ = ("labeler_ids",)
    LABELER_IDS_FIELD_NUMBER: _ClassVar[int]
    labeler_ids: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, labeler_ids: _Optional[_Iterable[str]] = ...) -> None: ...

class LabelerStats(_message.Message):
    __slots__ = ("labeler_id", "submissions", "median_seconds_per_question")
    LABELER_ID_FIELD_NUMBER: _ClassVar[int]
    SUBMISSIONS_FIELD_NUMBER: _ClassVar[int]
    MEDIAN_SECONDS_PER_QUESTION_FIELD_NUMBER: _ClassVar[int]
    labeler_id: str
    submissions: int
    median_seconds_per_question: float
    def __init__(self, labeler_id: _Optional[str] = ..., submissions: _Optional[int] = ..., median_seconds_per_question: _Optional[float] = ...) -> None: ...

class LabelerStatsResponse(_message.Message):
    __slots__ = ("labelers",)
    LABELERS_FIELD_NUMBER: _ClassVar[int]
    labelers: _containers.RepeatedCompositeFieldContainer[LabelerStats]
    def __init__(self, labelers: _Optional[_Iterable[_Union[LabelerStats, _Mapping]]] = ...) -> None: ...

class LabelerEvidenceRequest(_message.Message):
    __slots__ = ("labeler_ids",)
    LABELER_IDS_FIELD_NUMBER: _ClassVar[int]
    labeler_ids: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, labeler_ids: _Optional[_Iterable[str]] = ...) -> None: ...

class LabelerEvidence(_message.Message):
    __slots__ = ("labeler_id", "graded_attempts", "gold_total", "gold_correct", "comparable_total", "matched_total")
    LABELER_ID_FIELD_NUMBER: _ClassVar[int]
    GRADED_ATTEMPTS_FIELD_NUMBER: _ClassVar[int]
    GOLD_TOTAL_FIELD_NUMBER: _ClassVar[int]
    GOLD_CORRECT_FIELD_NUMBER: _ClassVar[int]
    COMPARABLE_TOTAL_FIELD_NUMBER: _ClassVar[int]
    MATCHED_TOTAL_FIELD_NUMBER: _ClassVar[int]
    labeler_id: str
    graded_attempts: int
    gold_total: int
    gold_correct: int
    comparable_total: int
    matched_total: int
    def __init__(self, labeler_id: _Optional[str] = ..., graded_attempts: _Optional[int] = ..., gold_total: _Optional[int] = ..., gold_correct: _Optional[int] = ..., comparable_total: _Optional[int] = ..., matched_total: _Optional[int] = ...) -> None: ...

class LabelerEvidenceResponse(_message.Message):
    __slots__ = ("labelers",)
    LABELERS_FIELD_NUMBER: _ClassVar[int]
    labelers: _containers.RepeatedCompositeFieldContainer[LabelerEvidence]
    def __init__(self, labelers: _Optional[_Iterable[_Union[LabelerEvidence, _Mapping]]] = ...) -> None: ...
