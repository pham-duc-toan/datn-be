import json

import pytest

from app import settings_store as st
from app.contracts import SettingChanged, SettingsSnapshot
from app.messaging import envelope as env_mod
from app.reputation import tham_so_hien_tai


@pytest.fixture(autouse=True)
def bo_nho_sach():
    st.xoa_het_cho_test()
    yield
    st.xoa_het_cho_test()


def test_chua_co_gia_tri_thi_dung_mac_dinh_cua_catalog():
    # Mac dinh trong shared/settings/catalog.json (xuat tu SettingCatalog C#).
    assert st.giay(st.DS_INTERVAL) == 900
    assert st.so_nguyen(st.DS_MIN_LABELS) == 5
    ts = tham_so_hien_tai()
    assert (ts.trong_so_gold, ts.tien_nghiem, ts.suc_nang_tien_nghiem) == (0.6, 0.7, 2.0)


def test_chi_ghi_de_khi_version_moi_hon():
    assert st.ap_dung(st.DS_MIN_LABELS, "8", 2)
    assert not st.ap_dung(st.DS_MIN_LABELS, "3", 1)   # event cu den tre
    assert not st.ap_dung(st.DS_MIN_LABELS, "3", 2)   # giao lai cung version
    assert st.so_nguyen(st.DS_MIN_LABELS) == 8
    assert st.phien_ban(st.DS_MIN_LABELS) == 2


def test_gia_tri_sai_kieu_hoac_ngoai_gioi_han_bi_bo_qua():
    assert not st.ap_dung(st.REPUTATION_GOLD_WEIGHT, "1.5", 1)     # > max 1
    assert not st.ap_dung(st.DS_MIN_LABELS, "2.5", 1)              # khong nguyen
    assert not st.ap_dung(st.DS_MIN_LABELS, "true", 1)             # bool khong phai so
    assert not st.ap_dung("khoa.khong.ton.tai", "1", 1)
    assert st.so_thuc(st.REPUTATION_GOLD_WEIGHT) == 0.6


def test_doc_event_setting_cua_csharp():
    # Hinh dang JSON y het admin-svc phat (camelCase, value la JSON vo huong).
    tho = {
        "eventId": "0192f000-0000-7000-8000-000000000001",
        "eventType": "setting.changed",
        "version": 1,
        "occurredAt": "2026-10-08T10:00:00+00:00",
        "producer": "admin-svc",
        "correlationId": "0192f000-0000-7000-8000-000000000002",
        "causationId": None,
        "actor": {"userId": "0192f000-0000-7000-8000-000000000003", "role": "admin"},
        "payload": {
            "key": "quality.reputation_gold_weight",
            "value": 0.8,
            "settingVersion": 3,
            "changedBy": "0192f000-0000-7000-8000-000000000003",
            "changedAt": "2026-10-08T10:00:00+00:00",
        },
    }
    env = env_mod.doc(json.dumps(tho).encode(), SettingChanged)
    assert env.payload.value == 0.8
    assert env.payload.setting_version == 3

    snap = dict(tho, eventType="settings.snapshot", payload={"items": [{"key": "quality.ds_interval", "value": 60, "settingVersion": 1}]})
    env2 = env_mod.doc(json.dumps(snap).encode(), SettingsSnapshot)
    assert env2.payload.items[0].value == 60


# ---- cho_theo_setting: doc lai chu ky moi nhip (admin rut chu ky co hieu luc ngay) ----

def _chu_ky_gia(monkeypatch, giay_ban_dau):
    hop = {"giay": giay_ban_dau}
    monkeypatch.setattr(st, "giay", lambda key: hop["giay"])
    return hop


def test_cho_du_chu_ky_khi_setting_khong_doi(monkeypatch):
    import asyncio, time
    _chu_ky_gia(monkeypatch, 0.3)
    t0 = time.monotonic()
    bi_dung = asyncio.run(st.cho_theo_setting(st.DS_INTERVAL, asyncio.Event(), nhip=0.05))
    assert not bi_dung
    assert time.monotonic() - t0 >= 0.29


def test_rut_ngan_chu_ky_giua_chung_thi_ket_thuc_som(monkeypatch):
    import asyncio, time
    hop = _chu_ky_gia(monkeypatch, 30.0)

    async def chay():
        cho = asyncio.create_task(st.cho_theo_setting(st.DS_INTERVAL, asyncio.Event(), nhip=0.05))
        await asyncio.sleep(0.2)
        hop["giay"] = 0.1
        return await cho

    t0 = time.monotonic()
    assert not asyncio.run(chay())
    assert time.monotonic() - t0 < 5


def test_dung_service_thi_thoat_ngay(monkeypatch):
    import asyncio, time
    _chu_ky_gia(monkeypatch, 30.0)

    async def chay():
        dung = asyncio.Event()
        cho = asyncio.create_task(st.cho_theo_setting(st.DS_INTERVAL, dung, nhip=1.0))
        await asyncio.sleep(0.1)
        dung.set()
        return await cho

    t0 = time.monotonic()
    assert asyncio.run(chay())
    assert time.monotonic() - t0 < 1
