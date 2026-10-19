"""Cau hinh doc tu bien moi truong (tien to ML_). Mac dinh = moi truong dev.

Vi du: ML_DATABASE_URL, ML_INTERNAL_API_KEY, ML_MODEL_NAME, ML_MODEL_CACHE_DIR.
"""

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="ML_", env_file=".env", extra="ignore", protected_namespaces=())

    service_name: str = "ml-svc"
    port: int = 8202

    # Giong ASPNETCORE_ENVIRONMENT: production PHAI dat ML_ENVIRONMENT=Production.
    environment: str = "Development"

    # Postgres rieng cua ml (pgvector) — docker-compose.infra.yml, profile p4.
    database_url: str = "postgresql+asyncpg://ml_user:dev_ml_pw@localhost:5422/ml_db"

    # Khoa goi noi bo (X-Internal-Key) — chi service trong mang noi bo goi duoc, khong qua gateway.
    internal_api_key: str = "demo_internal_key"

    # Mo hinh nhung cau da ngon ngu (co tieng Viet), 384 chieu, ~0,22 GB, chay CPU.
    model_name: str = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
    # Thu muc mo hinh (image Docker tai san luc build). Trong = thu muc mac dinh cua fastembed.
    model_cache_dir: str | None = None

    # Gioi han mot lan goi: so cau, so ky tu moi cau.
    max_texts: int = 256
    max_chars: int = 5000


_KHOA_DEV = ("demo_internal_key",)


def vi_pham_bi_mat_dev(cfg: BaseSettings) -> list[str]:
    """Ten truong cau hinh dang mang mat khau / khoa dev (S-08). Ham thuan — test duoc."""
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
            raise RuntimeError("S-08: moi truong " + cfg.environment + " dang dung mat khau / khoa DEV o: " + ", ".join(vi_pham))
    return cfg
