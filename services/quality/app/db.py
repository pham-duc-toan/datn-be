"""Ket noi Postgres + chay migration SQL thuan luc khoi dong.

Khong dung ORM cho nghiep vu: cac cau truy van cua quality chu yeu la gom nhom /
thong ke, viet SQL ro rang hon. SQLAlchemy chi lo pool ket noi va transaction.
"""

import logging
import re
from pathlib import Path

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker, create_async_engine

from app.config import settings

log = logging.getLogger(__name__)

THU_MUC_MIGRATION = Path(__file__).resolve().parent.parent / "migrations"

_engine: AsyncEngine | None = None
_session_factory: async_sessionmaker[AsyncSession] | None = None


def engine() -> AsyncEngine:
    global _engine
    if _engine is None:
        _engine = create_async_engine(settings().database_url, pool_size=10, max_overflow=5, pool_pre_ping=True)
    return _engine


def session_factory() -> async_sessionmaker[AsyncSession]:
    global _session_factory
    if _session_factory is None:
        _session_factory = async_sessionmaker(engine(), expire_on_commit=False)
    return _session_factory


_TEN_ROLE = re.compile(r"^[a-z_][a-z0-9_]{0,62}$")


async def dam_bao_role_doc() -> int:
    """Ban Python cua ReaderRoles.DamBaoDangNhapAsync (C#): bat LOGIN + dat mat khau cho role chi doc view
    (migration da tao role NOLOGIN + GRANT tren view). Mat khau tu cau hinh, khong nam trong migration."""
    so = 0
    async with engine().begin() as conn:
        raw = await conn.get_raw_connection()
        for role, mat_khau in settings().read_access_readers.items():
            if not mat_khau:
                continue
            if not _TEN_ROLE.match(role):
                raise ValueError(f"Ten role doc cheo khong hop le: {role!r}")
            mk = mat_khau.replace("'", "''")
            await raw.driver_connection.execute(
                f"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN "
                f"CREATE ROLE {role} LOGIN PASSWORD '{mk}'; ELSE ALTER ROLE {role} LOGIN PASSWORD '{mk}'; END IF; END $$;")
            so += 1
    if so:
        log.info("Doc cheo: bat dang nhap cho %s role chi doc view", so)
    return so


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
            # asyncpg chay duoc ca kich ban nhieu cau lenh khi KHONG co tham so —
            # di qua ket noi goc vi SQLAlchemy chuan bi (prepare) tung cau mot.
            raw = await conn.get_raw_connection()
            await raw.driver_connection.execute(sql)
            await conn.execute(text("INSERT INTO schema_migrations (version) VALUES (:v)"), {"v": file.name})
        log.info("Da chay migration %s", file.name)


async def dong() -> None:
    global _engine, _session_factory
    if _engine is not None:
        await _engine.dispose()
    _engine = None
    _session_factory = None
