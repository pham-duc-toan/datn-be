import asyncio
import os
import time
from pathlib import Path

from app.bo_dem import BoDem


def test_tao_mot_lan_khi_nhieu_request_cung_luc(tmp_path: Path):
    bd = BoDem(str(tmp_path), 3600, 10**9)
    so_lan = []

    def tao(p: Path) -> None:
        time.sleep(0.05)
        so_lan.append(1)
        p.write_bytes(b"x" * 10)

    async def chay():
        return await asyncio.gather(*[bd.lay("k", ".mp4", tao) for _ in range(5)])

    ra = asyncio.run(chay())
    assert len(so_lan) == 1
    assert len({str(p) for p in ra}) == 1 and ra[0].read_bytes() == b"x" * 10
    assert not [p for p in tmp_path.iterdir() if p.name.startswith(".")]   # khong sot file tam


def test_het_han_va_vuot_tran_bi_xoa(tmp_path: Path):
    bd = BoDem(str(tmp_path), 100, 25)
    cu = tmp_path / "cu.mp4"
    cu.write_bytes(b"1" * 10)
    os.utime(cu, (time.time() - 1000, time.time() - 1000))
    for i, ten in enumerate(("a", "b", "c")):
        p = tmp_path / f"{ten}.mp4"
        p.write_bytes(b"1" * 10)
        os.utime(p, (time.time() - 50 + i, time.time() - 50 + i))
    assert bd.don() == 2   # cu.mp4 het han; a.mp4 cu nhat bi xoa de con 20 <= 25 byte
    assert sorted(p.name for p in tmp_path.iterdir()) == ["b.mp4", "c.mp4"]


def test_loi_khi_tao_khong_de_lai_file(tmp_path: Path):
    bd = BoDem(str(tmp_path), 3600, 10**9)

    def hong(p: Path) -> None:
        p.write_bytes(b"do dang")
        raise RuntimeError("ffmpeg hong")

    try:
        asyncio.run(bd.lay("k", ".flac", hong))
    except RuntimeError:
        pass
    assert list(tmp_path.iterdir()) == []
