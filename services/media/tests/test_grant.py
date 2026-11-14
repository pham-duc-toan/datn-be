"""Token link xem: giai DUNG vector ma test C# (MediaGrantTests) tao ra — hai ngon ngu phai khop."""

import uuid
from datetime import datetime, timezone

import pytest

from app.grant import TokenKhongHopLe, doc_khoa, giai_ma

KHOA_VECTOR = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
TOKEN_VECTOR = (
    "ZGVmZ2hpamtsbW5vMzmoREPYerxZQGXK61RbzHPzNzumXcJDlvydecqSiHml2HHtfSOqNczCSQ7Mc_v-OXe6lAJwYxrIw57k3bOXpzq-6Tz7eQk3"
    "uULfcSO4HNRWSi0Glfe_9O3DBOZXMT5ztFOhTVh-i02AyfP2kT6LxREYsyzsSooHgVSggumHO_QBrKubWgFgB-sidD-r8C7t_KKeMiNk5_BSvgZj"
    "kH_C2LwTSjOy5sOJBjnGks5Ygbu0y--dA4dm-0w1JWX2tCv65xsy3pw4QDV2S0ZXcQ7GPHR8ojazbDif3jplp87ff54iS47Z0KcRHHbNPrTEMPLz"
    "fPRqicnmmqqzlWgGQ0yR_hNRpqIUSOD0hznhssiWLiCYu30Gh1oIFft5d2VZSnRD48clWwVWNSZmfok_xbtAJyrh4mYG"
)


def test_giai_dung_vector_csharp():
    q = giai_ma(doc_khoa(KHOA_VECTOR), TOKEN_VECTOR)

    assert q.grant_id == uuid.UUID("11111111-1111-1111-1111-111111111111")
    assert q.sample_id == uuid.UUID("22222222-2222-2222-2222-222222222222")
    assert q.project_id == uuid.UUID("33333333-3333-3333-3333-333333333333")
    assert q.storage_key == "projects/abc/x.png"
    assert q.modality == "image"
    assert q.user_id == uuid.UUID("44444444-4444-4444-4444-444444444444")
    assert q.guest_session_id is None
    assert q.role == "labeler" and q.mode == "watermarked"
    assert q.issued_at == datetime.fromtimestamp(1760000000, timezone.utc)
    assert q.het_han(datetime.fromtimestamp(1760000300, timezone.utc))
    assert not q.het_han(datetime.fromtimestamp(1760000299, timezone.utc))


def test_sua_mot_ky_tu_hoac_khac_khoa_thi_tu_choi():
    sua = TOKEN_VECTOR[:40] + ("B" if TOKEN_VECTOR[40] == "A" else "A") + TOKEN_VECTOR[41:]
    with pytest.raises(TokenKhongHopLe):
        giai_ma(doc_khoa(KHOA_VECTOR), sua)
    with pytest.raises(TokenKhongHopLe):
        giai_ma(bytes(32), TOKEN_VECTOR)
    with pytest.raises(TokenKhongHopLe):
        giai_ma(doc_khoa(KHOA_VECTOR), "khong-phai-token")


@pytest.mark.parametrize("khoa", ["", "AAECAwQ=", "%%%"])
def test_khoa_sai_bi_tu_choi(khoa):
    with pytest.raises(ValueError):
        doc_khoa(khoa)


def _ma_hoa(khoa: bytes, noi_dung: dict) -> str:
    import base64
    import json
    import os

    from cryptography.hazmat.primitives.ciphers.aead import AESGCM

    from app.grant import AAD

    nonce = os.urandom(12)
    du = nonce + AESGCM(khoa).encrypt(nonce, json.dumps(noi_dung).encode(), AAD)
    return base64.urlsafe_b64encode(du).decode().rstrip("=")


def _noi_dung_goc() -> dict:
    return {"v": 1, "g": str(uuid.uuid4()), "s": str(uuid.uuid4()), "p": str(uuid.uuid4()), "k": "p/a.mp4", "mo": "video",
            "u": str(uuid.uuid4()), "gs": None, "r": "labeler", "m": "watermarked", "iat": 1760000000, "exp": 1760000300}


def test_truong_watermark_noi_va_doan_video():
    khoa = doc_khoa(KHOA_VECTOR)
    nd = _noi_dung_goc() | {"wt": "nguồn: u - Công ty A - gannhan.vn", "ws": "nguồn: Công ty A - gannhan.vn", "a": 120.0, "b": 240.5}
    q = giai_ma(khoa, _ma_hoa(khoa, nd))
    assert q.chu_ca_nhan == "nguồn: u - Công ty A - gannhan.vn"
    assert q.chu_chung == "nguồn: Công ty A - gannhan.vn"
    assert (q.bat_dau, q.ket_thuc) == (120.0, 240.5)


def test_token_kieu_cu_khong_co_truong_moi():
    khoa = doc_khoa(KHOA_VECTOR)
    q = giai_ma(khoa, _ma_hoa(khoa, _noi_dung_goc()))
    assert q.chu_ca_nhan is None and q.chu_chung is None and q.bat_dau is None and q.ket_thuc is None
