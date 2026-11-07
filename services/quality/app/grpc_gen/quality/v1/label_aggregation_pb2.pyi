from google.protobuf.internal import containers as _containers
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class AggregateRequest(_message.Message):
    __slots__ = ("label_schema_json", "thresholds", "min_f1", "samples")
    class ThresholdsEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: float
        def __init__(self, key: _Optional[str] = ..., value: _Optional[float] = ...) -> None: ...
    LABEL_SCHEMA_JSON_FIELD_NUMBER: _ClassVar[int]
    THRESHOLDS_FIELD_NUMBER: _ClassVar[int]
    MIN_F1_FIELD_NUMBER: _ClassVar[int]
    SAMPLES_FIELD_NUMBER: _ClassVar[int]
    label_schema_json: str
    thresholds: _containers.ScalarMap[str, float]
    min_f1: float
    samples: _containers.RepeatedCompositeFieldContainer[SampleLabels]
    def __init__(self, label_schema_json: _Optional[str] = ..., thresholds: _Optional[_Mapping[str, float]] = ..., min_f1: _Optional[float] = ..., samples: _Optional[_Iterable[_Union[SampleLabels, _Mapping]]] = ...) -> None: ...

class SampleLabels(_message.Message):
    __slots__ = ("sample_id", "metadata_json", "label_data_json")
    SAMPLE_ID_FIELD_NUMBER: _ClassVar[int]
    METADATA_JSON_FIELD_NUMBER: _ClassVar[int]
    LABEL_DATA_JSON_FIELD_NUMBER: _ClassVar[int]
    sample_id: str
    metadata_json: str
    label_data_json: _containers.RepeatedScalarFieldContainer[str]
    def __init__(self, sample_id: _Optional[str] = ..., metadata_json: _Optional[str] = ..., label_data_json: _Optional[_Iterable[str]] = ...) -> None: ...

class AggregateResponse(_message.Message):
    __slots__ = ("samples",)
    SAMPLES_FIELD_NUMBER: _ClassVar[int]
    samples: _containers.RepeatedCompositeFieldContainer[SampleAggregate]
    def __init__(self, samples: _Optional[_Iterable[_Union[SampleAggregate, _Mapping]]] = ...) -> None: ...

class SampleAggregate(_message.Message):
    __slots__ = ("sample_id", "tools_json")
    SAMPLE_ID_FIELD_NUMBER: _ClassVar[int]
    TOOLS_JSON_FIELD_NUMBER: _ClassVar[int]
    sample_id: str
    tools_json: str
    def __init__(self, sample_id: _Optional[str] = ..., tools_json: _Optional[str] = ...) -> None: ...
