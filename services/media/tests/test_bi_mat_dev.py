"""S-08: ngoai Development, cau hinh mang mat khau / khoa dev thi khong khoi dong."""

import pytest

from app import config


def test_phat_hien_gia_tri_dev():
    assert config.vi_pham_bi_mat_dev(config.Settings())  # mac dinh dev → co vi pham


def test_chan_khi_khong_phai_development(monkeypatch):
    tien_to = config.Settings.model_config["env_prefix"]
    monkeypatch.setenv(tien_to + "ENVIRONMENT", "Production")
    config.settings.cache_clear()
    try:
        with pytest.raises(RuntimeError, match="S-08"):
            config.settings()
    finally:
        monkeypatch.delenv(tien_to + "ENVIRONMENT")
        config.settings.cache_clear()
