"""Hop dong envelope giua Python va C#: doi chieu contracts/events/envelope.schema.json."""

import json
import uuid
from pathlib import Path

import jsonschema
import pytest

from app.contracts import AnnotationSubmitted, ConsensusReached, ConsensusVote, ReputationChanged
from app.messaging.envelope import LoiHopDong, doc, tao, uuid7

SCHEMA = json.loads((Path(__file__).resolve().parents[3] / "contracts" / "events" / "envelope.schema.json").read_text(encoding="utf-8"))

# Dung dinh dang System.Text.Json + CrowdJson cua C# ghi ra (null tuong minh,
# DateTimeOffset 7 chu so le, enum camelCase).
TU_CSHARP = (
    '{"eventId":"01a11070-3a9d-7e98-a06f-8ad235602bae","eventType":"annotation.submitted","version":1,'
    '"occurredAt":"2026-10-07T08:59:13.3643945+00:00","producer":"annotation-svc",'
    '"correlationId":"0d352aaf-338d-5930-8bd7-e737a04e3ba3","causationId":null,'
    '"actor":{"userId":"1f5e19a7-9f58-589b-8c74-45334ff35d05","role":"labeler"},'
    '"payload":{"annotationId":"7efccfd8-ab86-58e6-93d8-9b84d6802967","taskId":"3d904a1a-3920-530b-b055-793baae64f1b",'
    '"projectId":"3d904a1a-3920-530b-b055-793baae64f1b","sampleId":"25bb3dd9-5900-5e1c-9d57-f84ed2c72645",'
    '"labelerId":"1f5e19a7-9f58-589b-8c74-45334ff35d05",'
    '"labelPayload":{"taskType":"image","schemaVersion":1,"data":{"label":{"labelIds":["do"]}}},'
    '"source":"professional"}}'
)


def test_doc_duoc_envelope_csharp_ghi():
    env = doc(TU_CSHARP.encode(), AnnotationSubmitted)

    assert env.actor is not None and env.actor.role == "labeler"
    assert env.payload.label_payload.data == {"label": {"labelIds": ["do"]}}
    assert env.payload.source == "professional"


def test_envelope_python_ghi_ra_dung_schema_chung_va_camel_case():
    env = tao(ConsensusReached(
        task_id=uuid.uuid4(), project_id=uuid.uuid4(), sample_id=uuid.uuid4(), status="notApplicable", final=None,
        votes=[ConsensusVote(annotation_id=uuid.uuid4(), labeler_id=uuid.uuid4(), agrees=None)]),
        "quality-svc", uuid.uuid4(), None)
    du_lieu = json.loads(env.to_json())

    jsonschema.validate(du_lieu, SCHEMA)
    # null tuong minh nhu C#, khong bo key.
    assert du_lieu["causationId"] is None and du_lieu["actor"] is None
    assert set(du_lieu["payload"]) == {"taskId", "projectId", "sampleId", "status", "final", "votes"}
    assert du_lieu["payload"]["votes"][0] == {"annotationId": du_lieu["payload"]["votes"][0]["annotationId"],
                                              "labelerId": du_lieu["payload"]["votes"][0]["labelerId"], "agrees": None}


def test_truong_la_hoac_sai_loai_vao_dlq():
    du_lieu = json.loads(TU_CSHARP)
    du_lieu["payload"]["truongMoi"] = 1
    with pytest.raises(LoiHopDong):
        doc(json.dumps(du_lieu).encode(), AnnotationSubmitted)

    with pytest.raises(LoiHopDong):
        doc(TU_CSHARP.encode(), ReputationChanged)


def test_uuid7_dung_phien_ban_va_tang_theo_thoi_gian():
    a = uuid7()
    b = uuid7()
    assert a.version == 7 and a.variant == uuid.RFC_4122
    assert a.int >> 80 <= b.int >> 80
