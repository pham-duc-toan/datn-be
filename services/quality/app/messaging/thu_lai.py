"""Ket qua xu ly mot event — ban Python cua Consumers/KetQuaXuLyEvent.cs (kien truc 3.29).

  - BoQuaEvent (loai 1): ca binh thuong (doi tuong da doi trang thai / da xoa) → ack, log info, khong thu lai.
  - Loi khac (loai 3, ky thuat): cho gian cach 1s, 3s, 9s... roi tra lai hang doi; lan cuoi (broker se chuyen vao
    DLQ) log ERROR de dev tim tren Kibana.
"""

import asyncio
from collections.abc import Mapping
from typing import Any


class BoQuaEvent(Exception):
    """Loai 1: handler nem khi event khong con gi de lam — consumer ack, khong thu lai, khong vao DLQ."""


def so_lan_da_that_bai(headers: Mapping[str, Any] | None) -> int:
    """Header x-delivery-count cua quorum queue (khong co = lan giao dau)."""
    if not headers:
        return 0
    v = headers.get("x-delivery-count")
    if v is None:
        return 0
    try:
        return int(v.decode() if isinstance(v, bytes) else v)
    except (TypeError, ValueError):
        return 0


def la_lan_cuoi(da_that_bai: int, gioi_han: int) -> bool:
    """Quorum queue dead-letter khi so lan tra lai VUOT x-delivery-limit → lan giao thu (limit + 1) la lan cuoi."""
    return da_that_bai >= gioi_han


def cho_truoc_khi_thu_lai(da_that_bai: int, toi_da: float) -> float:
    """1, 3, 9, 27... giay, khong qua toi_da."""
    return min(float(3 ** min(da_that_bai, 20)), toi_da)


async def ngu(giay: float) -> None:
    if giay > 0:
        await asyncio.sleep(giay)
