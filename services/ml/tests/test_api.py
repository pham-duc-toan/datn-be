import math
import os

import httpx
import pytest
from fastapi import FastAPI

from app.api import router
from app.config import Settings, vi_pham_bi_mat_dev
from app.db import KhoBoNho, _chuoi_vector, _doc_vector
from app.embedder import chuan_hoa, chuan_hoa_l2, khoa_bam

KHOA = {"X-Internal-Key": "demo_internal_key"}


class MoHinhGia:
    """Vector tat dinh theo do dai cau + dem so lan goi (kiem bo dem)."""

    ten = "gia"

    def __init__(self):
        self.da_nhung: list[str] = []

    def san_sang(self):
        return True

    def nhung(self, cau):
        self.da_nhung.extend(cau)
        return [[float(len(c)), 1.0] for c in cau]


@pytest.fixture
def app_test():
    app = FastAPI()
    app.include_router(router)
    app.state.mo_hinh = MoHinhGia()
    app.state.kho = KhoBoNho()
    return app


async def goi(app, duong, json=None, headers=KHOA):
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://ml") as c:
        if json is None:
            return await c.get(duong, headers=headers)
        return await c.post(duong, json=json, headers=headers)


async def test_sai_khoa_404(app_test):
    assert (await goi(app_test, "/internal/embeddings", {"texts": ["a"]}, headers={})).status_code == 404
    assert (await goi(app_test, "/internal/embeddings", {"texts": ["a"]}, headers={"X-Internal-Key": "sai"})).status_code == 404
    assert (await goi(app_test, "/internal/model", headers={})).status_code == 404


async def test_nhung_dung_thu_tu_va_dung_bo_dem(app_test):
    r = await goi(app_test, "/internal/embeddings", {"texts": ["con meo", "  con   meo ", "cho"]})
    assert r.status_code == 200
    j = r.json()
    assert j["model"] == "gia" and j["dim"] == 2
    assert j["vectors"][0] == j["vectors"][1]          # cung noi dung sau chuan hoa
    assert j["vectors"][2] == [3.0, 1.0]
    assert app_test.state.mo_hinh.da_nhung == ["con meo", "cho"]   # moi noi dung nhung mot lan
    assert j["cached"] == 1

    r = await goi(app_test, "/internal/embeddings", {"texts": ["cho", "con meo"]})
    assert r.json()["cached"] == 2
    assert app_test.state.mo_hinh.da_nhung == ["con meo", "cho"]   # lan hai lay het tu bo dem


async def test_kiem_dau_vao(app_test):
    assert (await goi(app_test, "/internal/embeddings", {"texts": []})).json()["code"] == "thieu_van_ban"
    assert (await goi(app_test, "/internal/embeddings", {"texts": ["a", "  "]})).json()["code"] == "van_ban_rong"
    assert (await goi(app_test, "/internal/embeddings", {"texts": ["a"] * 257})).json()["code"] == "qua_nhieu_van_ban"
    assert (await goi(app_test, "/internal/embeddings", {"texts": ["a" * 5001]})).json()["code"] == "van_ban_qua_dai"


async def test_thong_tin_mo_hinh(app_test):
    assert (await goi(app_test, "/internal/model")).json() == {"model": "gia", "ready": True}


def test_khoa_bam_theo_mo_hinh_va_noi_dung():
    assert chuan_hoa("  a \n b\t") == "a b"
    assert khoa_bam("m", "a  b") == khoa_bam("m", "a b")
    assert khoa_bam("m1", "a") != khoa_bam("m2", "a")


def test_chuan_hoa_l2_va_vector_pg():
    import numpy as np
    v = chuan_hoa_l2(np.array([[3.0, 4.0], [0.0, 0.0]]))
    assert math.isclose(float(np.linalg.norm(v[0])), 1.0) and v[1].tolist() == [0.0, 0.0]
    assert _doc_vector(_chuoi_vector([0.5, -0.25])) == [0.5, -0.25]


def test_s08_khoa_dev():
    assert "internal_api_key" in vi_pham_bi_mat_dev(Settings(environment="Production"))


@pytest.mark.skipif(not os.environ.get("ML_TEST_MO_HINH_THAT"), reason="dat ML_TEST_MO_HINH_THAT=1 de nap mo hinh that (~0,2 GB)")
def test_mo_hinh_that_tach_cau_cung_nghia_voi_cau_lac_de():
    import numpy as np
    from app.embedder import MoHinhFastEmbed
    mh = MoHinhFastEmbed(Settings().model_name, os.environ.get("ML_MODEL_CACHE_DIR"))
    v = np.array(mh.nhung(["Con mèo đang ngủ trên ghế sofa", "Chú mèo say giấc trên ghế", "Giá vàng hôm nay tăng mạnh"]))
    assert float(v[0] @ v[1]) > 0.6 > float(v[0] @ v[2])
