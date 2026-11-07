"""Doc de bai / bai nop / nhan tu project-svc qua gRPC (kien truc 3.22, contracts/grpc/project/v1).

quality-svc KHONG giu ban sao projects / attempts / labels nua: du lieu do la cua project-svc. Moi lan cham,
lo Dawid-Skene, xet gian lan doc thang tu nguon. Khong co lan nao o duong nong cua nguoi dung: consumer chi
ghi hang cho, worker goi gRPC; project-svc tam sap thi worker thu lai vong sau (KhongGoiDuoc).

Mot channel HTTP/2 dung chung ca tien trinh (grpc.aio da multiplex), tao luc goi dau tien.
"""

import json
import logging
import uuid
from collections.abc import AsyncIterator
from dataclasses import dataclass
from datetime import datetime, timezone

import grpc

from app.config import settings
from app.grpc_gen.project.v1 import quality_data_pb2 as pb
from app.grpc_gen.project.v1 import quality_data_pb2_grpc as pb_grpc

log = logging.getLogger(__name__)

HET_GIO_GOI = 10.0     # giay — RPC mot lan
HET_GIO_STREAM = 300.0  # giay — doc het nhan mot du an lon

# Loi tam thoi: project-svc dang khoi dong lai / mang chap chon → thu lai sau, khong phai loi du lieu.
_TAM_THOI = {grpc.StatusCode.UNAVAILABLE, grpc.StatusCode.DEADLINE_EXCEEDED, grpc.StatusCode.RESOURCE_EXHAUSTED,
             grpc.StatusCode.ABORTED}


class ChuaCauHinh(Exception):
    """Thieu QUALITY_INTERNAL_API_KEY — khong goi duoc project-svc."""


class KhongTimThay(Exception):
    """Du an chua publish (hoac khong ton tai)."""


class KhongGoiDuoc(Exception):
    """project-svc tam thoi khong tra loi — viec de lai cho vong sau."""


@dataclass(frozen=True)
class DuAn:
    project_id: uuid.UUID
    owner_id: uuid.UUID
    modality: str
    label_schema: dict
    pass_threshold_percent: int
    slots: int
    fill_slots_min_percent: int | None
    question_count: int
    status: str
    published_at: datetime
    # Dot thu bai: None = con nhan bai.
    finish_reason: str | None
    collection_finished_at: datetime | None
    excluded_sample_ids: frozenset[str]


@dataclass(frozen=True)
class Bai:
    attempt_id: uuid.UUID
    labeler_id: uuid.UUID
    # Cau THAT theo thu tu cua bai (khong co cau vang).
    sample_ids: tuple[str, ...]
    label_count: int
    answered_total: int
    gold_total: int
    gold_correct: int
    started_at: datetime
    submitted_at: datetime
    # Ket qua cham project-svc dang giu (sau khieu nai); graded False → chua cham.
    graded: bool
    comparable_count: int
    matched_count: int
    agreement_percent: int
    passed: bool
    rewarded: bool
    appeal_state: str
    client_ip: str | None
    device_id: str | None

    @property
    def giay_moi_cau(self) -> float:
        """Cung cong thuc QualityDataService.GiayMoiCau (C#)."""
        return max(0.0, (self.submitted_at - self.started_at).total_seconds()) / max(self.answered_total, 1)


@dataclass(frozen=True)
class Nhan:
    attempt_id: uuid.UUID
    sample_id: uuid.UUID
    labeler_id: uuid.UUID
    data: dict
    sample_metadata: dict | None
    submitted_at: datetime


_channel: grpc.aio.Channel | None = None
_stub: pb_grpc.QualityDataStub | None = None


def _stub_va_khoa() -> tuple[pb_grpc.QualityDataStub, tuple[tuple[str, str], ...]]:
    global _channel, _stub
    cfg = settings()
    if not cfg.internal_api_key:
        raise ChuaCauHinh("Thieu QUALITY_INTERNAL_API_KEY")
    if _stub is None:
        _channel = grpc.aio.insecure_channel(cfg.project_grpc)
        _stub = pb_grpc.QualityDataStub(_channel)
    return _stub, (("x-internal-key", cfg.internal_api_key),)


async def dong() -> None:
    global _channel, _stub
    if _channel is not None:
        await _channel.close()
    _channel = None
    _stub = None


def _doi_loi(ex: grpc.aio.AioRpcError) -> Exception:
    if ex.code() == grpc.StatusCode.NOT_FOUND:
        return KhongTimThay(ex.details())
    if ex.code() in _TAM_THOI:
        return KhongGoiDuoc(f"{ex.code().name}: {ex.details()}")
    return ex


def _luc(ts) -> datetime:
    return ts.ToDatetime(tzinfo=timezone.utc)


async def du_an(project_id: uuid.UUID) -> DuAn:
    stub, khoa = _stub_va_khoa()
    try:
        r = await stub.GetProject(pb.ProjectRequest(project_id=str(project_id)), metadata=khoa, timeout=HET_GIO_GOI)
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex
    return DuAn(
        project_id=uuid.UUID(r.project_id), owner_id=uuid.UUID(r.owner_id), modality=r.modality,
        label_schema=json.loads(r.label_schema_json), pass_threshold_percent=r.pass_threshold_percent, slots=r.slots,
        fill_slots_min_percent=r.fill_slots_min_percent if r.HasField("fill_slots_min_percent") else None,
        question_count=r.question_count, status=r.status, published_at=_luc(r.published_at),
        finish_reason=r.finish_reason or None,
        collection_finished_at=_luc(r.collection_finished_at) if r.HasField("collection_finished_at") else None,
        excluded_sample_ids=frozenset(r.excluded_sample_ids))


async def bai_da_nop(project_id: uuid.UUID) -> list[Bai]:
    """Moi bai da nop cua du an, theo thu tu nop."""
    stub, khoa = _stub_va_khoa()
    try:
        r = await stub.ListSubmittedAttempts(pb.ProjectRequest(project_id=str(project_id)), metadata=khoa, timeout=HET_GIO_GOI)
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex
    return [Bai(
        attempt_id=uuid.UUID(a.attempt_id), labeler_id=uuid.UUID(a.labeler_id), sample_ids=tuple(a.sample_ids),
        label_count=a.label_count, answered_total=a.answered_total, gold_total=a.gold_total, gold_correct=a.gold_correct,
        started_at=_luc(a.started_at), submitted_at=_luc(a.submitted_at), graded=a.graded,
        comparable_count=a.comparable_count, matched_count=a.matched_count, agreement_percent=a.agreement_percent,
        passed=a.passed, rewarded=a.rewarded, appeal_state=a.appeal_state,
        client_ip=a.client_ip or None, device_id=a.device_id or None) for a in r.attempts]


async def tung_nhan(project_id: uuid.UUID, attempt_ids: list[uuid.UUID] | None = None) -> AsyncIterator[Nhan]:
    """Nhan cua labeler trong bai da nop (attempt_ids rong = moi bai), theo thu tu nop — doc dan theo stream."""
    stub, khoa = _stub_va_khoa()
    yeu_cau = pb.LabelsRequest(project_id=str(project_id), attempt_ids=[str(a) for a in attempt_ids or []])
    try:
        async for d in stub.StreamLabels(yeu_cau, metadata=khoa, timeout=HET_GIO_STREAM):
            md = json.loads(d.sample_metadata_json) if d.sample_metadata_json else None
            yield Nhan(attempt_id=uuid.UUID(d.attempt_id), sample_id=uuid.UUID(d.sample_id),
                       labeler_id=uuid.UUID(d.labeler_id), data=json.loads(d.data_json),
                       sample_metadata=md or None, submitted_at=_luc(d.submitted_at))
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex


async def nhan(project_id: uuid.UUID, attempt_ids: list[uuid.UUID] | None = None) -> list[Nhan]:
    return [n async for n in tung_nhan(project_id, attempt_ids)]


async def nguoi_nop_chung(*, client_ip: str | None = None, device_id: str | None = None, tu: datetime) -> list[uuid.UUID]:
    """Cac labeler nop bai tu cung IP / thiet bi (moi du an) sau `tu`. Dien dung mot trong hai."""
    stub, khoa = _stub_va_khoa()
    yeu_cau = pb.SharedSubmittersRequest(client_ip=client_ip or "", device_id=device_id or "")
    yeu_cau.since.FromDatetime(tu)
    try:
        r = await stub.FindSharedSubmitters(yeu_cau, metadata=khoa, timeout=HET_GIO_GOI)
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex
    return [uuid.UUID(x) for x in r.labeler_ids]


async def thong_ke_labeler(labeler_ids: list[uuid.UUID]) -> dict[uuid.UUID, tuple[int, float]]:
    """{labeler: (so bai nop, trung vi giay moi cau)} tren moi du an."""
    if not labeler_ids:
        return {}
    stub, khoa = _stub_va_khoa()
    try:
        r = await stub.GetLabelerSubmissionStats(pb.LabelerStatsRequest(labeler_ids=[str(x) for x in labeler_ids]),
                                                 metadata=khoa, timeout=HET_GIO_GOI)
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex
    return {uuid.UUID(x.labeler_id): (x.submissions, x.median_seconds_per_question) for x in r.labelers}


@dataclass(frozen=True)
class BangChung:
    """Bang chung uy tin cong don moi du an: bai project-svc DA ghi ket qua cham."""
    graded_attempts: int = 0
    gold_total: int = 0
    gold_correct: int = 0
    comparable_total: int = 0
    matched_total: int = 0


async def bang_chung(labeler_ids: list[uuid.UUID]) -> dict[uuid.UUID, BangChung]:
    """{labeler: BangChung}. Labeler chua co bai nao duoc cham → khong co trong ket qua."""
    if not labeler_ids:
        return {}
    stub, khoa = _stub_va_khoa()
    try:
        r = await stub.GetLabelerEvidence(pb.LabelerEvidenceRequest(labeler_ids=[str(x) for x in labeler_ids]),
                                          metadata=khoa, timeout=HET_GIO_GOI)
    except grpc.aio.AioRpcError as ex:
        raise _doi_loi(ex) from ex
    return {uuid.UUID(x.labeler_id): BangChung(x.graded_attempts, x.gold_total, x.gold_correct, x.comparable_total,
                                               x.matched_total) for x in r.labelers}
