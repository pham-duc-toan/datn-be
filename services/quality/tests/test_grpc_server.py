"""gRPC LabelAggregation (3.24): project-svc gui nhan da chot, quality gop bang app/gop.py."""

import json

import grpc
import pytest

from app import grpc_server
from app.grpc_gen.quality.v1 import label_aggregation_pb2 as pb
from app.grpc_gen.quality.v1 import label_aggregation_pb2_grpc as pb_grpc

TAP_NHAN = {"modality": "image", "tools": [
    {"name": "loai", "kind": "classification", "classes": ["cho", "meo"]},
    {"name": "vat", "kind": "bbox", "classes": ["mat"], "required": False},
]}


def yeu_cau(*mau, nguong=None):
    r = pb.AggregateRequest(label_schema_json=json.dumps(TAP_NHAN), min_f1=0.8)
    for k, v in (nguong or {}).items():
        r.thresholds[k] = v
    for sid, nhan in mau:
        r.samples.append(pb.SampleLabels(sample_id=sid, metadata_json='{"width":100,"height":100}',
                                         label_data_json=[json.dumps(n) for n in nhan]))
    return r


def test_gop_lo_tra_ket_qua_tung_mau():
    ra = grpc_server.gop_lo(yeu_cau(
        ("m1", [{"loai": {"labelIds": ["cho"]}}, {"loai": {"labelIds": ["cho"]}}, {"loai": {"labelIds": ["meo"]}}]),
        ("m2", [{"loai": {"labelIds": ["cho"]}}, {"loai": {"labelIds": ["meo"]}}])))

    kq = {s.sample_id: json.loads(s.tools_json) for s in ra.samples}
    assert kq["m1"]["loai"]["final"] == {"labelIds": ["cho"]}
    assert kq["m1"]["loai"]["votes"] == {"cho": 2, "meo": 1}
    assert kq["m1"]["vat"] == {"method": "none", "kind": "bbox"}
    assert kq["m2"]["loai"]["disputed"] is True


def test_tap_nhan_sai_thi_value_error():
    with pytest.raises(ValueError):
        grpc_server.gop_lo(pb.AggregateRequest(label_schema_json="khong phai json"))


async def test_server_kiem_khoa_noi_bo():
    server = grpc.aio.server(interceptors=[grpc_server.KiemKhoaNoiBo("khoa_test")])
    pb_grpc.add_LabelAggregationServicer_to_server(grpc_server.LabelAggregation(), server)
    cong = server.add_insecure_port("127.0.0.1:0")
    await server.start()
    try:
        async with grpc.aio.insecure_channel(f"127.0.0.1:{cong}") as kenh:
            stub = pb_grpc.LabelAggregationStub(kenh)
            ra = await stub.AggregateSamples(yeu_cau(("m1", [{"loai": {"labelIds": ["cho"]}}])),
                                             metadata=(("x-internal-key", "khoa_test"),))
            assert json.loads(ra.samples[0].tools_json)["loai"]["final"] == {"labelIds": ["cho"]}

            with pytest.raises(grpc.aio.AioRpcError) as sai:
                await stub.AggregateSamples(yeu_cau(), metadata=(("x-internal-key", "sai"),))
            assert sai.value.code() == grpc.StatusCode.UNAUTHENTICATED

            with pytest.raises(grpc.aio.AioRpcError) as hong:
                await stub.AggregateSamples(pb.AggregateRequest(label_schema_json="{}"),
                                            metadata=(("x-internal-key", "khoa_test"),))
            assert hong.value.code() == grpc.StatusCode.INVALID_ARGUMENT
    finally:
        await server.stop(0)
