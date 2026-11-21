"""Ban xem truoc dung chung (kien truc 3.40): ham thuan tao anh, kich thuoc video, doan clip, khoa MinIO."""

import uuid

import cv2
import numpy as np

from app import xem_truoc


def _anh(rong: int, cao: int) -> bytes:
    anh = np.full((cao, rong, 3), 200, np.uint8)
    return cv2.imencode(".png", anh)[1].tobytes()


def test_anh_thu_nho_canh_dai_1080_va_co_chu():
    ra = cv2.imdecode(np.frombuffer(xem_truoc.anh_xem_truoc(_anh(4000, 3000), "nguồn: Công ty A - gannhan.vn"), np.uint8),
                      cv2.IMREAD_COLOR)
    assert ra.shape[1] == 1080 and ra.shape[0] == 810
    # Chu ve o goc duoi phai — vung do khac nen xam phang.
    assert np.abs(ra[-60:, -300:].astype(int) - 200).max() > 20


def test_anh_nho_giu_nguyen_kich_thuoc_khong_chu_khi_tat_watermark():
    ra = cv2.imdecode(np.frombuffer(xem_truoc.anh_xem_truoc(_anh(640, 480), None), np.uint8), cv2.IMREAD_COLOR)
    assert ra.shape[:2] == (480, 640)
    assert np.abs(ra.astype(int) - 200).max() <= 3


def test_kich_thuoc_video_toi_da_720p_canh_chan():
    assert xem_truoc.kich_thuoc_video(1920, 1080) == (1280, 720)
    assert xem_truoc.kich_thuoc_video(1081, 1921) == (406, 720)
    assert xem_truoc.kich_thuoc_video(641, 359) == (642, 360)


def test_doan_clip_15_giay_dau_doan_mau():
    assert xem_truoc.doan_clip(120.0, 240.0) == (120.0, 15.0)
    assert xem_truoc.doan_clip(10.0, 18.5) == (10.0, 8.5)
    assert xem_truoc.doan_clip(None, None) == (0.0, 15.0)


def test_khoa_tat_dinh_theo_du_an_va_mau():
    p, s = uuid.uuid4(), uuid.uuid4()
    assert xem_truoc.khoa(p, s, "image") == f"{p}/_preview/v1/{s}/anh.jpg"
    assert xem_truoc.khoa(p, s, "video").endswith("/video.mp4")
