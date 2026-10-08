"""Xac thuc JWT do identity-svc ky (RS256), lay khoa cong khai tu JWKS.

Cung quy uoc claim voi C# (CrowdClaims): sub = userId, role = vai tro (co the
nhieu gia tri). Gateway da kiem token mot lan; service van kiem lai (khong tin
mang noi bo — docs muc 3.8).
"""

import uuid
from dataclasses import dataclass

import jwt
from fastapi import Depends, Request

from app.config import settings
from app.errors import LoiApi

_jwks: jwt.PyJWKClient | None = None


def _khach_jwks() -> jwt.PyJWKClient:
    global _jwks
    if _jwks is None:
        # Cache khoa 5 phut; khoa xoay vong (kid moi) thi tu tai lai.
        _jwks = jwt.PyJWKClient(settings().jwks_url, cache_keys=True, lifespan=300)
    return _jwks


@dataclass(frozen=True)
class NguoiGoi:
    user_id: uuid.UUID
    roles: frozenset[str]

    @property
    def la_admin(self) -> bool:
        return "admin" in self.roles


def nguoi_goi(request: Request) -> NguoiGoi:
    header = request.headers.get("Authorization", "")
    if not header.startswith("Bearer "):
        raise LoiApi(401, "chua_dang_nhap", "Thieu token.")

    token = header[len("Bearer "):]
    cfg = settings()
    try:
        khoa = _khach_jwks().get_signing_key_from_jwt(token)
        claims = jwt.decode(token, khoa.key, algorithms=["RS256"], audience=cfg.jwt_audience, issuer=cfg.jwt_issuer)
    except jwt.PyJWTError as ex:
        raise LoiApi(401, "token_khong_hop_le", "Token khong hop le hoac da het han.") from ex

    vai_tro = claims.get("role", [])
    if isinstance(vai_tro, str):
        vai_tro = [vai_tro]

    try:
        uid = uuid.UUID(claims["sub"])
    except (KeyError, ValueError) as ex:
        raise LoiApi(401, "token_khong_hop_le", "Token thieu sub.") from ex

    return NguoiGoi(user_id=uid, roles=frozenset(vai_tro))


def can_vai_tro(*vai_tro: str):
    """Dependency: nguoi goi phai co mot trong cac vai tro — sai vai tro la 403 (RBAC)."""

    def kiem(ng: NguoiGoi = Depends(nguoi_goi)) -> NguoiGoi:
        if not ng.roles.intersection(vai_tro):
            raise LoiApi(403, "khong_co_quyen", "Vai tro khong duoc phep.")
        return ng

    return kiem
