"""Seed ban sao du an cua kich ban dev (shared/seeding/quality-seed.json).

Kich ban seed nam o C# (KichBanSeed); test QualitySeedSnapshotTests xuat phan
quality can ra JSON. Giong cac service C#: chi chay khi moi truong Development
VA bat co seed; du an da co thi bo qua (chay lai an toan).
"""

import json
import logging
from pathlib import Path

from sqlalchemy import text

from app.config import settings
from app.db import session_factory

log = logging.getLogger(__name__)

FILE_SEED = Path(__file__).resolve().parents[3] / "shared" / "seeding" / "quality-seed.json"


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
            r = await s.execute(
                text(
                    "INSERT INTO projects (project_id, owner_id, modality, label_schema, redundancy, max_redundancy, published_at) "
                    "VALUES (:id, :owner, :m, CAST(:schema AS jsonb), :r, :max_r, now()) ON CONFLICT (project_id) DO NOTHING"
                ),
                {"id": p["projectId"], "owner": p["ownerId"], "m": p["modality"], "schema": json.dumps(p["labelSchema"]),
                 "r": p["redundancy"], "max_r": p["maxRedundancy"]},
            )
            so += r.rowcount

    log.info("Seed quality: them %s / %s du an", so, len(du_an))
