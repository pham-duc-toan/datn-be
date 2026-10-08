"""Vo boc event — ban Python cua Crowd.BuildingBlocks.Messaging.EventEnvelope.

Hop dong chung: contracts/events/envelope.schema.json. Giong CrowdJson ben C#:
  - ten truong camelCase, enum chuoi camelCase;
  - LUON ghi null tuong minh (causationId, actor) — khong bo key;
  - KHONG chap nhan truong la (extra="forbid") — lech hop dong phai no ngay.
"""

import os
import re
import time
import uuid
from datetime import datetime, timezone
from typing import Any, ClassVar, Generic, Literal, TypeVar

from pydantic import BaseModel, ConfigDict, Field, ValidationError
from pydantic.alias_generators import to_camel

MAU_EVENT_TYPE = re.compile(r"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$")


class HopDong(BaseModel):
    """Lop goc cho envelope va moi payload: camelCase, cam truong la."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")


class Payload(HopDong):
    """Payload cua mot loai event. Lop con khai EVENT_TYPE va VERSION."""

    EVENT_TYPE: ClassVar[str]
    VERSION: ClassVar[int] = 1


class LoiHopDong(Exception):
    """Message khong doc duoc theo hop dong — thu lai bao nhieu lan cung sai, vao DLQ ngay."""


class Actor(HopDong):
    user_id: uuid.UUID
    role: Literal["business", "labeler", "sharer", "guest", "admin", "system"]


P = TypeVar("P", bound=Payload)


class Envelope(HopDong, Generic[P]):
    event_id: uuid.UUID
    event_type: str
    version: int = Field(ge=1)
    occurred_at: datetime
    producer: str = Field(min_length=1)
    correlation_id: uuid.UUID
    causation_id: uuid.UUID | None
    actor: Actor | None
    payload: P

    def to_json(self) -> str:
        # by_alias: camelCase; exclude_none=False: null tuong minh nhu C#.
        return self.model_dump_json(by_alias=True)


def uuid7() -> uuid.UUID:
    """UUIDv7 (RFC 9562): 48 bit thoi gian ms + ngau nhien — sap theo thoi gian nhu Guid.CreateVersion7."""
    ms = int(time.time() * 1000)
    ngau_nhien = int.from_bytes(os.urandom(10), "big")
    gia_tri = (ms & ((1 << 48) - 1)) << 80
    gia_tri |= 0x7 << 76                                   # version 7
    gia_tri |= ((ngau_nhien >> 62) & 0xFFF) << 64          # rand_a 12 bit
    gia_tri |= 0b10 << 62                                  # variant RFC 4122
    gia_tri |= ngau_nhien & ((1 << 62) - 1)                # rand_b 62 bit
    return uuid.UUID(int=gia_tri)


def tao(payload: Payload, producer: str, correlation_id: uuid.UUID, causation_id: uuid.UUID | None,
        occurred_at: datetime | None = None) -> Envelope:
    """Boc payload thanh envelope moi. actor = null: quality-svc la he thong tu dong."""
    return Envelope[type(payload)](
        event_id=uuid7(),
        event_type=payload.EVENT_TYPE,
        version=payload.VERSION,
        occurred_at=occurred_at or datetime.now(timezone.utc),
        producer=producer,
        correlation_id=correlation_id,
        causation_id=causation_id,
        actor=None,
        payload=payload,
    )


def doc(body: bytes, loai: type[P]) -> Envelope[P]:
    """Doc message theo dung loai payload. Sai hop dong → LoiHopDong."""
    try:
        env = Envelope[loai].model_validate_json(body)
    except ValidationError as ex:
        raise LoiHopDong(f"Message khong doc duoc theo hop dong envelope: {ex}") from ex

    if not MAU_EVENT_TYPE.match(env.event_type):
        raise LoiHopDong(f"eventType '{env.event_type}' sai dinh dang")

    # Chan doc nham loai: message 'escrow.reserved' ma cac truong tinh co khop.
    if env.event_type != loai.EVENT_TYPE:
        raise LoiHopDong(f"Envelope {env.event_id} mang eventType '{env.event_type}' nhung dang doc thanh {loai.__name__}")

    return env


def du_lieu_tho(env: Envelope) -> dict[str, Any]:
    """Envelope duoi dang dict (camelCase) — de luu cot envelope_json cua outbox."""
    return env.model_dump(by_alias=True, mode="json")
