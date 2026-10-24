"""Cau hinh doc tu bien moi truong (tien to MEDIA_). Mac dinh = moi truong dev.

Vi du: MEDIA_MINIO_ENDPOINT, MEDIA_GRANT_KEY, MEDIA_WATERMARK_KEY. media-svc KHONG co database:
watermark nhung thang nguoi xem vao anh, setting giu trong bo nho (settings_listener.py).
Chi thu gan voi HA TANG o day (ket noi, khoa bi mat); bat / tat va do manh watermark la SETTING
DONG cua admin-svc (settings_store.py).
"""

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="MEDIA_", extra="ignore")

    service_name: str = "media-svc"
    port: int = 8109
    environment: str = "Development"

    rabbit_host: str = "localhost"
    rabbit_port: int = 5672
    rabbit_user: str = "datn"
    rabbit_password: str = "dev_rabbit_pw"
    rabbit_vhost: str = "/"
    exchange: str = "datn.events"
    dead_letter_exchange: str = "datn.dlx"
    delivery_limit: int = 5

    jwt_issuer: str = "crowd-identity"
    jwt_audience: str = "crowd-api"
    jwks_url: str = "http://localhost:8101/.well-known/jwks.json"

    # MinIO: doc file mau (bucket private) de phat qua proxy.
    minio_endpoint: str = "localhost:9000"
    minio_access_key: str = "datn"
    minio_secret_key: str = "dev_minio_pw"
    minio_secure: bool = False
    minio_bucket: str = "datasets"

    # Khoa AES-256 (base64) dung CHUNG voi cac service C# tao link (MediaLinks:GrantKey).
    grant_key: str = "pgZwtqPVxGvu3oA5esvFaUio+ltJ2sSfgOWqYQNHwyU="
    # Khoa bi mat cua watermark (base64): quyet dinh khoi DCT nao mang bit nao. Phai GIU NGUYEN mai
    # mai — doi thi anh da phat truoc do khong giai ma duoc nua. Tach khoi grant_key de xoay vong
    # grant_key khong anh huong.
    watermark_key: str = "TUmrnIsOHHdHl7BEEngcDqPfPQW6v956RTfgOEOlILo="

    settings_catalog_path: str | None = None

    # Bo dem dia cho ban am thanh / video da watermark (bo_dem.py) — ha tang, khong phai setting dong.
    av_cache_dir: str = "/tmp/media-wm"
    av_cache_ttl_seconds: int = 6 * 3600
    av_cache_max_bytes: int = 5 * 1024 * 1024 * 1024


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
