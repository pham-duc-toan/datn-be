"""MinIO (giao thuc S3): doc anh goc, ghi anh da che, phat luong file am thanh / video.

Thu vien minio la dong bo — goi qua asyncio.to_thread o noi dung trong async.
"""

from collections.abc import Iterator
from dataclasses import dataclass
from functools import lru_cache

from minio import Minio
from minio.error import S3Error

from app.config import settings

KHUC = 256 * 1024


@dataclass(frozen=True)
class ThongTin:
    size: int
    content_type: str | None


class KhongCoFile(Exception):
    pass


class Kho:
    def __init__(self) -> None:
        cfg = settings()
        self._bucket = cfg.minio_bucket
        self._client = Minio(cfg.minio_endpoint, access_key=cfg.minio_access_key,
                             secret_key=cfg.minio_secret_key, secure=cfg.minio_secure)

    def thong_tin(self, key: str) -> ThongTin:
        try:
            st = self._client.stat_object(self._bucket, key)
        except S3Error as ex:
            if ex.code in ("NoSuchKey", "NoSuchObject"):
                raise KhongCoFile(key) from ex
            raise
        return ThongTin(size=st.size or 0, content_type=st.content_type)

    def doc(self, key: str) -> bytes:
        try:
            r = self._client.get_object(self._bucket, key)
        except S3Error as ex:
            if ex.code in ("NoSuchKey", "NoSuchObject"):
                raise KhongCoFile(key) from ex
            raise
        try:
            return r.read()
        finally:
            r.close()
            r.release_conn()

    def ghi(self, key: str, du_lieu: bytes, content_type: str) -> None:
        import io

        self._client.put_object(self._bucket, key, io.BytesIO(du_lieu), len(du_lieu), content_type=content_type)

    def xoa(self, key: str) -> None:
        self._client.remove_object(self._bucket, key)

    def luong(self, key: str, bat_dau: int, do_dai: int) -> Iterator[bytes]:
        """Doc tung khuc [bat_dau, bat_dau + do_dai) — cho Range request (tua video)."""
        r = self._client.get_object(self._bucket, key, offset=bat_dau, length=do_dai)
        try:
            yield from r.stream(KHUC)
        finally:
            r.close()
            r.release_conn()


@lru_cache
def kho() -> Kho:
    return Kho()
