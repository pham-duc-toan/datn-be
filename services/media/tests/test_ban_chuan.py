"""Viec nap video: doc event samples.ingested v2, gom doan mau theo file, bo qua doan da cat san."""

import json
import uuid
from pathlib import Path

from app import ban_chuan, hls
from app.contracts import SamplesIngested
from app.messaging import envelope as env_mod

CHU = "nguồn: Công ty A - gannhan.vn"


def test_doan_cua_mau():
    assert ban_chuan.doan_cua_mau({"durationSec": 30}) == (0.0, 30.0)
    assert ban_chuan.doan_cua_mau({"segmentStart": 120, "segmentEnd": 240, "durationSec": 120}) == (120.0, 240.0)
    assert ban_chuan.doan_cua_mau({}) == (0.0, None)
    assert ban_chuan.doan_cua_mau(None) == (0.0, None)


def test_doc_event_samples_ingested_v2():
    env = {
        "eventId": str(uuid.uuid4()), "eventType": "samples.ingested", "version": 2,
        "occurredAt": "2026-11-14T00:00:00Z", "producer": "project-svc", "correlationId": str(uuid.uuid4()),
        "causationId": None, "actor": None,
        "payload": {"projectId": str(uuid.uuid4()), "watermarkText": CHU,
                    "samples": [{"sampleId": str(uuid.uuid4()), "modality": "video", "storageKey": "p/x.mp4",
                                 "content": None, "metadata": {"durationSec": 3}}]},
    }
    p = env_mod.doc(json.dumps(env).encode(), SamplesIngested).payload
    assert p.samples[0].storage_key == "p/x.mp4" and p.watermark_text == CHU


def test_moi_file_video_mot_viec_kem_cac_doan():
    p = SamplesIngested(project_id=uuid.uuid4(), watermark_text=CHU, samples=[
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/a.mp4", "content": None,
         "metadata": {"segmentStart": 0, "segmentEnd": 120, "durationSec": 120}},
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/a.mp4", "content": None,
         "metadata": {"segmentStart": 120, "segmentEnd": 200.5, "durationSec": 80.5}},
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/b.mp4", "content": None, "metadata": {"durationSec": 9}},
        {"sample_id": uuid.uuid4(), "modality": "image", "storage_key": "p/c.png", "content": None, "metadata": {}},
    ])
    assert ban_chuan.cac_viec(p) == [
        {"storageKey": "p/a.mp4", "doan": [[0.0, 120.0], [120.0, 200.5]], "chu": CHU},
        {"storageKey": "p/b.mp4", "doan": [[0.0, 9.0]], "chu": CHU},
    ]


def test_cat_cac_doan_bo_qua_doan_da_co(tmp_path: Path, monkeypatch):
    da_co = {("p/a.mp4", 0.0, 120.0)}
    da_cat: list[tuple[float, float]] = []
    goc = tmp_path / "goc.mp4"
    goc.write_bytes(b"x")
    monkeypatch.setattr(hls, "da_co", lambda k, a, b: (k, a, b) in da_co)
    monkeypatch.setattr(hls, "tai_goc", lambda k: goc)
    monkeypatch.setattr(hls, "cat_va_luu", lambda g, k, a, b, chu: da_cat.append((a, b)))

    assert hls.cat_cac_doan("p/a.mp4", [(0.0, 120.0), (120.0, 200.5)], CHU) == 1
    assert da_cat == [(120.0, 200.5)]
    assert not goc.exists()     # file goc tam bi xoa sau khi cat


def test_khong_doan_nao_moi_thi_khong_tai_file_goc(monkeypatch):
    monkeypatch.setattr(hls, "da_co", lambda k, a, b: True)
    monkeypatch.setattr(hls, "tai_goc", lambda k: (_ for _ in ()).throw(AssertionError("khong duoc tai")))
    assert hls.cat_cac_doan("p/a.mp4", [(0.0, 10.0)], CHU) == 0
