"""Cau hinh doc tu bien moi truong (tien to QUALITY_). Mac dinh = moi truong dev.

Vi du: QUALITY_DATABASE_URL, QUALITY_RABBIT_HOST, QUALITY_DS_INTERVAL_SECONDS.
"""

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="QUALITY_", env_file=".env", extra="ignore")

    service_name: str = "quality-svc"
    port: int = 8201

    # Giong ASPNETCORE_ENVIRONMENT: production PHAI dat QUALITY_ENVIRONMENT=Production.
    environment: str = "Development"
    # Seed ban sao du an cua kich ban dev — chi chay khi environment = Development.
    seed_enabled: bool = True

    # Postgres rieng cua quality (docker-compose.infra.yml, profile p3).
    database_url: str = "postgresql+asyncpg://quality_user:dev_quality_pw@localhost:5421/quality_db"

    # RabbitMQ — cung exchange voi cac service C#.
    rabbit_host: str = "localhost"
    rabbit_port: int = 5672
    rabbit_user: str = "datn"
    rabbit_password: str = "dev_rabbit_pw"
    rabbit_vhost: str = "/"
    exchange: str = "datn.events"
    dead_letter_exchange: str = "datn.dlx"
    # So lan giao toi da truoc khi vao DLQ (VD-D-06) — giong Consumers:DeliveryLimit cua C#.
    # La tham so TOPOLOGY cua queue (doi la phai xoa queue) nen o lai bien moi truong.
    delivery_limit: int = 5

    # JWT do identity-svc ky (RS256).
    jwt_issuer: str = "crowd-identity"
    jwt_audience: str = "crowd-api"
    jwks_url: str = "http://localhost:8101/.well-known/jwks.json"

    # Catalog setting (kieu / mac dinh / gioi han). Trong: dung shared/settings/catalog.json cua repo.
    # Chu ky DS, trong so uy tin, outbox, prefetch... la SETTING DONG (admin-svc) — xem settings_store.py.
    settings_catalog_path: str | None = None

    # project-svc noi bo: bu ban sao du an khi thieu (du an publish truoc khi quality-svc ton tai).
    # Trong = khong bu (bo qua dong thuan kem canh bao nhu truoc).
    project_url: str | None = None
    internal_api_key: str | None = None

    # ml-svc noi bo: nhung cau tra loi mo de cham theo nghia (3.19). Trong = cham cau tra loi mo bang so chu.
    ml_url: str | None = None


# S-08: gia tri DEV cong khai trong repo — chi duoc dung o moi truong Development.
_KHOA_DEV = ("pgZwtqPVxGvu3oA5esvFaUio+ltJ2sSfgOWqYQNHwyU=", "TUmrnIsOHHdHl7BEEngcDqPfPQW6v956RTfgOEOlILo=", "demo_internal_key")


def vi_pham_bi_mat_dev(cfg: BaseSettings) -> list[str]:
    """Ten truong cau hinh dang mang mat khau / khoa dev. Ham thuan — test duoc."""
    import re
    ra = []
    for ten, gia_tri in cfg.model_dump().items():
        if isinstance(gia_tri, str) and (re.search(r"dev_[a-z0-9]+_pw", gia_tri, re.I) or any(k in gia_tri for k in _KHOA_DEV)):
            ra.append(ten)
    return sorted(ra)


@lru_cache
def settings() -> Settings:
    cfg = Settings()
    if cfg.environment != "Development":
        vi_pham = vi_pham_bi_mat_dev(cfg)
        if vi_pham:
            # Fail closed: khong chay moi truong that voi bi mat dev.
            raise RuntimeError("S-08: moi truong " + cfg.environment + " dang dung mat khau / khoa DEV o: " + ", ".join(vi_pham))
    return cfg
