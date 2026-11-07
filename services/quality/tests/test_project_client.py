"""project_client qua gRPC THAT (server gia trong tien trinh, cung stub sinh tu contracts/grpc): doi kieu dung,
gui khoa noi bo, doi ma loi gRPC thanh loi nghiep vu (NOT_FOUND → KhongTimThay, UNAVAILABLE → KhongGoiDuoc)."""

import uuid
from datetime import datetime, timedelta, timezone

import grpc
import pytest

from app import config, project_client
from app.grpc_gen.project.v1 import quality_data_pb2 as pb
from app.grpc_gen.project.v1 import quality_data_pb2_grpc as pb_grpc

DU_AN = uuid.uuid4()
CHU = uuid.uuid4()
BAI = uuid.uuid4()
NGUOI = uuid.uuid4()
MAU = uuid.uuid4()
LUC = datetime(2026, 11, 2, 8, 0, tzinfo=timezone.utc)


def _ts(d: datetime):
    from google.protobuf.timestamp_pb2 import Timestamp
    t = Timestamp()
    t.FromDatetime(d)
    return t


class GiaLap(pb_grpc.QualityDataServicer):
    def __init__(self) -> None:
        self.khoa: list[str | None] = []

    def _ghi_khoa(self, context) -> None:
        self.khoa.append(dict(context.invocation_metadata()).get("x-internal-key"))

    async def GetProject(self, request, context):
        self._ghi_khoa(context)
        if request.project_id != str(DU_AN):
            await context.abort(grpc.StatusCode.NOT_FOUND, "Du an chua publish.")
        r = pb.ProjectInfo(project_id=str(DU_AN), owner_id=str(CHU), modality="image",
                           label_schema_json='{"tools":[{"name":"loai","kind":"classification"}]}',
                           pass_threshold_percent=70, slots=3, question_count=5, status="Grading",
                           published_at=_ts(LUC), finish_reason="du_suat", collection_finished_at=_ts(LUC + timedelta(hours=1)),
                           excluded_sample_ids=[str(MAU)])
        return r

    async def ListSubmittedAttempts(self, request, context):
        self._ghi_khoa(context)
        return pb.AttemptList(attempts=[pb.AttemptInfo(
            attempt_id=str(BAI), labeler_id=str(NGUOI), sample_ids=[str(MAU)], label_count=1, answered_total=4,
            gold_total=3, gold_correct=3, started_at=_ts(LUC), submitted_at=_ts(LUC + timedelta(seconds=40)),
            graded=True, comparable_count=1, matched_count=1, agreement_percent=100, passed=True, rewarded=True,
            appeal_state="None", client_ip="203.113.5.9")])

    async def StreamLabels(self, request, context):
        self._ghi_khoa(context)
        for i in range(3):
            yield pb.LabelRow(attempt_id=str(BAI), sample_id=str(MAU), labeler_id=str(NGUOI),
                              data_json='{"loai":{"labelIds":["cho"]}}', sample_metadata_json="{}" if i == 0 else '{"width":%d}' % i,
                              submitted_at=_ts(LUC))

    async def FindSharedSubmitters(self, request, context):
        self._ghi_khoa(context)
        await context.abort(grpc.StatusCode.UNAVAILABLE, "dang khoi dong")

    async def GetLabelerEvidence(self, request, context):
        self._ghi_khoa(context)
        # Chi labeler co bai da cham moi co mat (giong QualityDataService).
        return pb.LabelerEvidenceResponse(labelers=[pb.LabelerEvidence(
            labeler_id=x, graded_attempts=2, gold_total=6, gold_correct=5, comparable_total=10, matched_total=7)
            for x in request.labeler_ids if x == str(NGUOI)])


@pytest.fixture
async def may_chu(monkeypatch):
    server = grpc.aio.server()
    gia = GiaLap()
    pb_grpc.add_QualityDataServicer_to_server(gia, server)
    cong = server.add_insecure_port("127.0.0.1:0")
    await server.start()
    monkeypatch.setenv("QUALITY_PROJECT_GRPC", f"127.0.0.1:{cong}")
    monkeypatch.setenv("QUALITY_INTERNAL_API_KEY", "khoa-test")
    config.settings.cache_clear()
    await project_client.dong()
    yield gia
    await project_client.dong()
    config.settings.cache_clear()
    await server.stop(None)


async def test_du_an_doi_kieu_va_gui_khoa_noi_bo(may_chu):
    d = await project_client.du_an(DU_AN)
    assert d.owner_id == CHU and d.label_schema["tools"][0]["kind"] == "classification"
    assert d.fill_slots_min_percent is None  # optional khong gui → None, khong phai 0
    assert d.finish_reason == "du_suat" and d.collection_finished_at == LUC + timedelta(hours=1)
    assert d.excluded_sample_ids == frozenset({str(MAU)})
    assert may_chu.khoa == ["khoa-test"]


async def test_du_an_khong_ton_tai_thanh_khong_tim_thay(may_chu):
    with pytest.raises(project_client.KhongTimThay):
        await project_client.du_an(uuid.uuid4())


async def test_bai_nop_va_giay_moi_cau(may_chu):
    [b] = await project_client.bai_da_nop(DU_AN)
    assert b.sample_ids == (str(MAU),) and b.passed and b.rewarded and b.device_id is None
    # 40 giay / 4 cau da tra loi (ca cau vang) — cung cong thuc QualityDataService.GiayMoiCau.
    assert b.giay_moi_cau == 10.0


async def test_nhan_doc_theo_stream(may_chu):
    ds = await project_client.nhan(DU_AN)
    # Mau khong co thong tin ("{}") → None, giong ban sao cu (gop.duong_cheo dung hinh bao cua chinh cac diem).
    assert [n.sample_metadata for n in ds] == [None, {"width": 1}, {"width": 2}]
    assert ds[0].data == {"loai": {"labelIds": ["cho"]}}


async def test_project_svc_tam_sap_thanh_khong_goi_duoc(may_chu):
    with pytest.raises(project_client.KhongGoiDuoc):
        await project_client.nguoi_nop_chung(client_ip="1.2.3.4", tu=LUC)


async def test_bang_chung_uy_tin_cong_don(may_chu):
    khac = uuid.uuid4()
    bc = await project_client.bang_chung([NGUOI, khac])
    assert bc[NGUOI] == project_client.BangChung(2, 6, 5, 10, 7)
    assert khac not in bc  # chua co bai duoc cham → noi goi dung BangChung() (toan 0)
    assert await project_client.bang_chung([]) == {}
