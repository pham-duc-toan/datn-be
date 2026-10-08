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


@lru_cache
def settings() -> Settings:
    return Settings()
