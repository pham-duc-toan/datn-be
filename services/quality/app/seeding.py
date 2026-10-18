"""Seed ban sao du an + bai nop + ket qua cham cua kich ban dev (shared/seeding/quality-seed.json).

Kich ban seed nam o C# (KichBanSeed); test QualitySeedSnapshotTests xuat phan
quality can ra JSON. Giong cac service C#: chi chay khi moi truong Development
VA bat co seed; du an da co thi bo qua (chay lai an toan).
"""

import json
import logging
import uuid
from pathlib import Path

from sqlalchemy import text

from app import processors
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
                    "INSERT INTO projects (project_id, owner_id, modality, label_schema, pass_threshold_percent, slots, question_count, published_at) "
                    "VALUES (:id, :owner, :m, CAST(:schema AS jsonb), :t, :slots, :q, now() - make_interval(secs => :ago)) "
                    "ON CONFLICT (project_id) DO NOTHING"
                ),
                {"id": p["projectId"], "owner": p["ownerId"], "m": p["modality"], "schema": json.dumps(p["labelSchema"]),
                 "t": p["passThresholdPercent"], "slots": p["slots"], "q": p["questionCount"], "ago": float(p["createdAgoSeconds"])},
            )
            if not r.rowcount:
                continue
            so += 1
            await _seed_bai(s, p)

    log.info("Seed quality: them %s / %s du an", so, len(du_an))


async def _seed_bai(s, p: dict) -> None:
    """Bai da nop (+ nhan) cua du an; du an Completed them ket qua cham — dung so lieu kich ban C#."""
    for b in p["attempts"]:
        await s.execute(
            text(
                "INSERT INTO attempts (attempt_id, project_id, labeler_id, sample_ids, answered_count, gold_total, gold_correct, started_at, submitted_at) "
                "VALUES (:a, :p, :l, :ids, :n, :gt, :gd, now() - make_interval(secs => :st), now() - make_interval(secs => :sb))"
            ),
            {"a": b["attemptId"], "p": p["projectId"], "l": b["labelerId"], "ids": [uuid.UUID(x) for x in b["sampleIds"]], "n": len(b["labels"]),
             "gt": b["goldTotal"], "gd": b["goldCorrect"], "st": float(b["startedAgoSeconds"]), "sb": float(b["submittedAgoSeconds"])},
        )
        for n in b["labels"]:
            await s.execute(
                text(
                    "INSERT INTO labels (annotation_id, project_id, attempt_id, sample_id, labeler_id, data, sample_metadata, received_at) "
                    "VALUES (:id, :p, :a, :sm, :l, CAST(:d AS jsonb), CAST(:md AS jsonb), now() - make_interval(secs => :sb))"
                ),
                {"id": processors.id_nhan(uuid.UUID(b["attemptId"]), uuid.UUID(n["sampleId"])), "p": p["projectId"],
                 "a": b["attemptId"], "sm": n["sampleId"], "l": b["labelerId"], "d": json.dumps(n["data"]),
                 "md": json.dumps(n["sampleMetadata"]), "sb": float(b["submittedAgoSeconds"])},
            )

    if "gradedAgoSeconds" not in p:
        return

    await s.execute(
        text(
            "INSERT INTO project_collections (project_id, reason, attempt_ids, excluded_sample_ids, finished_at, graded_at, majority_used) "
            "VALUES (:p, :r, :a, '{}', now() - make_interval(secs => :g), now() - make_interval(secs => :g), :mj)"
        ),
        {"p": p["projectId"], "r": "du_suat" if len(p["attempts"]) >= p["slots"] else "het_han",
         "a": [uuid.UUID(b["attemptId"]) for b in p["attempts"]], "g": float(p["gradedAgoSeconds"]), "mj": p["majorityUsed"]},
    )
    for b in p["attempts"]:
        kq = b["result"]
        await s.execute(
            text(
                "INSERT INTO attempt_grades (attempt_id, project_id, labeler_id, comparable_count, matched_count, agreement_percent, "
                "gold_total, gold_correct, passed, answered_count, graded_at) "
                "VALUES (:a, :p, :l, :c, :m, :pt, :gt, :gd, :ok, :n, now() - make_interval(secs => :g))"
            ),
            {"a": b["attemptId"], "p": p["projectId"], "l": b["labelerId"], "c": kq["comparableCount"], "m": kq["matchedCount"],
             "pt": kq["agreementPercent"], "gt": b["goldTotal"], "gd": b["goldCorrect"], "ok": kq["passed"],
             "n": len(b["labels"]), "g": float(p["gradedAgoSeconds"])},
        )
    await s.execute(
        text("UPDATE projects SET closed_at = now() - make_interval(secs => :c) WHERE project_id = :p"),
        {"p": p["projectId"], "c": float(p["completedAgoSeconds"])},
    )
