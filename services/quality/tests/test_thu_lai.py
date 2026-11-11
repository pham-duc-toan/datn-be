"""Loai 3 (kien truc 3.29): dem lan giao, nhan ra lan cuoi, gian cach thu lai — khop ban C# (ThuLaiEvent)."""
from app.messaging import thu_lai


def test_doc_x_delivery_count():
    assert thu_lai.so_lan_da_that_bai(None) == 0
    assert thu_lai.so_lan_da_that_bai({}) == 0
    assert thu_lai.so_lan_da_that_bai({"x-delivery-count": 3}) == 3
    assert thu_lai.so_lan_da_that_bai({"x-delivery-count": b"4"}) == 4
    assert thu_lai.so_lan_da_that_bai({"x-delivery-count": "x"}) == 0


def test_lan_cuoi():
    assert not thu_lai.la_lan_cuoi(4, 5)
    assert thu_lai.la_lan_cuoi(5, 5)


def test_gian_cach():
    assert [thu_lai.cho_truoc_khi_thu_lai(n, 30) for n in range(5)] == [1, 3, 9, 27, 30]
    assert thu_lai.cho_truoc_khi_thu_lai(2, 0) == 0
