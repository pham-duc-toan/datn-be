"""gRPC quality PHUC VU (kien truc 3.24, contracts/grpc/quality/v1/label_aggregation.proto).

project-svc gui nhan DA CHOT cua cac mau can tinh (ket qua giao doanh nghiep: bai dat + khach cong link) →
gop bang app/gop.py — noi DUY NHAT cai thuat toan gop (truoc day project-svc chay ban C# rieng). Khong doc / ghi
DB: dau vao la cua project-svc, tinh xong tra ve; project-svc giu bang dem sample_results.

Khoa noi bo trong metadata x-internal-key (cung khoa InternalApi:Key cua cac service C#). Gop (numpy, mat na
lon) chay trong thread rieng — khong chan vong su kien cua consumer / worker.
"""

import asyncio
import hmac
import json
import logging
from typing import Any

import grpc

from app import gop
from app.config import settings
from app.grpc_gen.quality.v1 import label_aggregation_pb2 as pb
from app.grpc_gen.quality.v1 import label_aggregation_pb2_grpc as pb_grpc

log = logging.getLogger(__name__)

# Lo cua project-svc toi da ~2 MB JSON (Helpers/GopNhanClient.CoLo); de du cho protobuf + ket qua.
CO_MESSAGE = 32 * 1024 * 1024


class KiemKhoaNoiBo(grpc.aio.ServerInterceptor):
    """Sai / thieu x-internal-key → UNAUTHENTICATED. So thoi gian co dinh nhu [InternalApi] cua C#."""

    def __init__(self, khoa: str):
        self._khoa = khoa.encode("utf-8")

        async def tu_choi(_request, context):
            await context.abort(grpc.StatusCode.UNAUTHENTICATED, "Sai khoa noi bo.")

        self._tu_choi = grpc.unary_unary_rpc_method_handler(tu_choi)

    async def intercept_service(self, continuation, handler_call_details):
        gui_len = dict(handler_call_details.invocation_metadata or ()).get("x-internal-key", "")
        if not self._khoa or not hmac.compare_digest(self._khoa, str(gui_len).encode("utf-8")):
            return self._tu_choi
        return await continuation(handler_call_details)


def gop_lo(request: pb.AggregateRequest) -> pb.AggregateResponse:
    """Ham thuan (khong I/O) — test goi thang. Tap nhan / nhan sai dinh dang → ValueError."""
    try:
        tap_nhan: dict[str, Any] = json.loads(request.label_schema_json)
    except json.JSONDecodeError as ex:
        raise ValueError("label_schema_json khong phai JSON: " + str(ex)) from ex
    if not isinstance(tap_nhan, dict) or not isinstance(tap_nhan.get("tools"), list):
        raise ValueError("label_schema_json phai co mang tools.")

    nguong = dict(request.thresholds)
    f1 = request.min_f1 if request.min_f1 > 0 else gop.F1_TOI_THIEU_MAC_DINH
    ra = pb.AggregateResponse()
    for s in request.samples:
        mau = json.loads(s.metadata_json) if s.metadata_json else None
        nhan = [json.loads(x) for x in s.label_data_json]
        ket_qua = gop.gop_mau(tap_nhan, nhan, mau, nguong, f1)
        ra.samples.append(pb.SampleAggregate(
            sample_id=s.sample_id, tools_json=json.dumps(ket_qua, ensure_ascii=False, separators=(",", ":"))))
    return ra


class LabelAggregation(pb_grpc.LabelAggregationServicer):
    async def AggregateSamples(self, request, context):
        try:
            return await asyncio.to_thread(gop_lo, request)
        except (ValueError, KeyError, TypeError) as ex:
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Khong gop duoc: " + str(ex))


async def bat_dau() -> grpc.aio.Server | None:
    """Mo cong gRPC (QUALITY_GRPC_PORT). Thieu khoa noi bo hoac cong 0 → khong mo (ghi canh bao)."""
    cfg = settings()
    if cfg.grpc_port <= 0:
        return None
    if not cfg.internal_api_key:
        log.warning("Thieu QUALITY_INTERNAL_API_KEY — khong mo gRPC LabelAggregation (project-svc se tra 503 ket qua)")
        return None

    server = grpc.aio.server(
        interceptors=[KiemKhoaNoiBo(cfg.internal_api_key)],
        options=[("grpc.max_receive_message_length", CO_MESSAGE), ("grpc.max_send_message_length", CO_MESSAGE)])
    pb_grpc.add_LabelAggregationServicer_to_server(LabelAggregation(), server)
    server.add_insecure_port(f"[::]:{cfg.grpc_port}")
    await server.start()
    log.info("gRPC LabelAggregation tren cong %s", cfg.grpc_port)
    return server
