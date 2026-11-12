"""Seed KET QUA cua quality cho kich ban dev (shared/seeding/quality-seed.json).

Kich ban seed nam o C# (KichBanSeed); test QualitySeedSnapshotTests xuat phan quality can ra JSON. De bai, bai
nop va nhan da seed o project-svc — quality doc qua gRPC (kien truc 3.22), nen o day chi con: voi du an
Completed, moc cham (project_gradings, ds_at NULL → worker viec_cho chay Dawid-Skene lan dau roi tinh lai uy
tin labeler bi anh huong) + moc dong. Du an dang chay chua cham thi chua co DS. Bang chung uy tin (so cau so
duoc / giong / cau vang) nam tren project.attempts da seed — khong chep ve day.
Giong cac service C#: chi chay khi moi truong Development VA bat co seed; da co thi bo qua (chay lai an toan).
"""

import json
import logging
import uuid
from pathlib import Path

from sqlalchemy import text

from app.config import settings
from app.db import session_factory

log = logging.getLogger(__name__)

FILE_SEED = Path(__file__).resolve().parents[3] / "shared" / "seeding" / "quality-seed.json"

# correlation_id cua lan cham seed (khong co event goc).
KHONG_GIAN_SEED = uuid.UUID("6f1d3c9e-2b7a-4f55-9a43-0c8e1d2b7a01")


async def chay() -> None:
    cfg = settings()
    if cfg.environment != "Development" or not cfg.seed_enabled:
        return

    if not FILE_SEED.exists():
        log.warning("Seed quality: khong thay %s — bo qua", FILE_SEED)
        return

    du_an = json.loads(FILE_SEED.read_text(encoding="utf-8"))["projects"]
    so = 0
    async with session_factory()() as s, s.begin():
        for p in du_an:
            if "gradedAgoSeconds" in p and await _seed_ket_qua(s, p):
                so += 1

    log.info("Seed quality: them ket qua cham %s du an", so)


async def _seed_ket_qua(s, p: dict) -> bool:
    """Du an Completed: ket qua cham dung so lieu kich ban C#. Da co thi bo qua."""
    r = await s.execute(
        text(
            # reputation_at = graded_at: kich ban seed da co uy tin san (identity / labeler_cache) — khong tinh lai.
            "INSERT INTO project_gradings (project_id, requested_at, correlation_id, graded_at, majority_used, reputation_at) "
            "VALUES (:p, now() - make_interval(secs => :g), :c, now() - make_interval(secs => :g), :mj, "
            "now() - make_interval(secs => :g)) "
            "ON CONFLICT (project_id) DO NOTHING"
        ),
        {"p": p["projectId"], "g": float(p["gradedAgoSeconds"]), "c": uuid.uuid5(KHONG_GIAN_SEED, p["projectId"]),
         "mj": p["majorityUsed"]},
    )
    if not r.rowcount:
        return False

    await s.execute(
        text("INSERT INTO project_archives (project_id, closed_at) VALUES (:p, now() - make_interval(secs => :c)) "
             "ON CONFLICT (project_id) DO NOTHING"),
        {"p": p["projectId"], "c": float(p["completedAgoSeconds"])},
    )
    return True
