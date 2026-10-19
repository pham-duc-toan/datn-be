"""Nhung cau van ban thanh vector (chuan hoa L2 — tich vo huong = cosine).

Mo hinh nap MOT lan (lan dau mat ~20 giay), dung chung cho moi yeu cau; ONNX chay CPU nen moi lan nhung
chay trong thread rieng de khong chan vong su kien. Khoa luong: onnxruntime an toan da luong nhung tranh
nhieu lo lon chiem het CPU cung luc.
"""

import hashlib
import logging
import re
import threading
from typing import Protocol

import numpy as np

log = logging.getLogger(__name__)


def chuan_hoa(van_ban: str) -> str:
    """Bo khoang trang thua — "  Con meo\\n ngu " va "Con meo ngu" la mot cau."""
    return re.sub(r"\s+", " ", van_ban).strip()


def khoa_bam(model: str, van_ban: str) -> bytes:
    return hashlib.sha256((model + "\x00" + chuan_hoa(van_ban)).encode("utf-8")).digest()


def chuan_hoa_l2(m: np.ndarray) -> np.ndarray:
    do_dai = np.linalg.norm(m, axis=1, keepdims=True)
    do_dai[do_dai == 0] = 1.0
    return m / do_dai


class MoHinhNhung(Protocol):
    ten: str

    def san_sang(self) -> bool: ...

    def nhung(self, cau: list[str]) -> list[list[float]]: ...


class MoHinhFastEmbed:
    def __init__(self, ten: str, thu_muc: str | None) -> None:
        self.ten = ten
        self._thu_muc = thu_muc
        self._mo_hinh = None
        self._khoa = threading.Lock()

    def nap(self) -> None:
        with self._khoa:
            if self._mo_hinh is None:
                from fastembed import TextEmbedding
                log.info("Nap mo hinh %s ...", self.ten)
                self._mo_hinh = TextEmbedding(self.ten, cache_dir=self._thu_muc)
                log.info("Mo hinh %s san sang", self.ten)

    def san_sang(self) -> bool:
        return self._mo_hinh is not None

    def nhung(self, cau: list[str]) -> list[list[float]]:
        self.nap()
        with self._khoa:
            m = np.array(list(self._mo_hinh.embed([chuan_hoa(c) for c in cau])), dtype=np.float32)
        return chuan_hoa_l2(m).tolist()
