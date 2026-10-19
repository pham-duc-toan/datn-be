"""Ket noi Postgres (pgvector) + migration SQL thuan + bo dem vector.

Giong quality-svc: SQL ro rang, SQLAlchemy chi lo pool ket noi va transaction.
"""

import logging
from pathlib import Path
from typing import Protocol

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncEngine, create_async_engine

from app.config import settings

log = logging.getLogger(__name__)

THU_MUC_MIGRATION = Path(__file__).resolve().parent.parent / "migrations"

_engine: AsyncEngine | None = None


def engine() -> AsyncEngine:
    global _engine
    if _engine is None:
        _engine = create_async_engine(settings().database_url, pool_size=5, max_overflow=5, pool_pre_ping=True)
    return _engine


async def chay_migration() -> None:
    """Chay cac file migrations/NNN_*.sql chua chay, theo thu tu ten. Moi file mot transaction."""
    async with engine().begin() as conn:
        await conn.execute(text(
            "CREATE TABLE IF NOT EXISTS schema_migrations ("
            " version varchar(200) PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())"))

    async with engine().connect() as conn:
        da_chay = {r[0] for r in (await conn.execute(text("SELECT version FROM schema_migrations"))).all()}

    for file in sorted(THU_MUC_MIGRATION.glob("*.sql")):
        if file.name in da_chay:
            continue
        sql = file.read_text(encoding="utf-8")
        async with engine().begin() as conn:
            raw = await conn.get_raw_connection()
            await raw.driver_connection.execute(sql)
            await conn.execute(text("INSERT INTO schema_migrations (version) VALUES (:v)"), {"v": file.name})
        log.info("Da chay migration %s", file.name)


async def dong() -> None:
    global _engine
    if _engine is not None:
        await _engine.dispose()
    _engine = None


class KhoVector(Protocol):
    """Bo dem vector theo khoa bam. Test dung ban trong bo nho."""

    async def lay(self, khoa: list[bytes]) -> dict[bytes, list[float]]: ...

    async def luu(self, model: str, ban_ghi: dict[bytes, list[float]]) -> None: ...


def _chuoi_vector(v: list[float]) -> str:
    return "[" + ",".join(f"{x:.7f}" for x in v) + "]"


def _doc_vector(s: str) -> list[float]:
    return [float(x) for x in s.strip("[]").split(",")] if s and s != "[]" else []


class KhoPostgres:
    async def lay(self, khoa: list[bytes]) -> dict[bytes, list[float]]:
        if not khoa:
            return {}
        async with engine().begin() as conn:
            dong_ = (await conn.execute(
                text("UPDATE embedding_cache SET last_used_at = now() WHERE text_hash = ANY(:k) "
                     "RETURNING text_hash, embedding::text AS v"), {"k": khoa})).all()
        return {bytes(d.text_hash): _doc_vector(d.v) for d in dong_}

    async def luu(self, model: str, ban_ghi: dict[bytes, list[float]]) -> None:
        if not ban_ghi:
            return
        async with engine().begin() as conn:
            for k, v in ban_ghi.items():
                await conn.execute(
                    text("INSERT INTO embedding_cache (text_hash, model, dim, embedding) "
                         "VALUES (:k, :m, :d, CAST(:v AS vector)) ON CONFLICT (text_hash) DO NOTHING"),
                    {"k": k, "m": model, "d": len(v), "v": _chuoi_vector(v)})


class KhoBoNho:
    """Bo dem trong bo nho (test, hoac chay ml-svc khong co DB)."""

    def __init__(self) -> None:
        self.du_lieu: dict[bytes, list[float]] = {}

    async def lay(self, khoa: list[bytes]) -> dict[bytes, list[float]]:
        return {k: self.du_lieu[k] for k in khoa if k in self.du_lieu}

    async def luu(self, model: str, ban_ghi: dict[bytes, list[float]]) -> None:
        for k, v in ban_ghi.items():
            self.du_lieu.setdefault(k, v)
