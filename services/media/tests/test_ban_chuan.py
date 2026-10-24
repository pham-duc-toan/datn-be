"""Ban chuan video: khoa MinIO, lay ban co san / chuan hoa tai cho, doc event dataset.ingested."""

import json
import uuid
from pathlib import Path

from app import ban_chuan
from app.contracts import DatasetIngested
from app.messaging import envelope as env_mod
from app.storage import KhongCoFile


def test_khoa_ban_chuan_cung_tien_to_du_an():
    assert ban_chuan.khoa_ban_chuan("p1/abc.mov") == "p1/_wm/chuan-v2/abc.mov.mp4"
    assert ban_chuan.khoa_ban_chuan("abc.mp4") == "_wm/chuan-v2/abc.mp4.mp4"


class _KhoGia:
    def __init__(self, co: set[str]) -> None:
        self.co = co
        self.da_len: list[str] = []

    def tai_ve(self, key: str, duong: str) -> None:
        if key not in self.co:
            raise KhongCoFile(key)
        Path(duong).write_bytes(key.encode())

    def tai_len(self, key: str, duong: str, loai: str) -> None:
        self.da_len.append(key)


def test_co_san_tren_minio_thi_khong_chuan_hoa(tmp_path: Path, monkeypatch):
    k = _KhoGia({"p1/_wm/chuan-v2/v.mp4.mp4"})
    monkeypatch.setattr(ban_chuan, "kho", lambda: k)
    monkeypatch.setattr(ban_chuan.watermark_av, "chuan_hoa_video", lambda *_: (_ for _ in ()).throw(AssertionError()))
    ban_chuan.tao_ban_chuan("p1/v.mp4", tmp_path / "c.mp4")
    assert (tmp_path / "c.mp4").read_bytes() == b"p1/_wm/chuan-v2/v.mp4.mp4"
    assert k.da_len == []


def test_chua_co_thi_chuan_hoa_roi_ghi_len(tmp_path: Path, monkeypatch):
    k = _KhoGia({"p1/v.mp4"})
    monkeypatch.setattr(ban_chuan, "kho", lambda: k)
    monkeypatch.setattr(ban_chuan.watermark_av, "chuan_hoa_video", lambda nguon, dich: Path(dich).write_bytes(b"chuan"))
    ban_chuan.tao_ban_chuan("p1/v.mp4", tmp_path / "c.mp4")
    assert (tmp_path / "c.mp4").read_bytes() == b"chuan"
    assert k.da_len == ["p1/_wm/chuan-v2/v.mp4.mp4"]


def test_doc_event_dataset_ingested():
    env = {
        "eventId": str(uuid.uuid4()), "eventType": "dataset.ingested", "version": 1,
        "occurredAt": "2026-10-24T00:00:00Z", "producer": "project-svc", "correlationId": str(uuid.uuid4()),
        "causationId": None, "actor": None,
        "payload": {"projectId": str(uuid.uuid4()), "datasetId": str(uuid.uuid4()), "batchIndex": 0, "batchCount": 1,
                    "samples": [{"sampleId": str(uuid.uuid4()), "modality": "video", "storageKey": "p/x.mp4",
                                 "content": None, "metadata": {"durationSec": 3}}]},
    }
    p = env_mod.doc(json.dumps(env).encode(), DatasetIngested).payload
    assert p.samples[0].storage_key == "p/x.mp4"


def test_moi_file_video_mot_viec():
    p = DatasetIngested(project_id=uuid.uuid4(), dataset_id=uuid.uuid4(), batch_index=0, batch_count=1, samples=[
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/a.mp4", "content": None, "metadata": {}},
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/a.mp4", "content": None, "metadata": {}},
        {"sample_id": uuid.uuid4(), "modality": "video", "storage_key": "p/b.mp4", "content": None, "metadata": {}},
        {"sample_id": uuid.uuid4(), "modality": "image", "storage_key": "p/c.png", "content": None, "metadata": {}},
    ])
    assert ban_chuan.cac_viec(p) == ["p/a.mp4", "p/b.mp4"]
