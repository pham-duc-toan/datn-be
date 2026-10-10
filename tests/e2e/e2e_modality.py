# E2E da loai du lieu qua gateway. In PASS / FAIL tung buoc.
import json, os, subprocess, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
import tao_media  # noqa: E402

M = os.path.join(os.path.dirname(os.path.abspath(__file__)), "media")
tao_media.dam_bao(M)
PW = "Matkhau@123"
KET_QUA = {"pass": 0, "fail": 0}


def kiem(ten, dk, chi_tiet=""):
    if dk:
        KET_QUA["pass"] += 1
        print("  PASS", ten)
    else:
        KET_QUA["fail"] += 1
        print("  FAIL", ten, "|", str(chi_tiet)[:600])


def login(email):
    r = requests.post(G + "/auth/login", json={"email": email, "password": PW})
    r.raise_for_status()
    return {"Authorization": "Bearer " + r.json()["accessToken"]}


def cho(dk, giay=30, buoc=0.5):
    het = time.time() + giay
    while time.time() < het:
        v = dk()
        if v:
            return v
        time.sleep(buoc)
    return None


admin = login("admin@crowd.local")
biz1 = login("doanhnghiep1@crowd.local")
lab1 = login("labeler1@crowd.local")
lab2 = login("labeler2@crowd.local")


def nhan_task(h, pid, cho_dong_bo=False):
    r = requests.post(G + "/tasks/projects/%s/next" % pid, headers=h)
    if cho_dong_bo and r.status_code == 403:
        return None  # member.added / project.published chua toi task-svc
    if r.status_code == 204 or not r.text:
        return None
    r.raise_for_status()
    return r.json()


def nop(h, aid, payload):
    return requests.post(G + "/tasks/assignments/%s/submit" % aid, headers=h, json={"payload": payload})


def tha(h, aid):
    requests.post(G + "/tasks/assignments/%s/release" % aid, headers=h)


def du_an_seed(ten_chua):
    r = requests.get(G + "/projects?mine=true&pageSize=100", headers=biz1).json()
    items = r["items"] if isinstance(r, dict) else r
    for p in items:
        if p["name"].startswith("[Seed]") and ten_chua in p["name"]:
            return p
    raise Exception("khong thay du an " + ten_chua)


# =====================================================================
print("\n[1] Du lieu cu sau migration doc duoc")
r = requests.get(G + "/projects/3d904a1a-3920-530b-b055-793baae64f1b", headers=biz1)
kiem("GET du an P1 (anh cu) 200", r.status_code == 200, r.text)
ls = r.json().get("labelSchema") if r.status_code == 200 else None
kiem("P1 labelSchema dang tools, modality image", ls is not None and ls.get("modality") == "image" and ls["tools"][0]["name"] == "label", ls)
r = requests.get(G + "/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/results", headers=biz1)
kiem("ket qua P1 (nhan cu) gop duoc", r.status_code == 200 and r.json()["sampleCount"] > 0, r.text)
if r.status_code == 200:
    s0 = r.json()["samples"][0]
    kiem("ket qua co tools.label.method=majority", s0["tools"]["label"]["method"] == "majority", s0)
r = requests.get(G + "/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b?status=approved", headers=biz1)
kiem("danh sach nhan cu doc duoc payload moi", r.status_code == 200 and r.json()["items"][0]["payload"]["data"].get("label") is not None, r.text)

# =====================================================================
print("\n[2] P5 van ban: cam xuc + NER (span)")
p5 = du_an_seed("Van ban")
t = nhan_task(lab1, p5["id"])
if t is None:
    print("  SKIP P5 seed het task cho lab1 (cac ca kiem tra da phu bang du an moi: e2e_tong_the 11b, muc [6] ben duoi)")
else:
    kiem("lab1 nhan task P5", t["modality"] == "text" and t["content"]["text"], t)
if t:
    text = t["content"]["text"]
    kiem("task co labelSchema 2 cong cu", len(t["labelSchema"]["tools"]) == 2, t["labelSchema"])
    kiem("task khong co fileUrl (text)", t.get("fileUrl") is None, t)
    r = nop(lab1, t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "DIA_DIEM", "start": 0, "end": len(text) + 5}]})
    kiem("span vuot do dai van ban → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"cam_xuc": {"labelIds": ["vui_ve"]}, "thuc_the": []})
    kiem("lop khong co trong tap nhan → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "DIA_DIEM", "start": 0, "end": min(3, len(text))}]})
    kiem("nop hop le 200", r.status_code == 200, r.text)

# =====================================================================
print("\n[3] P6 am thanh: chep loi + doan thoi gian")
p6 = du_an_seed("Am thanh")
t = nhan_task(lab1, p6["id"])
if t is None:
    print("  SKIP P6 seed het task cho lab1 (cac ca kiem tra da phu bang du an moi: e2e_tong_the 11b, muc [6] ben duoi)")
else:
    kiem("lab1 nhan task P6", t["modality"] == "audio" and t["fileUrl"], t)
if t:
    md = t["metadata"]
    dai = md["durationSec"]
    kiem("metadata co durationSec", dai > 0, md)
    am = requests.get(t["fileUrl"])
    kiem("fileUrl tai duoc file WAV", am.status_code == 200 and am.content[:4] == b"RIFF", am.status_code)
    r = nop(lab1, t["assignmentId"], {"loi_noi": {"text": "xin chao"}, "doan": [{"labelId": "giong_noi", "start": 0, "end": dai + 3}]})
    kiem("doan vuot thoi luong mau → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"loi_noi": {"text": "xin chao"}, "doan": [{"labelId": "giong_noi", "start": 0, "end": min(2, dai)}]})
    kiem("nop hop le 200", r.status_code == 200, r.text)

# =====================================================================
print("\n[4] P7 cap cau tra loi (pairwise)")
p7 = du_an_seed("So sanh cap")
t = nhan_task(lab1, p7["id"])
if t is None:
    print("  SKIP P7 seed het task cho lab1 (cac ca kiem tra da phu bang du an moi: e2e_tong_the 11b, muc [6] ben duoi)")
else:
    kiem("lab1 nhan task P7", t["modality"] == "pair" and t["content"].get("a"), t)
if t:
    r = nop(lab1, t["assignmentId"], {"tot_hon": {"choice": "c"}, "an_toan": {"labelIds": ["an_toan"]}})
    kiem("choice sai → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"tot_hon": {"choice": "b"}, "an_toan": {"labelIds": ["an_toan"]}})
    kiem("nop hop le 200", r.status_code == 200, r.text)
r = requests.get(G + "/annotations/projects/%s/results" % p7["id"], headers=biz1)
kiem("ket qua P7: majority tot_hon = a (2 phieu seed)", r.status_code == 200 and any(
    s["tools"]["tot_hon"].get("final") == {"choice": "a"} for s in r.json()["samples"]), r.text)


# =====================================================================
def du_an_moi(ten, modality, tap_nhan, so_mau_du_kien_toi_thieu=1):
    r = requests.post(G + "/projects", headers=biz1, json={"name": ten, "description": "e2e", "modality": modality, "visibility": "public"})
    kiem("tao du an " + modality, r.status_code in (200, 201), r.text)
    pid = r.json()["id"]
    r = requests.put(G + "/projects/%s/label-schema" % pid, headers=biz1, json=tap_nhan)
    kiem("dat tap nhan", r.status_code == 200, r.text)
    r = requests.put(G + "/projects/%s/guideline" % pid, headers=biz1, json={"markdown": "e2e", "examples": []})
    kiem("dat huong dan", r.status_code == 200, r.text)
    return pid


def upload(pid, files):
    r = requests.post(G + "/projects/%s/uploads" % pid, headers=biz1,
                      json={"files": [{"name": os.path.basename(f), "sizeBytes": os.path.getsize(f)} for f in files]})
    kiem("xin link upload %d file" % len(files), r.status_code == 200, r.text)
    keys = []
    for slot, f in zip(r.json(), files):
        with open(f, "rb") as fh:
            put = requests.put(slot["uploadUrl"], data=fh.read())
        kiem("PUT thang len MinIO " + os.path.basename(f), put.status_code == 200, put.text)
        keys.append(slot["key"])
    return keys


def nap_manifest(pid, rows=None, manifest_key=None):
    body = {"name": "lo-e2e"}
    if rows is not None:
        body["rows"] = rows
    if manifest_key is not None:
        body["manifestKey"] = manifest_key
    r = requests.post(G + "/projects/%s/datasets/manifest" % pid, headers=biz1, json=body)
    kiem("tao lo manifest → 202 Pending", r.status_code == 202 and r.json()["status"] == "pending", r.text)
    did = r.json()["id"]

    def xong():
        ds = requests.get(G + "/projects/%s/datasets" % pid, headers=biz1).json()
        d = [x for x in ds if x["id"] == did][0]
        return d if d["status"] in ("ready", "failed") else None

    d = cho(xong, 60)
    kiem("worker xu ly xong lo", d is not None, d)
    return d


def mau(pid):
    return requests.get(G + "/projects/%s/samples?page=1&pageSize=100" % pid, headers=biz1).json()["items"]


def chay_du_an(pid, so_mau, ngan_sach=50000):
    r = requests.put(G + "/projects/%s/pricing" % pid, headers=biz1,
                     json={"unitPriceVnd": 1000, "redundancy": 1, "budgetVnd": ngan_sach, "deadline": "2027-12-31T00:00:00Z"})
    kiem("dat gia", r.status_code == 200, r.text)
    r = requests.post(G + "/projects/%s/publish" % pid, headers=biz1)
    kiem("publish", r.status_code == 200, r.text)
    st = cho(lambda: (lambda s: s if s == "pendingApproval" else None)(
        requests.get(G + "/projects/" + pid, headers=biz1).json()["status"]), 20)
    kiem("ky quy xong → pendingApproval", st == "pendingApproval", requests.get(G + "/projects/" + pid, headers=biz1).text)
    r = requests.post(G + "/projects/%s/approve" % pid, headers=admin)
    kiem("admin duyet", r.status_code == 200, r.text)
    r = requests.post(G + "/projects/%s/join" % pid, headers=lab1)
    kiem("lab1 tham gia", r.status_code == 200, r.text)
    # Cho task-svc nhan project.published + member.added + dataset.ingested.
    t = cho(lambda: nhan_task(lab1, pid, True), 20, 1)
    kiem("lab1 nhan duoc task", t is not None, t)
    return t


def duyet_tat_ca(pid):
    def ds():
        items = requests.get(G + "/annotations/projects/%s?status=pendingReview" % pid, headers=biz1).json()["items"]
        return items if items else None
    items = cho(ds, 20) or []
    for a in items:
        requests.post(G + "/annotations/%s/approve" % a["id"], headers=biz1)
    kiem("duyet %d nhan" % len(items), len(items) > 0)
    return items


# =====================================================================
print("\n[5] Du an ANH moi: bbox + polygon + phan loai, upload thang + manifest")
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:size=300x200:duration=1", "-frames:v", "1", os.path.join(M, "anh2.png")], check=True)
pid = du_an_moi("[e2e] Anh khung", "image", {
    "modality": "image",
    "tools": [
        {"name": "loai", "kind": "classification", "classes": ["ngoai_troi", "trong_nha"]},
        {"name": "vat", "kind": "bbox", "classes": ["xe", "nguoi"], "required": False},
        {"name": "vung", "kind": "polygon", "classes": ["duong"], "required": False},
    ]})
r = requests.post(G + "/projects/%s/uploads" % pid, headers=biz1, json={"files": [{"name": "virus.exe", "sizeBytes": 10}]})
kiem("upload duoi .exe cho du an anh → 400", r.status_code == 400, r.text)
keys = upload(pid, [os.path.join(M, "anh.png"), os.path.join(M, "anh2.png"), os.path.join(M, "gia-mao.png")])
d = nap_manifest(pid, rows=[{"file": keys[0], "name": "do.png"}, {"file": keys[1], "name": "xanh.png"},
                            {"file": keys[2], "name": "gia-mao.png"}, {"file": "khac-du-an/x.png"}])
kiem("2 mau, bo qua 2 (gia mao + khac du an)", d and d["sampleCount"] == 2 and d["skippedCount"] == 2, d)
kiem("ErrorSummary neu ro ly do", d and "khong phai anh" in (d.get("errorSummary") or "") and "khong thuoc du an" in d["errorSummary"], d)
ms = mau(pid)
kiem("mau anh co metadata width/height that", sorted((m["metadata"]["width"], m["metadata"]["height"]) for m in ms) == [(200, 150), (300, 200)], [m["metadata"] for m in ms])
mau_do = [m for m in ms if m["metadata"]["width"] == 200][0]
r = requests.post(G + "/projects/%s/gold-items" % pid, headers=biz1, json={"items": [{
    "sampleId": mau_do["id"], "purpose": "qualityCheck",
    "expectedPayload": {"loai": {"labelIds": ["ngoai_troi"]}, "vat": [{"labelId": "xe", "x": 500, "y": 0, "w": 10, "h": 10}]}}]})
kiem("cau vang khung ngoai anh → 400", r.status_code == 400, r.text)
r = requests.post(G + "/projects/%s/gold-items" % pid, headers=biz1, json={"items": [{
    "sampleId": mau_do["id"], "purpose": "qualityCheck",
    "expectedPayload": {"loai": {"labelIds": ["ngoai_troi"]}, "vat": [{"labelId": "xe", "x": 10, "y": 10, "w": 50, "h": 40}]}}]})
kiem("cau vang bbox hop le", r.status_code == 200, r.text)
t = chay_du_an(pid, 2)
if t:
    kiem("task la anh con lai (xanh 300x200)", t["metadata"]["width"] == 300, t["metadata"])
    r = nop(lab1, t["assignmentId"], {"loai": {"labelIds": ["ngoai_troi"]}, "vat": [{"labelId": "xe", "x": 250, "y": 150, "w": 100, "h": 100}]})
    kiem("bbox tran ra ngoai anh → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"loai": {"labelIds": ["ngoai_troi"]}, "vung": [{"labelId": "duong", "points": [[0, 0], [10, 0]]}]})
    kiem("polygon < 3 dinh → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {
        "loai": {"labelIds": ["ngoai_troi"]},
        "vat": [{"labelId": "xe", "x": 20, "y": 30, "w": 100, "h": 50}, {"labelId": "nguoi", "x": 150, "y": 20, "w": 30, "h": 80}],
        "vung": [{"labelId": "duong", "points": [[0, 200], [300, 200], [300, 150], [0, 150]]}]})
    kiem("nop bbox + polygon hop le", r.status_code == 200, r.text)
    duyet_tat_ca(pid)
    r = requests.get(G + "/annotations/projects/%s/results" % pid, headers=biz1)
    kiem("results: phan bo vat {xe:1,nguoi:1}", r.status_code == 200 and r.json()["labelDistribution"]["vat"] == {"nguoi": 1, "xe": 1}, r.text)
    r = requests.get(G + "/annotations/projects/%s/export?format=coco" % pid, headers=biz1)
    ok = r.status_code == 200
    coco = r.json() if ok else {}
    kiem("export COCO: 1 anh, 3 annotation, 3 danh muc", ok and len(coco["images"]) == 1 and len(coco["annotations"]) == 3 and len(coco["categories"]) == 3, r.text[:400])
    if ok:
        poly = [a for a in coco["annotations"] if a["segmentation"]][0]
        kiem("COCO polygon: area 15000, bbox [0,150,300,50]", poly["area"] == 15000 and poly["bbox"] == [0, 150, 300, 50], poly)
        kiem("COCO image co width/height", coco["images"][0]["width"] == 300 and coco["images"][0]["height"] == 200, coco["images"])
    r = requests.get(G + "/annotations/projects/%s/export?format=csv" % pid, headers=biz1)
    kiem("export CSV co cot tung cong cu", r.status_code == 200 and "loai,vat,vung" in r.content.decode("utf-8-sig").splitlines()[0], r.text[:300])

# =====================================================================
print("\n[6] Du an AM THANH moi: cat doan 10 giay (file 23 giay)")
pid = du_an_moi("[e2e] Am thanh cat doan", "audio", {
    "modality": "audio", "segmentSeconds": 10,
    "tools": [{"name": "chep", "kind": "transcription"}, {"name": "doan", "kind": "temporalSegment", "classes": ["noi", "im"], "required": False}]})
r = requests.post(G + "/projects/%s/datasets" % pid, headers=biz1, files={"file": ("a.zip", b"PK\x03\x04", "application/zip")}, data={"name": "zip"})
kiem("ZIP cho du an audio → tu choi (zip_chi_cho_anh)", r.status_code in (400, 409) and "zip_chi_cho_anh" in r.text, r.text)
keys = upload(pid, [os.path.join(M, "cuoc-goi.wav")])
d = nap_manifest(pid, rows=[{"file": keys[0], "name": "cuoc-goi.wav"}])
kiem("23 giay → 3 doan (0-10, 10-20, 20-23)", d and d["sampleCount"] == 3, d)
ms = sorted(mau(pid), key=lambda m: m["metadata"].get("segmentStart", 0))
kiem("metadata doan dung (ffprobe that)", [(m["metadata"].get("segmentStart"), m["metadata"].get("segmentEnd")) for m in ms] == [(0, 10), (10, 20), (20, 23)], [m["metadata"] for m in ms])
kiem("ca 3 doan dung chung mot file", len(set(m["fileUrl"].split("?")[0] for m in ms)) == 1)
t = chay_du_an(pid, 3)
if t:
    dai = t["metadata"]["durationSec"]
    r = nop(lab1, t["assignmentId"], {"chep": {"text": "a" * 6000}})
    kiem("ban chep vuot maxLength → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"chep": {"text": "xin chao"}, "doan": [{"labelId": "noi", "start": 0, "end": dai + 3}]})
    kiem("doan thoi gian vuot thoi luong doan → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"chep": {"text": "xin chao"}, "doan": [{"labelId": "nhac", "start": 0, "end": 1}]})
    kiem("lop doan khong co trong tap nhan → 400", r.status_code == 400, r.text)
    r = nop(lab1, t["assignmentId"], {"chep": {"text": "xin chao cac ban"}, "doan": [{"labelId": "noi", "start": 0.5, "end": dai - 0.5}]})
    kiem("nop hop le", r.status_code == 200, r.text)

# =====================================================================
print("\n[7] Du an VIDEO moi: ffprobe doc kich thuoc + thoi luong")
pid = du_an_moi("[e2e] Video", "video", {
    "modality": "video", "tools": [{"name": "canh", "kind": "classification", "classes": ["ngay", "dem"]},
                                   {"name": "su_kien", "kind": "temporalSegment", "classes": ["xe_qua"], "required": False}]})
r = requests.post(G + "/projects/%s/uploads" % pid, headers=biz1, json={"files": [{"name": "a.wav", "sizeBytes": 10}]})
kiem("xin link .wav cho du an video → 400 (loc so bo theo duoi)", r.status_code == 400, r.text)
keys = upload(pid, [os.path.join(M, "clip.mp4"), os.path.join(M, "am-thanh-gia.mp4")])
d = nap_manifest(pid, rows=[{"file": keys[0]}, {"file": keys[1]}])
kiem("mp4 that nhan, WAV doi ten .mp4 bi ffprobe loai (khong co hinh)", d and d["sampleCount"] == 1 and "khong phai video" in (d.get("errorSummary") or ""), d)
ms = mau(pid)
kiem("metadata video 320x240, ~12 giay", ms and ms[0]["metadata"]["width"] == 320 and ms[0]["metadata"]["height"] == 240 and abs(ms[0]["metadata"]["durationSec"] - 12) < 0.5, ms and ms[0]["metadata"])
t = chay_du_an(pid, 1)
if t:
    r = nop(lab1, t["assignmentId"], {"canh": {"labelIds": ["ngay"]}, "su_kien": [{"labelId": "xe_qua", "start": 2, "end": 4.5}]})
    kiem("nop video hop le", r.status_code == 200, r.text)

# =====================================================================
print("\n[8] Du an TEXT moi qua file manifest .jsonl trong MinIO")
pid = du_an_moi("[e2e] Van ban jsonl", "text", {
    "modality": "text", "tools": [{"name": "y_dinh", "kind": "classification", "classes": ["hoi", "khen", "che"]}]})
p = os.path.join(M, "cau.jsonl")
with open(p, "w", encoding="utf-8") as f:
    for c in ["San pham tot qua", "Giao hang cham", "Shop mo cua may gio?", "San pham tot qua", "khong phai json"]:
        f.write((json.dumps({"text": c}, ensure_ascii=False) if c != "khong phai json" else c) + "\n")
keys = upload(pid, [p])
d = nap_manifest(pid, manifest_key=keys[0])
kiem("4 dong hop le → 3 mau (1 trung), 2 bo qua", d and d["sampleCount"] == 3 and d["skippedCount"] == 2, d)
ms = mau(pid)
kiem("mau text co content + length", all(m["content"]["text"] and m["metadata"]["length"] == len(m["content"]["text"]) for m in ms), ms)

print("\nTONG: %d PASS, %d FAIL" % (KET_QUA["pass"], KET_QUA["fail"]))
sys.exit(1 if KET_QUA["fail"] else 0)
