"""Token link xem mau — giai ma token do service C# tao (Crowd.BuildingBlocks.Storage.MediaGrant).

token = base64url(nonce 12 byte | ban ma | tag 16 byte), AES-256-GCM, AAD "crowd-media-grant-v1".
Ma hoa (khong chi ky) vi trong token co khoa MinIO — client khong duoc biet (FP-03). Truong tuy chon
(wt, ws, a, b — watermark noi va doan video) khong co thi la None: token kieu cu van doc duoc. Ai khong co
khoa thi khong tao / sua / doc duoc token: sai mot ky tu la giai ma that bai.
"""

import base64
import binascii
import json
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

AAD = b"crowd-media-grant-v1"
DO_DAI_NONCE = 12
PHIEN_BAN = 1
CHE_DO = ("original", "watermarked")


class TokenKhongHopLe(Exception):
    """Token sai / bi sua / khac khoa / sai dinh dang."""


@dataclass(frozen=True)
class QuyenXem:
    grant_id: uuid.UUID
    sample_id: uuid.UUID
    project_id: uuid.UUID
    storage_key: str
    modality: str
    user_id: uuid.UUID | None
    guest_session_id: uuid.UUID | None
    role: str
    mode: str
    issued_at: datetime
    expires_at: datetime
    # Watermark noi (anh / video) do C# dung san: "wt" chu cua nguoi xem, "ws" chu dung chung phan giua video.
    chu_ca_nhan: str | None = None
    chu_chung: str | None = None
    # Video: doan cua mau trong file (giay) — danh sach phat HLS chi gom doan nay.
    bat_dau: float | None = None
    ket_thuc: float | None = None

    def het_han(self, luc: datetime) -> bool:
        return luc >= self.expires_at


def doc_khoa(base64_khoa: str) -> bytes:
    try:
        khoa = base64.b64decode(base64_khoa, validate=True)
    except (binascii.Error, ValueError) as ex:
        raise ValueError("Khoa phai la base64 cua 32 byte") from ex
    if len(khoa) != 32:
        raise ValueError("Khoa phai dai dung 32 byte (AES-256)")
    return khoa


def _tu_base64url(s: str) -> bytes:
    return base64.urlsafe_b64decode(s + "=" * (-len(s) % 4))


def giai_ma_tho(khoa: bytes, token: str) -> bytes:
    try:
        du = _tu_base64url(token)
    except (binascii.Error, ValueError) as ex:
        raise TokenKhongHopLe("token khong phai base64url") from ex
    if len(du) < DO_DAI_NONCE + 16:
        raise TokenKhongHopLe("token qua ngan")
    try:
        return AESGCM(khoa).decrypt(du[:DO_DAI_NONCE], du[DO_DAI_NONCE:], AAD)
    except InvalidTag as ex:
        raise TokenKhongHopLe("token bi sua hoac khac khoa") from ex


def _uuid_hoac_none(v: object) -> uuid.UUID | None:
    return None if v is None else uuid.UUID(str(v))


def _chuoi_hoac_none(v: object) -> str | None:
    return None if v is None else str(v)


def giai_ma(khoa: bytes, token: str) -> QuyenXem:
    noi_dung = giai_ma_tho(khoa, token)
    try:
        d = json.loads(noi_dung)
        if d["v"] != PHIEN_BAN or d["m"] not in CHE_DO:
            raise TokenKhongHopLe("phien ban / che do khong ho tro")
        return QuyenXem(
            grant_id=uuid.UUID(d["g"]),
            sample_id=uuid.UUID(d["s"]),
            project_id=uuid.UUID(d["p"]),
            storage_key=str(d["k"]),
            modality=str(d["mo"]),
            user_id=_uuid_hoac_none(d["u"]),
            guest_session_id=_uuid_hoac_none(d["gs"]),
            role=str(d["r"]),
            mode=str(d["m"]),
            issued_at=datetime.fromtimestamp(int(d["iat"]), timezone.utc),
            expires_at=datetime.fromtimestamp(int(d["exp"]), timezone.utc),
            chu_ca_nhan=_chuoi_hoac_none(d.get("wt")),
            chu_chung=_chuoi_hoac_none(d.get("ws")),
            bat_dau=None if d.get("a") is None else float(d["a"]),
            ket_thuc=None if d.get("b") is None else float(d["b"]),
        )
    except (KeyError, ValueError, TypeError) as ex:
        raise TokenKhongHopLe("noi dung token sai dinh dang") from ex
