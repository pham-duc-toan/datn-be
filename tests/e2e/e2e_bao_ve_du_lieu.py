# E2E BAO VE DU LIEU (media-svc): chan tai ban goc (FP-03) + chu noi "nguồn: <nguoi xem> - <doanh nghiep> - <domain>"
# goc duoi phai anh (FP-02, kien truc 3.37). API giai ma chi con cho am thanh (anh doc thang chu tren hinh).
# Anh mau: data/mat.jpg (anh NASA, public domain), data/canh.png. Tu tao tai khoan + du an moi.
# Chay: python e2e_bao_ve_du_lieu.py   (stack demo: E2E_PROFILE=demo)
import hashlib, io, os, sys, time, zipfile
import numpy as np
import requests
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import dat_cau_hoi_cong_cu  # noqa: E402
from tro_giup import *  # noqa: E402,F401,F403

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")
GIA = 20000


def doc(ten):
    with open(os.path.join(DATA, ten), "rb") as f:
        return f.read()


ANH = {"mat.jpg": doc("mat.jpg"), "canh.png": doc("canh.png")}


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def sha(b):
    return hashlib.sha256(b).hexdigest()


def zip_anh(ten_anh):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for t in ten_anh:
            z.writestr(t, ANH[t])
    return buf.getvalue()


def giai_ma(noi_dung, ten="ro_ri.jpg", h=None, data=None):
    return requests.post(G + "/media/admin/watermark/decode", headers=h or admin, files={"file": (ten, noi_dung)}, data=data or {})


def tai(url):
    return requests.get(url)


def vung_khac(goc: bytes, moi: bytes) -> tuple[float, float]:
    """Ti le (tren, duoi) cua vung pixel khac nhau giua hai anh cung kich thuoc — chu noi chi o day anh."""
    a = np.asarray(Image.open(io.BytesIO(goc)).convert("RGB"), dtype=np.int16)
    b = np.asarray(Image.open(io.BytesIO(moi)).convert("RGB"), dtype=np.int16)
    ys = np.nonzero(np.abs(a - b).max(axis=2) > 40)[0]
    return (float(ys.min()) / a.shape[0], float(ys.max()) / a.shape[0]) if len(ys) else (1.0, 0.0)


try:
    biz = dang_ky(["business"], "bvdl-biz")
    lab1 = dang_ky(["labeler"], "bvdl-lab1")
    lab2 = dang_ky(["labeler"], "bvdl-lab2")
    rev = dang_ky(["labeler"], "bvdl-rev")
    nap_tien = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": TAG + "bvdl"}), json={"amountVnd": 1000000}).json()
    requests.post(G + f"/payments/sandbox/checkout/{nap_tien['intentId']}/pay?amount=1000000")
    cho(lambda: vi(biz)["businessAvailableVnd"] >= 1000000, 30)

    # =================================================================================
    print("\n[1] Chu du an: link qua media-svc, nhan DUNG ban goc")
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-bvdl] anh {TAG}", "description": "e2e", "modality": "image", "visibility": "public"})
    P = r.json()["id"]
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "image", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["co_nguoi", "khong"]}]}}).raise_for_status()
    dat_cau_hoi_cong_cu(biz["h"], P)
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"pricing": {"rewardVnd": 10000, "slots": 3, "passThresholdPercent": 60,
                                                                       "deadline": "2027-12-31T00:00:00Z"}}).raise_for_status()
    r = requests.post(G + f"/projects/{P}/samples/zip", headers=biz["h"], files={"file": ("anh.zip", zip_anh(["mat.jpg", "canh.png"]), "application/zip")}, data={"name": "anh"})
    kiem("nap 2 anh (ZIP)", r.status_code in (200, 201), r.text)
    mau = {m["originalName"]: m for m in requests.get(G + f"/projects/{P}/samples?pageSize=100", headers=biz["h"]).json()["items"]}
    kiem("link xem di qua gateway /media/v/, khong lo MinIO",
         len(mau) == 2 and all("/media/v/" in m["fileUrl"] and ":9000" not in m["fileUrl"] and "X-Amz" not in m["fileUrl"] for m in mau.values()),
         [m["fileUrl"] for m in mau.values()])
    r = tai(mau["mat.jpg"]["fileUrl"])
    kiem("chu du an tai fileUrl = DUNG ban goc (khong watermark)", r.status_code == 200 and sha(r.content) == sha(ANH["mat.jpg"]), (r.status_code, len(r.content)))
    kiem("phan hoi khong cho luu dem (no-store, nosniff, inline)",
         "no-store" in r.headers.get("Cache-Control", "") and r.headers.get("X-Content-Type-Options") == "nosniff"
         and r.headers.get("Content-Disposition") == "inline", dict(r.headers))

    # =================================================================================
    print("\n[2] Labeler: anh mang chu noi 'nguồn: <ma cua chinh ho> - ...' goc duoi phai; admin khong can giai ma")
    publish_va_duyet(biz, P)
    for u in (lab1, lab2):
        cho(lambda: requests.post(G + f"/projects/{P}/join", headers=u["h"]).status_code in (200, 409), 10)
    goc = {m["id"]: n for n, m in mau.items()}

    b1 = bat_dau(lab1, P)
    t1 = b1["questions"][0]
    kiem("labeler 1 mo bai: cau hoi co fileUrl qua media", "/media/v/" in (t1.get("fileUrl") or ""), t1)
    r1 = tai(t1["fileUrl"])
    ten1 = goc[t1["sampleId"]]
    kiem("labeler nhan anh KHAC ban goc (co chu noi)", r1.status_code == 200 and sha(r1.content) != sha(ANH[ten1]), (r1.status_code, ten1))
    tren, duoi = vung_khac(ANH[ten1], r1.content)
    kiem("chi vung day anh bi doi (chu noi goc duoi, phan con lai giu nguyen)", tren > 0.8 and duoi > 0.9, (tren, duoi))
    kiem("tai lai cung link → cung noi dung", sha(tai(t1["fileUrl"]).content) == sha(r1.content))

    b2 = bat_dau(lab2, P)
    t2 = next(q for q in b2["questions"] if q["sampleId"] == t1["sampleId"])
    r2 = tai(t2["fileUrl"])
    kiem("CUNG anh trong bai cua labeler 2 → chu khac labeler 1 (mang ma nguoi xem)", sha(r2.content) != sha(r1.content))

    kiem("anh / video → API giai ma tra 400 (doc chu tren hinh)",
         giai_ma(r1.content).status_code == 400 and code(giai_ma(r1.content)) == "khong_can_giai_ma")
    kiem("labeler goi API giai ma → 403", giai_ma(r1.content, h=lab1["h"]).status_code == 403)
    kiem("khong dang nhap → 401", requests.post(G + "/media/admin/watermark/decode", files={"file": ("x.jpg", r1.content)}).status_code == 401)
    r = giai_ma(b"khong phai file", ten="x.bin")
    kiem("file khong phai am thanh → 400 media_khong_hop_le", r.status_code == 400 and code(r) == "media_khong_hop_le", r.text)

    # =================================================================================
    print("\n[3] Link: bi sua → 403, het han → 410")
    token = t1["fileUrl"].rsplit("/", 1)[1]
    sua = token[:30] + ("B" if token[30] == "A" else "A") + token[31:]
    r = tai(t1["fileUrl"].rsplit("/", 1)[0] + "/" + sua)
    kiem("sua mot ky tu token → 403 link_khong_hop_le", r.status_code == 403 and code(r) == "link_khong_hop_le", r.text)
    kiem("token rac → 403", tai(G + "/media/v/abc").status_code == 403)
    dat_va_cho("storage.view_link_ttl", 10, ("project",))
    try:
        # Mo lai bai (GET) → link moi theo han moi.
        tt = requests.get(G + f"/tasks/attempts/{b1['attemptId']}", headers=lab1["h"]).json()["questions"][0]
        kiem("link moi xem duoc ngay", tai(tt["fileUrl"]).status_code == 200)
        time.sleep(11)
        r = tai(tt["fileUrl"])
        kiem("qua 10 giay → 410 link_het_han", r.status_code == 410 and code(r) == "link_het_han", r.text)
    finally:
        dat_va_cho("storage.view_link_ttl", 300, ("project",))

    # =================================================================================
    print("\n[4] Ket qua: chu du an xem nhan cua bai da nop → anh BAN GOC")
    for q in b1["questions"]:
        luu(lab1, b1["attemptId"], q["sampleId"], {"nhan": {"labelIds": ["co_nguoi"]}}).raise_for_status()
    kiem("labeler 1 nop ca bai", nop_bai(lab1, b1["attemptId"]).status_code == 200)
    ds_chu = cho(lambda: [a for a in requests.get(G + f"/annotations/projects/{P}?status=submitted", headers=biz["h"]).json()["items"]
                          if a["sampleId"] == t1["sampleId"]], 20)
    kiem("chu du an tai anh cua nhan = ban goc", bool(ds_chu) and sha(tai(ds_chu[0]["fileUrl"]).content) == sha(ANH[ten1]))
    kiem("labeler khac khong xem duoc nhan cua du an → 404",
         requests.get(G + f"/annotations/projects/{P}", headers=lab2["h"]).status_code in (403, 404))

    # =================================================================================
    print("\n[5] Admin tat watermark (setting) → labeler nhan ban goc (khong chu noi)")
    # media-svc khong co DB: giu setting trong bo nho → cho toi khi anh labeler doi.
    def anh_la_goc():
        tt = requests.get(G + f"/tasks/attempts/{b2['attemptId']}", headers=lab2["h"]).json()["questions"][0]
        return sha(tai(tt["fileUrl"]).content) == sha(ANH[goc[tt["sampleId"]]])
    dat("media.watermark_enabled", False)
    try:
        kiem("tat watermark → anh labeler = ban goc", cho(anh_la_goc, 15))
    finally:
        dat("media.watermark_enabled", True)
    kiem("bat lai → co watermark", cho(lambda: not anh_la_goc(), 15))

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e bao ve du lieu: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
