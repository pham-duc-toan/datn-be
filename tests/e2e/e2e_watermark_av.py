# E2E WATERMARK AM THANH / VIDEO (FP-02):
#   labeler nhan link xem → file mang watermark (am thanh: FLAC, video: H.264 ma hoa lai) → tua duoc (Range 206);
#   lan mo thu hai lay tu bo dem (nhanh); admin giai ma file "ro ri" → dung labeler; chu du an nhan ban goc.
# Chay: python e2e_watermark_av.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402


def giai_ma(noi_dung, ten):
    return requests.post(G + "/media/admin/watermark/decode", headers=admin, files={"file": (ten, noi_dung)})


try:
    biz = dang_ky(["business"], "wmav")
    h.nap(biz, 2000000)
    lab = dang_ky(["labeler"], "wmav-l")

    for loai, tep, tool, duoi, mime in (
        ("audio", "cuoc-goi.wav", {"name": "doan", "kind": "temporalSegment", "classes": ["noi"]}, "flac", "audio/flac"),
        ("video", "clip.mp4", {"name": "doan", "kind": "temporalSegment", "classes": ["chay"]}, "mp4", "video/mp4"),
        # 720p: chi nhung khung dau + cuoi (ban chuan dung chung, phan giua copy) — nhanh, khong phu thuoc do dai.
        ("video", "clip-hd.mp4", {"name": "doan", "kind": "temporalSegment", "classes": ["chay"]}, "mp4", "video/mp4"),
    ):
        print(f"\n[{loai}]")
        P, _, mau = h.du_an(biz, f"wm {loai}", loai, {"modality": loai, "tools": [tool]}, files=[os.path.join(h.M, tep)], red=1, tran=1)
        publish_va_duyet(biz, P)
        if loai == "video":
            trang_thai = [x.get("mediaState") for x in requests.get(G + f"/projects/{P}/samples", headers=biz["h"]).json()["items"]]
            kiem("video da chuan bi san truoc khi publish (mediaState = ready)", trang_thai and all(x == "ready" for x in trang_thai), trang_thai)
        tham_gia(lab, P)
        t = nhan_task(lab, P, 20)
        kiem("labeler nhan task co link xem", t is not None and t.get("fileUrl"), t)

        t0 = time.time()
        r = requests.get(t["fileUrl"])
        lan_dau = time.time() - t0
        kiem(f"link labeler: 200, {mime}, chong luu", r.status_code == 200 and r.headers.get("Content-Type", "").startswith(mime)
             and "no-store" in r.headers.get("Cache-Control", ""), (r.status_code, r.headers.get("Content-Type")))
        goc = open(os.path.join(h.M, tep), "rb").read()
        kiem("noi dung KHAC ban goc (da nhung watermark)", r.content != goc and len(r.content) > 1000)

        t0 = time.time()
        r2 = requests.get(t["fileUrl"], headers={"Range": "bytes=0-1023"})
        kiem("tua (Range) → 206 dung 1024 byte, lay tu bo dem", r2.status_code == 206 and len(r2.content) == 1024
             and r2.content == r.content[:1024] and r2.headers.get("Content-Range", "").endswith(f"/{len(r.content)}"),
             (r2.status_code, r2.headers.get("Content-Range")))
        print(f"   (lan dau {lan_dau:.2f}s, lan hai {time.time() - t0:.2f}s)")

        g = giai_ma(r.content, f"ro-ri.{duoi}")
        j = g.json() if g.status_code == 200 else {}
        kiem("admin giai ma file ro ri → dung labeler", j.get("found") and j["viewer"]["userId"] == lab["id"] and j["mediaType"] == loai, g.text)
        g = giai_ma(goc, tep)
        kiem("ban goc → khong co watermark", g.status_code == 200 and g.json()["found"] is False, g.text)
        kiem("labeler khong goi duoc API giai ma", requests.post(G + "/media/admin/watermark/decode", headers=lab["h"],
                                                                 files={"file": (tep, goc)}).status_code == 403)

        url_chu = list(mau.values())[0].get("fileUrl")
        if url_chu:
            rc = requests.get(url_chu)
            kiem("chu du an nhan DUNG ban goc", rc.status_code == 200 and rc.content == goc, rc.status_code)

    print("\n[tat watermark]")
    dat_va_cho("media.watermark_enabled", False, [])
    time.sleep(3)
    P, _, _ = h.du_an(biz, "wm tat", "audio", {"modality": "audio", "tools": [{"name": "d", "kind": "temporalSegment", "classes": ["x"]}]},
                      files=[os.path.join(h.M, "cuoc-goi.wav")], red=1, tran=1)
    publish_va_duyet(biz, P)
    tham_gia(lab, P)
    t = nhan_task(lab, P, 20)
    r = requests.get(t["fileUrl"])
    kiem("media.watermark_enabled = false → phat ban goc", r.content == open(os.path.join(h.M, "cuoc-goi.wav"), "rb").read())
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
