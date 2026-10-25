"""Hop dong envelope giua Python va C#: doi chieu contracts/events/envelope.schema.json."""

import json
import uuid
from datetime import datetime, timezone
from pathlib import Path

import jsonschema
import pytest

from app.contracts import AttemptSubmitted, GradedAttempt, ProjectGraded, ReputationChanged
from app.messaging.envelope import LoiHopDong, doc, tao, uuid7

SCHEMA = json.loads((Path(__file__).resolve().parents[3] / "contracts" / "events" / "envelope.schema.json").read_text(encoding="utf-8"))

# Dung dinh dang System.Text.Json + CrowdJson cua C# ghi ra (null tuong minh,
# DateTimeOffset 7 chu so le, enum camelCase).
TU_CSHARP = (
    '{"eventId":"01a11070-3a9d-7e98-a06f-8ad235602bae","eventType":"attempt.submitted","version":1,'
    '"occurredAt":"2026-10-07T08:59:13.3643945+00:00","producer":"task-svc",'
    '"correlationId":"0d352aaf-338d-5930-8bd7-e737a04e3ba3","causationId":null,'
    '"actor":{"userId":"1f5e19a7-9f58-589b-8c74-45334ff35d05","role":"labeler"},'
    '"payload":{"attemptId":"7efccfd8-ab86-58e6-93d8-9b84d6802967",'
    '"projectId":"3d904a1a-3920-530b-b055-793baae64f1b","labelerId":"1f5e19a7-9f58-589b-8c74-45334ff35d05",'
    '"answers":[{"sampleId":"25bb3dd9-5900-5e1c-9d57-f84ed2c72645","storageKey":"p/a.png","sampleContent":null,'
    '"sampleMetadata":{"width":64,"height":48},'
    '"labelPayload":{"taskType":"image","schemaVersion":1,"data":{"label":{"labelIds":["do"]}}}},'
    '{"sampleId":"35bb3dd9-5900-5e1c-9d57-f84ed2c72645","storageKey":"p/b.png","sampleContent":null,'
    '"sampleMetadata":{"width":64,"height":48},"labelPayload":null}],'
    '"goldTotal":2,"goldCorrect":1,"startedAt":"2026-10-07T08:40:00+00:00","submittedAt":"2026-10-07T08:59:13.3643945+00:00",'
    '"clientIp":"203.113.5.9","deviceId":null}}'
)


def test_doc_duoc_envelope_csharp_ghi():
    env = doc(TU_CSHARP.encode(), AttemptSubmitted)

    assert env.actor is not None and env.actor.role == "labeler"
    assert env.payload.answers[0].label_payload.data == {"label": {"labelIds": ["do"]}}
    assert env.payload.answers[0].sample_metadata == {"width": 64, "height": 48}
    # Cau bo trong: labelPayload null.
    assert env.payload.answers[1].label_payload is None
    assert (env.payload.gold_total, env.payload.gold_correct) == (2, 1)


def test_envelope_python_ghi_ra_dung_schema_chung_va_camel_case():
    env = tao(ProjectGraded(
        project_id=uuid.uuid4(), pass_threshold_percent=80, majority_used=True,
        attempts=[GradedAttempt(attempt_id=uuid.uuid4(), labeler_id=uuid.uuid4(), comparable_count=5, matched_count=4,
                                agreement_percent=80, gold_total=2, gold_correct=2, passed=True, rewarded=True, answered_count=5)],
        graded_at=datetime.now(timezone.utc)),
        "quality-svc", uuid.uuid4(), None)
    du_lieu = json.loads(env.to_json())

    jsonschema.validate(du_lieu, SCHEMA)
    # null tuong minh nhu C#, khong bo key.
    assert du_lieu["causationId"] is None and du_lieu["actor"] is None
    assert set(du_lieu["payload"]) == {"projectId", "passThresholdPercent", "majorityUsed", "attempts", "gradedAt"}
    assert set(du_lieu["payload"]["attempts"][0]) == {
        "attemptId", "labelerId", "comparableCount", "matchedCount", "agreementPercent", "goldTotal", "goldCorrect",
        "passed", "rewarded", "answeredCount"}


def test_truong_la_hoac_sai_loai_vao_dlq():
    du_lieu = json.loads(TU_CSHARP)
    du_lieu["payload"]["truongMoi"] = 1
    with pytest.raises(LoiHopDong):
        doc(json.dumps(du_lieu).encode(), AttemptSubmitted)

    with pytest.raises(LoiHopDong):
        doc(TU_CSHARP.encode(), ReputationChanged)


def test_uuid7_dung_phien_ban_va_tang_theo_thoi_gian():
    a = uuid7()
    b = uuid7()
    assert a.version == 7 and a.variant == uuid.RFC_4122
    assert a.int >> 80 <= b.int >> 80
