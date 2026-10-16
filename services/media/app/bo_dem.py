"""Bo dem dia cho ban am thanh / video da watermark.

Nhung watermark vao video la ma hoa lai CA file — khong lam lai moi lan trinh duyet xin mot doan (Range)
khi tua. Ban da nhung luu tren dia cuc bo theo khoa (file goc, nguoi xem, do manh, khoa watermark):
  - cung nguoi xem mo lai trong thoi han → phat ngay, tua duoc;
  - nhieu request cung luc cho CUNG mot ban → chi mot request tao, cac request khac doi (khoa theo ban);
  - qua han (ttl) hoac tong dung luong vuot tran → xoa ban cu nhat. Mat bo dem chi ton thoi gian tao lai.
Khong co DB: mot instance mot bo dem; nhieu instance thi moi instance tu tao (ket qua y het nhau).
"""

import asyncio
import hashlib
import logging
import os
import time
import uuid
from pathlib import Path
from typing import Callable

log = logging.getLogger(__name__)


class BoDem:
    def __init__(self, thu_muc: str, ttl_giay: float, tran_byte: int) -> None:
        self.thu_muc = Path(thu_muc)
        self.ttl = ttl_giay
        self.tran = tran_byte
        self._khoa: dict[str, asyncio.Lock] = {}

    @staticmethod
    def khoa(*phan: str) -> str:
        return hashlib.sha256("|".join(phan).encode()).hexdigest()[:40]

    def _con_han(self, p: Path) -> bool:
        try:
            return time.time() - p.stat().st_mtime < self.ttl
        except FileNotFoundError:
            return False

    async def lay(self, khoa: str, duoi: str, tao: Callable[[Path], None]) -> Path:
        """Duong dan ban da tao; chua co / het han thi goi tao(duong_dan_tam) trong thread roi doi ten."""
        self.thu_muc.mkdir(parents=True, exist_ok=True)
        dich = self.thu_muc / f"{khoa}{duoi}"
        if self._con_han(dich):
            return dich
        lk = self._khoa.setdefault(khoa, asyncio.Lock())
        async with lk:
            if self._con_han(dich):
                return dich
            tam = self.thu_muc / f".{khoa}.{uuid.uuid4().hex}{duoi}"
            try:
                await asyncio.to_thread(tao, tam)
                os.replace(tam, dich)
            finally:
                if tam.exists():
                    tam.unlink()
        self._khoa.pop(khoa, None)
        await asyncio.to_thread(self.don)
        return dich

    def don(self) -> int:
        """Xoa ban het han, roi ban cu nhat cho toi khi duoi tran. Tra so file da xoa."""
        if not self.thu_muc.exists():
            return 0
        bay_gio = time.time()
        con: list[tuple[float, int, Path]] = []
        xoa = 0
        for p in self.thu_muc.iterdir():
            if not p.is_file() or p.name.startswith("."):
                continue
            try:
                st = p.stat()
            except FileNotFoundError:
                continue
            if bay_gio - st.st_mtime >= self.ttl:
                p.unlink(missing_ok=True)
                xoa += 1
            else:
                con.append((st.st_mtime, st.st_size, p))
        tong = sum(c[1] for c in con)
        for _mt, kich, p in sorted(con):
            if tong <= self.tran:
                break
            p.unlink(missing_ok=True)
            tong -= kich
            xoa += 1
        if xoa:
            log.info("Bo dem watermark: xoa %s file", xoa)
        return xoa
