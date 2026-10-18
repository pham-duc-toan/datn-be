# E2E BAO VE DU LIEU (media-svc): chan tai ban goc (FP-03) + watermark chim mang ma nguoi xem (FP-02),
# API giai ma anh ro ri ra dung nguoi.
# Anh mau: data/mat.jpg (anh NASA, public domain), data/canh.png. Tu tao tai khoan + du an moi.
# Chay: python e2e_bao_ve_du_lieu.py   (stack demo: E2E_PROFILE=demo)
import hashlib, io, os, sys, time, zipfile
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
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
    requests.put(G + f"/projects/{P}/label-schema", headers=biz["h"], json={"modality": "image", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["co_nguoi", "khong"]}]}).raise_for_status()
    requests.put(G + f"/projects/{P}/guideline", headers=biz["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    requests.put(G + f"/projects/{P}/pricing", headers=biz["h"], json={"rewardVnd": 10000, "slots": 3, "passThresholdPercent": 60,
                                                                       "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    r = requests.post(G + f"/projects/{P}/datasets", headers=biz["h"], files={"file": ("anh.zip", zip_anh(["mat.jpg", "canh.png"]), "application/zip")}, data={"name": "anh"})
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
    kiem("ban goc khong mang ma ai", giai_ma(r.content).json().get("found") is False)

    # =================================================================================
    print("\n[2] Labeler: anh mang watermark chim = ma cua chinh ho; admin giai ma ra dung nguoi")
    publish_va_duyet(biz, P)
    for u in (lab1, lab2):
        cho(lambda: requests.post(G + f"/projects/{P}/join", headers=u["h"]).status_code in (200, 409), 10)
    goc = {m["id"]: n for n, m in mau.items()}

    b1 = bat_dau(lab1, P)
    t1 = b1["questions"][0]
    kiem("labeler 1 mo bai: cau hoi co fileUrl qua media", "/media/v/" in (t1.get("fileUrl") or ""), t1)
    r1 = tai(t1["fileUrl"])
    ten1 = goc[t1["sampleId"]]
    kiem("labeler nhan anh KHAC ban goc (co watermark)", r1.status_code == 200 and sha(r1.content) != sha(ANH[ten1]), (r1.status_code, ten1))
    kiem("tai lai cung link → cung noi dung", sha(tai(t1["fileUrl"]).content) == sha(r1.content))
    g = giai_ma(r1.content).json()
    kiem("admin giai ma anh 'ro ri' cua labeler 1 → dung labeler 1",
         g.get("found") and g["viewer"]["type"] == "user" and g["viewer"]["userId"] == lab1["id"] and g["confidence"] > 0.6, g)

    b2 = bat_dau(lab2, P)
    t2 = next(q for q in b2["questions"] if q["sampleId"] == t1["sampleId"])
    r2 = tai(t2["fileUrl"])
    g2 = giai_ma(r2.content).json()
    kiem("CUNG anh trong bai cua labeler 2 → giai ra labeler 2 (khong nham labeler 1)",
         sha(r2.content) != sha(r1.content) and g2.get("found") and g2["viewer"]["userId"] == lab2["id"], g2)

    # Anh thu hai cua cung nguoi → van giai ra nguoi do (nguoi xem nhung thang trong anh).
    t1b = b1["questions"][1]
    g1b = giai_ma(tai(t1b["fileUrl"]).content).json()
    kiem("anh thu hai cua labeler 1 → van giai ra labeler 1", g1b.get("found") and g1b["viewer"]["userId"] == lab1["id"], g1b)

    kiem("labeler goi API giai ma → 403", giai_ma(r1.content, h=lab1["h"]).status_code == 403)
    kiem("khong dang nhap → 401", requests.post(G + "/media/admin/watermark/decode", files={"file": ("x.jpg", r1.content)}).status_code == 401)
    r = giai_ma(b"khong phai anh")
    kiem("file khong phai anh → 400 anh_khong_hop_le", r.status_code == 400 and code(r) == "anh_khong_hop_le", r.text)
    r = giai_ma(r1.content, data={"width": "512"})
    kiem("width ma thieu height → 400 kich_thuoc_khong_hop_le", r.status_code == 400 and code(r) == "kich_thuoc_khong_hop_le", r.text)
    g = giai_ma(r1.content, data={"width": "512", "height": "512"}).json()
    kiem("gui kem kich thuoc goc van giai dung", g.get("found") and g["viewer"]["userId"] == lab1["id"], g)

    # =================================================================================
    print("\n[3] Link: bi sua → 403, het han → 410")
    token = t1["fileUrl"].rsplit("/", 1)[1]
    sua = token[:30] + ("B" if token[30] == "A" else "A") + token[31:]
    r = tai(t1["fileUrl"].rsplit("/", 1)[0] + "/" + sua)
    kiem("sua mot ky tu token → 403 link_khong_hop_le", r.status_code == 403 and code(r) == "link_khong_hop_le", r.text)
    kiem("token rac → 403", tai(G + "/media/v/abc").status_code == 403)
    dat_va_cho("storage.view_link_ttl", 10, ("task",))
    try:
        # Mo lai bai (GET) → link moi theo han moi.
        tt = requests.get(G + f"/tasks/attempts/{b1['attemptId']}", headers=lab1["h"]).json()["questions"][0]
        kiem("link moi xem duoc ngay", tai(tt["fileUrl"]).status_code == 200)
        time.sleep(11)
        r = tai(tt["fileUrl"])
        kiem("qua 10 giay → 410 link_het_han", r.status_code == 410 and code(r) == "link_het_han", r.text)
    finally:
        dat_va_cho("storage.view_link_ttl", 300, ("task",))

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
    print("\n[5] Admin tat watermark (setting) → labeler nhan ban goc")
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
