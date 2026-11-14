# E2E WATERMARK AM THANH / VIDEO (FP-02, kien truc 3.17 + 3.37):
#   am thanh: labeler nhan FLAC mang watermark chim → tua duoc (Range 206), lan hai lay tu bo dem; admin giai ma file
#             "ro ri" → dung labeler; chu du an nhan ban goc;
#   video:    labeler nhan danh sach phat HLS cua doan mau (.../index.m3u8) + chu de len trinh phat; mang giua DUNG
#             CHUNG giua hai labeler, mang dau / cuoi RIENG tung nguoi; chu du an nhan ban goc; API giai ma tu choi video.
# Chay: python e2e_watermark_av.py   (stack demo: E2E_PROFILE=demo)
import hashlib, os, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402


def giai_ma(noi_dung, ten):
    return requests.post(G + "/media/admin/watermark/decode", headers=admin, files={"file": (ten, noi_dung)})


def sha(b):
    return hashlib.sha256(b).hexdigest()


def cac_mang(url_danh_sach):
    """Tai danh sach phat → (noi dung, [url tuyet doi cua tung mang])."""
    r = requests.get(url_danh_sach)
    goc = url_danh_sach.rsplit("/", 1)[0] + "/"
    return r, [goc + d for d in r.text.splitlines() if d and not d.startswith("#")]


try:
    biz = dang_ky(["business"], "wmav")
    h.nap(biz, 2000000)
    lab = dang_ky(["labeler"], "wmav-l")
    lab2 = dang_ky(["labeler"], "wmav-l2")

    # ============================================================ AM THANH: watermark chim
    print("\n[audio]")
    tep = "cuoc-goi.wav"
    P, _, mau = h.du_an(biz, "wm audio", "audio", {"modality": "audio", "tools": [{"name": "doan", "kind": "temporalSegment", "classes": ["noi"]}]},
                        files=[os.path.join(h.M, tep)], red=1, tran=1)
    publish_va_duyet(biz, P)
    tham_gia(lab, P)
    t = nhan_task(lab, P, 20)
    kiem("labeler nhan task co link xem", t is not None and t.get("fileUrl"), t)

    t0 = time.time()
    r = requests.get(t["fileUrl"])
    lan_dau = time.time() - t0
    kiem("link labeler: 200, audio/flac, chong luu", r.status_code == 200 and r.headers.get("Content-Type", "").startswith("audio/flac")
         and "no-store" in r.headers.get("Cache-Control", ""), (r.status_code, r.headers.get("Content-Type")))
    goc = open(os.path.join(h.M, tep), "rb").read()
    kiem("noi dung KHAC ban goc (da nhung watermark)", r.content != goc and len(r.content) > 1000)

    t0 = time.time()
    r2 = requests.get(t["fileUrl"], headers={"Range": "bytes=0-1023"})
    kiem("tua (Range) → 206 dung 1024 byte, lay tu bo dem", r2.status_code == 206 and len(r2.content) == 1024
         and r2.content == r.content[:1024] and r2.headers.get("Content-Range", "").endswith(f"/{len(r.content)}"),
         (r2.status_code, r2.headers.get("Content-Range")))
    print(f"   (lan dau {lan_dau:.2f}s, lan hai {time.time() - t0:.2f}s)")

    g = giai_ma(r.content, "ro-ri.flac")
    j = g.json() if g.status_code == 200 else {}
    kiem("admin giai ma file ro ri → dung labeler", j.get("found") and j["viewer"]["userId"] == lab["id"] and j["mediaType"] == "audio", g.text)
    g = giai_ma(goc, tep)
    kiem("ban goc → khong co watermark", g.status_code == 200 and g.json()["found"] is False, g.text)
    kiem("labeler khong goi duoc API giai ma", requests.post(G + "/media/admin/watermark/decode", headers=lab["h"],
                                                             files={"file": (tep, goc)}).status_code == 403)
    url_chu = list(mau.values())[0].get("fileUrl")
    if url_chu:
        rc = requests.get(url_chu)
        kiem("chu du an nhan DUNG ban goc", rc.status_code == 200 and rc.content == goc, rc.status_code)

    # ============================================================ VIDEO: HLS theo doan mau + chu noi
    for tep in ("clip.mp4", "clip-hd.mp4"):
        print(f"\n[video {tep}]")
        P, _, mau = h.du_an(biz, f"wm video {tep}", "video",
                            {"modality": "video", "tools": [{"name": "doan", "kind": "temporalSegment", "classes": ["chay"]}]},
                            files=[os.path.join(h.M, tep)], red=1, tran=1)
        publish_va_duyet(biz, P)
        trang_thai = [x.get("mediaState") for x in requests.get(G + f"/projects/{P}/samples", headers=biz["h"]).json()["items"]]
        kiem("video da cat HLS san truoc khi publish (mediaState = ready)", trang_thai and all(x == "ready" for x in trang_thai), trang_thai)

        cua = {}
        for u in (lab, lab2):
            tham_gia(u, P)
            tt = nhan_task(u, P, 20)
            kiem("labeler nhan danh sach phat HLS cua doan mau", tt is not None and (tt.get("fileUrl") or "").endswith("/index.m3u8"), tt)
            kiem("kem chu de len trinh phat mang ma cua chinh labeler",
                 (tt.get("watermarkText") or "").startswith("nguồn: " + u["id"]), tt.get("watermarkText"))
            t0 = time.time()
            r, mang = cac_mang(tt["fileUrl"])
            kiem("danh sach phat: 200, mpegurl, chong luu", r.status_code == 200 and "mpegurl" in r.headers.get("Content-Type", "")
                 and "no-store" in r.headers.get("Cache-Control", "") and r.text.startswith("#EXTM3U") and "#EXT-X-ENDLIST" in r.text,
                 (r.status_code, r.headers.get("Content-Type"), r.text[:200]))
            noi = {}
            for url in mang:
                rm = requests.get(url)
                kiem(f"mang {url.rsplit('/', 2)[-2]}/{url.rsplit('/', 1)[-1]} → 200 video/mp2t",
                     rm.status_code == 200 and rm.headers.get("Content-Type", "").startswith("video/mp2t") and len(rm.content) > 1000,
                     (rm.status_code, rm.headers.get("Content-Type")))
                noi[url.rsplit("/", 2)[-2] + "/" + url.rsplit("/", 1)[-1]] = sha(rm.content)
            print(f"   ({len(mang)} mang, tai het {time.time() - t0:.2f}s)")
            cua[u["id"]] = noi
            kiem("mang dau / cuoi la mang rieng (p/...)", any(k.startswith("p/") for k in noi), list(noi))

        a, b = cua[lab["id"]], cua[lab2["id"]]
        chung = [k for k in a if k.startswith("c/")]
        rieng = [k for k in a if k.startswith("p/")]
        kiem("mang giua GIONG HET nhau giua hai labeler (dung chung, khong xu ly rieng)", all(a[k] == b.get(k) for k in chung), chung)
        kiem("mang dau / cuoi KHAC nhau giua hai labeler (chu mang ma rieng)", all(a[k] != b.get(k) for k in rieng), rieng)

        r = giai_ma(b"0" * 2000, "ro-ri.mp4")
        kiem("API giai ma tu choi video (doc chu tren hinh)", r.status_code == 400 and r.json().get("code") == "khong_can_giai_ma", r.text)
        url_chu = list(mau.values())[0].get("fileUrl")
        if url_chu:
            rc = requests.get(url_chu)
            kiem("chu du an nhan DUNG ban goc (file nguyen, khong HLS)",
                 rc.status_code == 200 and rc.content == open(os.path.join(h.M, tep), "rb").read() and not url_chu.endswith(".m3u8"),
                 rc.status_code)

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
