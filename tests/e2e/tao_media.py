"""Sinh file media cho e2e_modality bang ffmpeg (khong commit file nhi phan vao repo).

  anh.png            200x150 (do)                  — mau anh hop le
  anh2.png           300x200 (xanh)                — e2e_modality tu sinh lai
  clip.mp4           320x240, 12 giay, h264 + aac  — video hop le
  cuoc-goi.wav       23 giay, 16 kHz mono          — cat thanh 3 doan 10 giay
  am-thanh-gia.mp4   ban sao WAV doi duoi .mp4     — ffprobe phai loai (khong co hinh)
  gia-mao.png        "MZ fake exe"                 — khong phai anh, phai bi bo qua
"""

import os
import shutil
import subprocess


def _ffmpeg(*tham_so: str) -> None:
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", *tham_so], check=True)


def dam_bao(thu_muc: str) -> None:
    os.makedirs(thu_muc, exist_ok=True)

    def p(ten: str) -> str:
        return os.path.join(thu_muc, ten)

    if not os.path.exists(p("anh.png")):
        _ffmpeg("-f", "lavfi", "-i", "color=c=red:size=200x150:duration=1", "-frames:v", "1", p("anh.png"))
    if not os.path.exists(p("clip.mp4")):
        _ffmpeg("-f", "lavfi", "-i", "testsrc=size=320x240:rate=10:duration=12",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=12",
                "-ac", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", p("clip.mp4"))
    if not os.path.exists(p("cuoc-goi.wav")):
        _ffmpeg("-f", "lavfi", "-i", "sine=frequency=300:sample_rate=16000:duration=23", "-ac", "1", "-c:a", "pcm_s16le", p("cuoc-goi.wav"))
    if not os.path.exists(p("am-thanh-gia.mp4")):
        shutil.copyfile(p("cuoc-goi.wav"), p("am-thanh-gia.mp4"))
    if not os.path.exists(p("gia-mao.png")):
        with open(p("gia-mao.png"), "wb") as f:
            f.write(b"MZ fake exe")


if __name__ == "__main__":
    dam_bao(os.path.join(os.path.dirname(os.path.abspath(__file__)), "media"))
    print("xong")
