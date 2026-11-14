"""HLS theo doan mau: moc chia mang, khoa MinIO, danh sach phat (mang rieng dau / cuoi + mang giua dung chung),
cat that bang ffmpeg va ma hoa lai mang rieng bang PyAV (giu moc thoi gian, co chu cua nguoi xem)."""

import shutil
import subprocess
from pathlib import Path

import av
import numpy as np
import pytest

from app import hls

co_ffmpeg = pytest.mark.skipif(shutil.which("ffmpeg") is None, reason="can ffmpeg trong PATH")


def test_moc_cat():
    assert hls.moc_cat(3.0) == []                    # ngan: mot mang
    assert hls.moc_cat(4.99) == []
    assert hls.moc_cat(5.0) == [2.0, 3.0]            # dau 2s | giua 1s | cuoi 2s
    assert hls.moc_cat(14.0) == [2.0, 8.0, 12.0]
    # Mau 2 phut: 1 mang dau + 19 mang giua 6s + 1 mang giua 2s (116-118) + 1 mang cuoi.
    assert hls.moc_cat(120.0) == [2.0, 8.0, 14.0, 20.0, 26.0, 32.0, 38.0, 44.0, 50.0, 56.0, 62.0, 68.0, 74.0, 80.0,
                                  86.0, 92.0, 98.0, 104.0, 110.0, 116.0, 118.0]
    # Mang giua cuoi khong ngan hon 1s: 15s → [2, 8, 13] (13 - 8 = 5s, khong tach them mang 1s le).
    assert hls.moc_cat(15.0) == [2.0, 8.0, 13.0]


def test_khoa_cung_tien_to_du_an():
    assert hls.thu_muc("p1/raw/a.mp4", 120, 240.5) == "p1/_wm/hls-v1/raw/a.mp4/120000-240500"
    assert hls.khoa_chi_muc("p1/a.mp4", 0, 10) == "p1/_wm/hls-v1/a.mp4/0-10000/index.json"
    assert hls.khoa_mang("p1/a.mp4", 0, 10, "c00002.ts") == "p1/_wm/hls-v1/a.mp4/0-10000/c00002.ts"


def _cm(*dai: float) -> hls.ChiMuc:
    return hls.ChiMuc([hls.Mang(hls.ten_mang(i), d) for i, d in enumerate(dai)])


def test_danh_sach_phat_mang_rieng_dau_cuoi_giua_dung_chung():
    ds = hls.danh_sach_phat(_cm(2.0, 6.0, 6.0, 2.0))
    dong = ds.strip().split("\n")
    uri = [d for d in dong if not d.startswith("#")]
    assert uri == ["p/dau.ts", "c/00001.ts", "c/00002.ts", "p/cuoi.ts"]
    assert dong[0] == "#EXTM3U" and dong[-1] == "#EXT-X-ENDLIST"
    assert "#EXT-X-PLAYLIST-TYPE:VOD" in dong and "#EXT-X-TARGETDURATION:6" in dong
    # Chuyen giua mang rieng va mang dung chung: bao trinh phat khoi tao lai bo giai ma.
    assert ds.count("#EXT-X-DISCONTINUITY") == 2


def test_danh_sach_phat_mot_mang():
    ds = hls.danh_sach_phat(_cm(3.5))
    assert [d for d in ds.split("\n") if d and not d.startswith("#")] == ["p/ca.ts"]
    assert "#EXT-X-DISCONTINUITY" not in ds


def test_chi_so_mang_rieng():
    cm = _cm(2.0, 6.0, 2.0)
    assert hls.chi_so_mang_rieng(cm, "dau") == 0
    assert hls.chi_so_mang_rieng(cm, "cuoi") == 2
    assert hls.chi_so_mang_rieng(cm, "ca") is None
    assert hls.chi_so_mang_rieng(_cm(3.0), "ca") == 0
    assert hls.chi_so_mang_rieng(_cm(3.0), "dau") is None


def test_chi_muc_ghi_doc_khu_hoi():
    cm = _cm(2.0, 6.0, 2.0)
    assert hls.doc_chi_muc(hls._ghi_chi_muc(cm)) == cm


def _video_mau(duong: Path, giay: int, kich: str = "640x360") -> None:
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", f"testsrc2=size={kich}:rate=25:duration={giay}",
                    "-f", "lavfi", "-i", f"sine=frequency=300:duration={giay}", "-shortest", "-c:v", "libx264",
                    "-pix_fmt", "yuv420p", "-c:a", "aac", str(duong)], check=True)


def _khung_cuoi_goc_phai(duong: Path, so: int = 5) -> np.ndarray:
    """Khung thu `so` cua mang, chi vung 15% duoi cung."""
    with av.open(str(duong)) as f:
        for i, k in enumerate(f.decode(video=0)):
            if i == so:
                anh = k.to_ndarray(format="bgr24")
                return anh[int(anh.shape[0] * 0.85):]
    raise AssertionError("mang it khung hon mong doi")


@co_ffmpeg
def test_cat_doan_mau_va_tao_mang_rieng(tmp_path: Path):
    goc = tmp_path / "goc.mp4"
    _video_mau(goc, 20)
    ra = tmp_path / "ra"
    ra.mkdir()

    # Doan mau [3, 17] (14 giay) cua file 20 giay.
    cm = hls._cat_mot_doan(goc, 3.0, 17.0, "nguồn: Công ty A - gannhan.vn", ra)
    assert [m.ten for m in cm.mang] == ["c00000.ts", "c00001.ts", "c00002.ts", "c00003.ts"]
    assert abs(sum(m.dai for m in cm.mang) - 14.0) < 0.1
    assert abs(cm.mang[0].dai - 2.0) < 0.05 and abs(cm.mang[-1].dai - 2.0) < 0.1

    # Mang rieng = mang dau sach + chu cua nguoi xem: vung goc duoi cua cung mot khung phai khac nhau.
    sach = _khung_cuoi_goc_phai(ra / "c00000.ts")
    rieng = tmp_path / "p_dau.ts"
    hls.tao_mang_rieng(ra / "c00000.ts", rieng, "nguồn: 0192f3a4-7c1e-7a2b-9d10-5e6f7a8b9c0d - Công ty A - gannhan.vn")
    co_chu = _khung_cuoi_goc_phai(rieng)
    assert np.abs(co_chu.astype(int) - sach.astype(int)).sum() > 0

    # Mang rieng giu moc thoi gian + so khung + am thanh cua mang sach (ghep vao danh sach phat khong lech).
    with av.open(str(ra / "c00000.ts")) as a, av.open(str(rieng)) as b:
        assert b.streams.video[0].codec_context.width == a.streams.video[0].codec_context.width
        assert len(b.streams.audio) == len(a.streams.audio) == 1
        so_a = sum(1 for _ in a.decode(video=0))
        so_b = sum(1 for _ in b.decode(video=0))
        assert so_a == so_b == 50
    with av.open(str(ra / "c00000.ts")) as a, av.open(str(rieng)) as b:
        assert abs(float(a.streams.video[0].start_time * a.streams.video[0].time_base)
                   - float(b.streams.video[0].start_time * b.streams.video[0].time_base)) < 0.05


@co_ffmpeg
def test_doan_ngan_mot_mang_khong_chu_dung_chung(tmp_path: Path):
    goc = tmp_path / "goc.mp4"
    _video_mau(goc, 4)
    ra = tmp_path / "ra"
    ra.mkdir()
    cm = hls._cat_mot_doan(goc, 0.0, 3.5, "nguồn: Công ty A - gannhan.vn", ra)
    assert cm.mot_mang and abs(cm.mang[0].dai - 3.5) < 0.1
    assert not (ra / "lop.png").exists()
