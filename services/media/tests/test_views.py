"""Ham thuan cua views: doc header Range (tua am thanh), quyen tai danh sach phat / mang HLS theo han token."""

import base64
import json
import os
import time
import uuid

import pytest
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

from app.config import settings
from app.errors import LoiApi
from app.grant import AAD, doc_khoa
from app.views import _doc_range, _quyen_hls, chu_cua


def test_doc_range():
    assert _doc_range(None, 100) is None
    assert _doc_range("bytes=0-9", 100) == (0, 9)
    assert _doc_range("bytes=90-", 100) == (90, 99)
    assert _doc_range("bytes=-10", 100) == (90, 99)
    assert _doc_range("bytes=50-500", 100) == (50, 99)
    assert _doc_range("bytes=200-300", 100) is None
    assert _doc_range("bytes=0-1,5-6", 100) is None
    assert _doc_range("items=0-1", 100) is None


def _token(**them) -> str:
    bay_gio = int(time.time())
    nd = {"v": 1, "g": str(uuid.uuid4()), "s": str(uuid.uuid4()), "p": str(uuid.uuid4()), "k": "p/a.mp4", "mo": "video",
          "u": str(uuid.uuid4()), "gs": None, "r": "labeler", "m": "watermarked", "iat": bay_gio, "exp": bay_gio + 300,
          "a": 0.0, "b": 30.0}
    nd.update(them)
    nonce = os.urandom(12)
    du = nonce + AESGCM(doc_khoa(settings().grant_key)).encrypt(nonce, json.dumps(nd).encode(), AAD)
    return base64.urlsafe_b64encode(du).decode().rstrip("=")


def test_danh_sach_phat_can_token_con_han_mang_duoc_gia_han():
    bay_gio = int(time.time())
    con_han = _token()
    vua_het = _token(exp=bay_gio - 60)
    het_qua_gia_han = _token(exp=bay_gio - settings().hls_grace_seconds - 60)

    assert _quyen_hls(con_han, cho_gia_han=False).ket_thuc == 30.0
    with pytest.raises(LoiApi) as e:
        _quyen_hls(vua_het, cho_gia_han=False)
    assert e.value.status == 410
    # Mang trong danh sach phat: tai tiep duoc trong thoi gian gia han (labeler tam dung roi xem tiep).
    assert _quyen_hls(vua_het, cho_gia_han=True) is not None
    with pytest.raises(LoiApi):
        _quyen_hls(het_qua_gia_han, cho_gia_han=True)


def test_hls_chi_cho_video_co_watermark_va_co_doan():
    for sai in (_token(mo="image"), _token(m="original")):
        with pytest.raises(LoiApi) as e:
            _quyen_hls(sai, cho_gia_han=False)
        assert e.value.status == 404
    with pytest.raises(LoiApi) as e:
        _quyen_hls(_token(a=None, b=None), cho_gia_han=False)
    assert e.value.status == 409


def test_chu_cua_nguoi_xem():
    from app.grant import giai_ma
    q = giai_ma(doc_khoa(settings().grant_key), _token(wt="nguồn: x - Công ty A - gannhan.vn"))
    assert chu_cua(q) == "nguồn: x - Công ty A - gannhan.vn"
    q = giai_ma(doc_khoa(settings().grant_key), _token())
    assert chu_cua(q) == f"nguồn: {q.user_id}"
