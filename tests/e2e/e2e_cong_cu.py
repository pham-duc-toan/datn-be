# E2E CONG CU GAN NHAN + GOP NHAN TU DONG (kien truc muc 3.15) tren luong du an lam trung tam (3.18) —
# tu tao tai khoan va du an moi.
#
# Phu cac cong cu them sau va viec gop nhan nhieu nguoi cho MOI cong cu:
#   [1] anh: khung + thuoc tinh (OCR), diem moc, duong gap khuc, mat na, so, chep chu, van ban tu do
#       → kiem dinh dang LUC NOP CA BAI, ket qua gop (annotation-svc), xuat COCO / CSV / YOLO
#   [2] diem moc lech nhau: 3 bai → da so (2 diem gan nhau) → bai lech bi cham khong giong
#   [3] van ban: doan + QUAN HE giua cac doan
#   [4] du lieu DANH SACH: xep hang (Borda) + thang diem (trung vi)
#   [5] video: theo doi doi tuong (chuoi khung)
#   [6] bai test dau vao + cau vang kiem tra bang diem moc — cham theo do lech so voi duong cheo anh
#   [7] setting nguong ghep doi luc dang chay
# Chay: python e2e_cong_cu.py   (stack demo: E2E_PROFILE=demo). Can ffmpeg (sinh anh / video mau).
import copy, io, os, subprocess, sys, zipfile
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import tao_media  # noqa: E402

M = os.path.join(os.path.dirname(os.path.abspath(__file__)), "media")
tao_media.dam_bao(M)
GIA = 1000


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


def anh_mau(ten, mau, rong, cao):
    """Sinh mot anh mot mau bang ffmpeg (moi mau mot noi dung khac nhau — project-svc bo anh trung)."""
    p = os.path.join(M, ten)
    if not os.path.exists(p):
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", f"color=c={mau}:size={rong}x{cao}:duration=1",
                        "-frames:v", "1", p], check=True)
    return p


def du_an(chu, ten, modality, tap_nhan, rows=None, files=None, suat=3, dieu_kien=None):
    """Tao du an nhap + nap du lieu + gia CA DU AN. Tra (pid, lo du lieu, {ten mau: mau})."""
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-cong-cu] {ten} {TAG}", "description": "e2e", "modality": modality, "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    r = requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json=tap_nhan)
    assert r.status_code == 200, r.text
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    if dieu_kien:
        requests.put(G + f"/projects/{pid}/eligibility", headers=chu["h"], json=dieu_kien).raise_for_status()

    if files is not None:
        r = requests.post(G + f"/projects/{pid}/uploads", headers=chu["h"],
                          json={"files": [{"name": os.path.basename(f), "sizeBytes": os.path.getsize(f)} for f in files]})
        assert r.status_code == 200, r.text
        rows = []
        for o, f in zip(r.json(), files):
            with open(f, "rb") as fh:
                requests.put(o["uploadUrl"], data=fh.read()).raise_for_status()
            rows.append({"file": o["key"], "name": os.path.basename(f)})

    r = requests.post(G + f"/projects/{pid}/datasets/manifest", headers=chu["h"], json={"name": "lo", "rows": rows})
    assert r.status_code == 202, r.text
    did = r.json()["id"]
    lo = cho(lambda: next((d for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json()
                           if d["id"] == did and d["status"] in ("ready", "failed")), None), 90)
    assert lo is not None, "lo du lieu chua xu ly xong"
    mau = {m["originalName"]: m for m in requests.get(G + f"/projects/{pid}/samples?pageSize=200", headers=chu["h"]).json()["items"]}
    dat_gia(chu, pid, max(10000, GIA * max(1, len(mau))), suat)
    return pid, lo, mau


def nop_nhan(u, aid, payload):
    """aid tu nhan_task ("<attemptId>|<sampleId>"): luu cau roi NOP CA BAI."""
    attempt_id, sid = aid.split("|")
    luu(u, attempt_id, sid, payload).raise_for_status()
    return nop_bai(u, attempt_id)


def tu_choi(u, aid, payload, ten):
    """Nhan SAI: nop bai phai 400 bai_chua_hop_le (kem ma loi nhan) va bai con mo (sua roi nop lai duoc)."""
    r = nop_nhan(u, aid, payload)
    kiem(ten + " → 400", r.status_code == 400 and "bai_chua_hop_le" in r.text and "nhan_" in r.text, r.text)


def cham(chu, pid, so_bai, giay=60):
    """Dong thu bai (neu chua) va doi cham. → {labelerId: AttemptSummary}."""
    if requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "running":
        ket_thuc_som(chu, pid)

    def xong():
        ds_ = requests.get(G + f"/tasks/projects/{pid}/attempts", headers=chu["h"]).json()
        ds_ = [b for b in ds_ if b["state"] == "submitted"]
        return ds_ if len(ds_) == so_bai and all(b["passed"] is not None for b in ds_) else None
    ds_ = cho(xong, giay)
    return None if ds_ is None else {b["labelerId"]: b for b in ds_}


def giong(kq):
    """{labelerId: bai} → {labelerId: tat ca cau so duoc deu giong?}"""
    return None if kq is None else {k: b["matchedCount"] == b["comparableCount"] for k, b in kq.items()}


def da_so(pid):
    return sql("quality", f"select string_agg(status, ',' order by sample_id) from sample_majorities where project_id = '{pid}'")


def ket_qua(chu, pid):
    r = requests.get(G + f"/annotations/projects/{pid}/results", headers=chu["h"])
    assert r.status_code == 200, r.text
    return r.json()


def gan(a, b, sai_so=1e-6):
    return abs(a - b) <= sai_so


try:
    biz = dang_ky(["business"], "biz")
    nap(biz, 5_000_000)
    l1, l2, l3 = (dang_ky(["labeler"], f"lab{i}") for i in (1, 2, 3))
    ID = {l1["id"]: "l1", l2["id"]: "l2", l3["id"]: "l3"}

    # =================================================================================
    print("\n[1] Anh: khung + thuoc tinh (OCR), diem moc, duong gap khuc, mat na, so, chep chu, van ban tu do")
    TAP_NHAN_ANH = {"modality": "image", "tools": [
        {"name": "chu", "kind": "bbox", "classes": ["dong"], "attributes": [
            {"name": "noi_dung", "type": "text", "required": True, "maxLength": 50},
            {"name": "mau", "type": "choice", "options": ["do", "xanh"]}]},
        {"name": "moc", "kind": "keypoint", "classes": ["goc"], "required": False},
        {"name": "vach", "kind": "polyline", "classes": ["le"], "required": False},
        {"name": "vung", "kind": "mask", "classes": ["nen"], "required": False},
        {"name": "so_dong", "kind": "number", "integer": True, "min": 0, "max": 20},
        {"name": "tieu_de", "kind": "transcription", "required": False},
        {"name": "mo_ta", "kind": "freeText", "required": False}]}
    P1, lo, mau = du_an(biz, "Anh tong hop", "image", TAP_NHAN_ANH, files=[os.path.join(M, "anh.png")])
    kiem("nap 1 anh 200x150", lo["sampleCount"] == 1 and mau["anh.png"]["metadata"] == {"width": 200, "height": 150}, (lo, mau))
    ls = requests.get(G + f"/projects/{P1}", headers=biz["h"]).json()["labelSchema"]
    kiem("tap nhan luu dang chuan: thuoc tinh co maxLength / required, so co integer / tolerance",
         ls["tools"][0]["attributes"][0] == {"name": "noi_dung", "type": "text", "required": True, "maxLength": 50}
         and ls["tools"][4] == {"name": "so_dong", "kind": "number", "required": True, "integer": True, "tolerance": 0, "min": 0, "max": 20}, ls)

    r = requests.put(G + f"/projects/{P1}/label-schema", headers=biz["h"], json={"modality": "image", "tools": [{"name": "xep", "kind": "ranking"}]})
    kiem("cong cu ranking cho du an anh → 400", r.status_code == 400, r.text)
    r = requests.put(G + f"/projects/{P1}/label-schema", headers=biz["h"], json={"modality": "image", "tools": [
        {"name": "v", "kind": "bbox", "classes": ["a"], "attributes": [{"name": "x", "type": "choice"}]}]})
    kiem("thuoc tinh choice thieu options → 400", r.status_code == 400, r.text)
    requests.put(G + f"/projects/{P1}/label-schema", headers=biz["h"], json=TAP_NHAN_ANH).raise_for_status()

    publish_va_duyet(biz, P1)
    for u in (l1, l2):
        tham_gia(u, P1)

    A = {"chu": [{"labelId": "dong", "x": 10, "y": 10, "w": 100, "h": 20, "attributes": {"noi_dung": "CAM DO XE", "mau": "do"}}],
         "moc": [{"labelId": "goc", "x": 10, "y": 10}, {"labelId": "goc", "x": 190, "y": 140}],
         "vach": [{"labelId": "le", "points": [[0, 75], [200, 75]]}],
         "vung": [{"labelId": "nen", "rle": {"size": [150, 200], "counts": [0, 15000, 15000]}}],
         "so_dong": {"value": 1}, "tieu_de": {"text": "Cam do xe"}, "mo_ta": {"text": "bien bao mau do"}}
    B = {"chu": [{"labelId": "dong", "x": 12, "y": 11, "w": 98, "h": 20, "attributes": {"noi_dung": "cam do xe", "mau": "do"}}],
         "moc": [{"labelId": "goc", "x": 188, "y": 141}, {"labelId": "goc", "x": 12, "y": 9}],
         "vach": [{"labelId": "le", "points": [[200, 77], [100, 76], [0, 77]]}],
         "vung": [{"labelId": "nen", "rle": {"size": [150, 200], "counts": [0, 15150, 14850]}}],
         "so_dong": {"value": 1}, "tieu_de": {"text": "cam do xe"}, "mo_ta": {"text": "mot tam bien cam"}}

    def sua(goc, **doi):
        moi = copy.deepcopy(goc)
        moi.update(doi)
        return moi

    t1 = nhan_task(l1, P1, 30)
    kiem("cau hoi anh co fileUrl + metadata", t1 and t1["modality"] == "image" and t1["metadata"]["width"] == 200 and t1.get("fileUrl"), t1)
    aid = t1["assignmentId"]
    tu_choi(l1, aid, sua(A, moc=[{"labelId": "goc", "x": 250, "y": 10}]), "diem moc ngoai anh")
    tu_choi(l1, aid, sua(A, vach=[{"labelId": "le", "points": [[0, 75]]}]), "duong gap khuc mot dinh")
    tu_choi(l1, aid, sua(A, vach=[{"labelId": "le", "points": [[5, 5], [5, 5]]}]), "duong gap khuc do dai 0")
    tu_choi(l1, aid, sua(A, vung=[{"labelId": "nen", "rle": {"size": [10, 10], "counts": [0, 50, 50]}}]), "mat na sai kich thuoc anh")
    tu_choi(l1, aid, sua(A, vung=[{"labelId": "nen", "rle": {"size": [150, 200], "counts": [0, 15000, 14999]}}]), "mat na tong counts khac so pixel")
    tu_choi(l1, aid, sua(A, vung=[{"labelId": "nen", "rle": {"size": [150, 200], "counts": [100, 0, 29900]}}]), "mat na counts khong o dang chuan")
    tu_choi(l1, aid, sua(A, chu=[dict(A["chu"][0], attributes={"noi_dung": "x", "mau": "tim"})]), "thuoc tinh ngoai lua chon")
    tu_choi(l1, aid, sua(A, chu=[dict(A["chu"][0], attributes={"mau": "do"})]), "thieu thuoc tinh bat buoc")
    tu_choi(l1, aid, sua(A, chu=[dict(A["chu"][0], attributes={"noi_dung": "x", "la": True})]), "thuoc tinh khong khai trong tap nhan")
    tu_choi(l1, aid, sua(A, so_dong={"value": 2.5}), "so khong nguyen")
    tu_choi(l1, aid, sua(A, so_dong={"value": 99}), "so vuot max")
    tu_choi(l1, aid, sua(A, tieu_de={"text": "x" * 6000}), "chep chu vuot maxLength")
    kiem("sua lai nhan hop le (du 7 cong cu) → nop ca bai", nop_nhan(l1, aid, A).status_code == 200)
    t2 = nhan_task(l2, P1, 30)
    kiem("l2 nop nhan gan giong", nop_nhan(l2, t2["assignmentId"], B).status_code == 200)

    kq_bai = cham(biz, P1, 2)
    # Hai bai (< 3) → khong so da so, chi xet cau vang; du an khong co cau vang → ca hai dat.
    kiem("cham: 2 bai dat (it hon 3 bai → chi xet cau vang)", kq_bai is not None and all(b["passed"] for b in kq_bai.values()), kq_bai)
    kiem("quality luu metadata mau theo nhan (kich thuoc anh cho diem moc / duong)",
         sql("quality", f"select count(*) from labels where project_id = '{P1}' and sample_metadata ->> 'width' = '200'") == "2")
    kiem("nhan cua 2 bai dat vao ket qua", cho(lambda: len(ds_nhan(biz, P1, "accepted")) == 2, 30), ds_nhan(biz, P1))
    kq = ket_qua(biz, P1)
    s = kq["samples"][0]
    tl = s["tools"]
    kiem("khung: MOT khung da gop, toa do trung vi, 2 nguoi ve",
         tl["chu"]["method"] == "cluster" and len(tl["chu"]["final"]) == 1 and tl["chu"]["supports"] == [2]
         and [tl["chu"]["final"][0][k] for k in ("x", "y", "w", "h")] == [11, 10.5, 99, 20], tl["chu"])
    kiem("khung: thuoc tinh da gop (chu lay ban o giua, lua chon theo da so)",
         tl["chu"]["final"][0]["attributes"] == {"noi_dung": "CAM DO XE", "mau": "do"}, tl["chu"])
    kiem("diem moc: hai diem, trung vi toa do",
         [(p["x"], p["y"]) for p in tl["moc"]["final"]] == [(11, 9.5), (189, 140.5)], tl["moc"])
    kiem("duong gap khuc: ve nguoc chieu van ghep thanh mot duong", len(tl["vach"]["final"]) == 1 and tl["vach"]["supports"] == [2], tl["vach"])
    kiem("mat na: bo phieu tung pixel (2 nguoi → phan giao)",
         tl["vung"]["final"][0]["rle"] == {"size": [150, 200], "counts": [0, 15000, 15000]}, tl["vung"])
    kiem("so: trung vi", tl["so_dong"]["method"] == "median" and tl["so_dong"]["final"] == {"value": 1}, tl["so_dong"])
    kiem("chep chu: ban o giua", tl["tieu_de"]["method"] == "medoid" and tl["tieu_de"]["final"] == {"text": "Cam do xe"}, tl["tieu_de"])
    kiem("van ban tu do: KHONG gop (none), xem tung nhan", tl["mo_ta"]["method"] == "none" and len(s["labels"]) == 2, tl["mo_ta"])
    kiem("mau khong tranh chap; consensusStatus agreed; consensusFinal co khung da gop",
         not s["disputed"] and s["consensusStatus"] == "agreed" and s["consensusFinal"]["chu"][0]["x"] == 11, s)
    kiem("phan bo nhan dem theo lop cua cong cu dang danh sach, khong co cong cu so / van ban",
         kq["labelDistribution"].get("chu") == {"dong": 2} and "so_dong" not in kq["labelDistribution"] and "mo_ta" not in kq["labelDistribution"],
         kq["labelDistribution"])

    r = requests.get(G + f"/annotations/projects/{P1}/export?format=coco", headers=biz["h"])
    coco = r.json() if r.status_code == 200 else {}
    kiem("COCO: 1 anh, 2 annotation (khung da gop + mat na), 2 danh muc",
         r.status_code == 200 and len(coco["images"]) == 1 and len(coco["annotations"]) == 2 and len(coco["categories"]) == 2, r.text[:400])
    if coco:
        khung, mat_na = coco["annotations"]
        kiem("COCO khung: bbox trung vi + support + thuoc tinh",
             khung["bbox"] == [11, 10.5, 99, 20] and khung["support"] == 2 and khung["attributes"]["noi_dung"] == "CAM DO XE", khung)
        kiem("COCO mat na: segmentation RLE, area 15000, bbox nua trai anh",
             mat_na["segmentation"]["size"] == [150, 200] and mat_na["area"] == 15000 and mat_na["bbox"] == [0, 0, 100, 150], mat_na)
    r = requests.get(G + f"/annotations/projects/{P1}/export?format=csv", headers=biz["h"])
    dong = r.content.decode("utf-8-sig").splitlines()
    kiem("CSV: cot tung cong cu; so ghi gia tri gop, chep chu ghi ban da chon",
         r.status_code == 200 and dong[0].endswith("chu,moc,vach,vung,so_dong,tieu_de,mo_ta") and ",1,Cam do xe," in dong[1], dong[:2])
    r = requests.get(G + f"/annotations/projects/{P1}/export?format=yolo", headers=biz["h"])
    z = zipfile.ZipFile(io.BytesIO(r.content)) if r.status_code == 200 else None
    nhan_yolo = z.read(f"labels/{s['sampleId']}.txt").decode() if z else ""
    kiem("YOLO: mot dong cho khung da gop", r.status_code == 200 and len(nhan_yolo.strip().splitlines()) == 1, nhan_yolo)

    # =================================================================================
    print("\n[2] Diem moc lech nhau: 3 bai → da so → bai lech bi cham khong giong")
    P2, _, _ = du_an(biz, "Diem moc da so", "image", {"modality": "image", "tools": [{"name": "moc", "kind": "keypoint", "classes": ["tam"]}]},
                     files=[os.path.join(M, "anh.png")])
    publish_va_duyet(biz, P2)
    for u in (l1, l2, l3):
        tham_gia(u, P2)
    nop_nhan(l1, nhan_task(l1, P2, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 100, "y": 75}]}).raise_for_status()
    nop_nhan(l2, nhan_task(l2, P2, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 150, "y": 120}]}).raise_for_status()
    nop_nhan(l3, nhan_task(l3, P2, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 101, "y": 76}]}).raise_for_status()
    kq_bai = cham(biz, P2, 3)   # du 3 suat → tu dong thu bai
    kiem("da so: l1 va l3 giong, l2 (lech 67 px > 2% duong cheo) khac",
         kq_bai is not None and {ID[k]: v for k, v in giong(kq_bai).items()} == {"l1": True, "l2": False, "l3": True}, kq_bai)
    kiem("bai lech: 0% < nguong 60% → truot", kq_bai and kq_bai[l2["id"]]["passed"] is False and kq_bai[l2["id"]]["agreementPercent"] == 0, kq_bai)
    cuoi = json.loads(sql("quality", f"select final from sample_majorities where project_id = '{P2}' and status = 'agreed'") or "null")
    kiem("ket qua da so = trung vi cua hai diem khop", cuoi and gan(cuoi["moc"][0]["x"], 100.5) and gan(cuoi["moc"][0]["y"], 75.5), cuoi)
    kiem("bang chung uy tin theo bai: 2 giong, 1 khac",
         sql("quality", f"select string_agg(matched_count::text, ',' order by matched_count) from attempt_grades where project_id = '{P2}'") == "0,1,1")

    # =================================================================================
    print("\n[3] Van ban: doan + QUAN HE giua cac doan")
    CAU = "Lan lam viec tai FPT o Ha Noi"
    P3, _, _ = du_an(biz, "Quan he", "text", {"modality": "text", "tools": [
        {"name": "ner", "kind": "span", "classes": ["PER", "ORG", "LOC"]},
        {"name": "qh", "kind": "relation", "classes": ["lam_tai", "o_tai"], "spanTool": "ner", "required": False}]},
        rows=[{"text": CAU, "name": "cau1"}])
    r = requests.put(G + f"/projects/{P3}/label-schema", headers=biz["h"], json={"modality": "text", "tools": [
        {"name": "ner", "kind": "span", "classes": ["PER"]}, {"name": "qh", "kind": "relation", "classes": ["x"], "spanTool": "khong_co"}]})
    kiem("relation tro toi cong cu span khong ton tai → 400", r.status_code == 400 and "spanTool" in r.text, r.text)
    publish_va_duyet(biz, P3)
    for u in (l1, l2, l3):
        tham_gia(u, P3)
    DOAN = [{"labelId": "PER", "start": 0, "end": 3}, {"labelId": "ORG", "start": 17, "end": 20}, {"labelId": "LOC", "start": 23, "end": 29}]
    LAM_TAI = {"labelId": "lam_tai", "from": {"start": 0, "end": 3}, "to": {"start": 17, "end": 20}}
    O_TAI = {"labelId": "o_tai", "from": {"start": 17, "end": 20}, "to": {"start": 23, "end": 29}}
    t1 = nhan_task(l1, P3, 30)
    kiem("doan dung vi tri trong cau", (CAU[0:3], CAU[17:20], CAU[23:29]) == ("Lan", "FPT", "Ha Noi") and t1["content"]["text"] == CAU, t1)
    tu_choi(l1, t1["assignmentId"], {"ner": DOAN, "qh": [dict(LAM_TAI, to={"start": 17, "end": 21})]}, "quan he tro toi vi tri khong phai mot doan")
    tu_choi(l1, t1["assignmentId"], {"ner": DOAN, "qh": [dict(LAM_TAI, to={"start": 0, "end": 3})]}, "quan he noi mot doan voi chinh no")
    tu_choi(l1, t1["assignmentId"], {"ner": DOAN, "qh": [LAM_TAI, LAM_TAI]}, "quan he ghi trung")
    nop_nhan(l1, t1["assignmentId"], {"ner": DOAN, "qh": [LAM_TAI, O_TAI]}).raise_for_status()
    nop_nhan(l2, nhan_task(l2, P3, 30)["assignmentId"], {"ner": DOAN, "qh": [LAM_TAI]}).raise_for_status()
    nop_nhan(l3, nhan_task(l3, P3, 30)["assignmentId"], {"ner": list(reversed(DOAN)), "qh": [LAM_TAI]}).raise_for_status()
    kq_bai = cham(biz, P3, 3)
    kiem("ba bai: quan he 'lam_tai' qua ban; nguoi ghi thua quan he bi cham khac da so",
         kq_bai is not None and {ID[k]: v for k, v in giong(kq_bai).items()} == {"l1": False, "l2": True, "l3": True}, kq_bai)
    cho(lambda: len(ds_nhan(biz, P3, "accepted")) == 2, 30)
    tl = ket_qua(biz, P3)["samples"][0]["tools"]
    kiem("ket qua (nhan cua 2 bai dat): 3 doan + 1 quan he, cach gop majority",
         tl["ner"]["method"] == "majority" and len(tl["ner"]["final"]) == 3 and tl["qh"]["method"] == "majority"
         and tl["qh"]["final"] == [LAM_TAI] and tl["qh"]["supports"] == [2], tl)

    # =================================================================================
    print("\n[4] Du lieu DANH SACH: xep hang (Borda) + thang diem (trung vi)")
    P4, lo, mau = du_an(biz, "Xep hang", "list", {"modality": "list", "tools": [
        {"name": "xep", "kind": "ranking"},
        {"name": "diem", "kind": "number", "integer": True, "min": 1, "max": 5, "tolerance": 1}]},
        rows=[{"prompt": "Cau tra loi nao tot nhat?", "items": ["Tra loi A", "Tra loi B", "Tra loi C", "Tra loi D"], "name": "xh1"},
              {"items": ["chi mot muc"], "name": "hong1"}, {"items": ["a", 5], "name": "hong2"}, {"text": "khong co items", "name": "hong3"}])
    kiem("nap 1 mau danh sach, bo 3 dong sai (it muc / muc khong phai chuoi / thieu items)",
         lo["sampleCount"] == 1 and lo["skippedCount"] == 3 and "items" in (lo.get("errorSummary") or ""), lo)
    kiem("mau danh sach: content co items + prompt, metadata co itemCount",
         mau["xh1"]["content"]["items"][3] == "Tra loi D" and mau["xh1"]["content"]["prompt"] and mau["xh1"]["metadata"] == {"itemCount": 4}, mau)
    r = requests.put(G + f"/projects/{P4}/label-schema", headers=biz["h"], json={"modality": "list", "tools": [{"name": "v", "kind": "bbox", "classes": ["a"]}]})
    kiem("cong cu khung cho du lieu danh sach → 400", r.status_code == 400, r.text)
    publish_va_duyet(biz, P4)
    for u in (l1, l2, l3):
        tham_gia(u, P4)
    t1 = nhan_task(l1, P4, 30)
    kiem("cau danh sach: modality list, 4 muc, khong fileUrl",
         t1["modality"] == "list" and len(t1["content"]["items"]) == 4 and t1["metadata"]["itemCount"] == 4 and t1.get("fileUrl") is None, t1)
    tu_choi(l1, t1["assignmentId"], {"xep": {"order": [0, 1, 2]}, "diem": {"value": 4}}, "xep thieu muc")
    tu_choi(l1, t1["assignmentId"], {"xep": {"order": [0, 1, 2, 4]}, "diem": {"value": 4}}, "xep muc khong ton tai")
    tu_choi(l1, t1["assignmentId"], {"xep": {"order": [0, 1, 1, 2]}, "diem": {"value": 4}}, "xep lap muc")
    tu_choi(l1, t1["assignmentId"], {"xep": {"order": [0, 1, 2, 3]}, "diem": {"value": 6}}, "diem ngoai thang")
    nop_nhan(l1, t1["assignmentId"], {"xep": {"order": [0, 1, 2, 3]}, "diem": {"value": 4}}).raise_for_status()
    nop_nhan(l2, nhan_task(l2, P4, 30)["assignmentId"], {"xep": {"order": [1, 0, 2, 3]}, "diem": {"value": 5}}).raise_for_status()
    nop_nhan(l3, nhan_task(l3, P4, 30)["assignmentId"], {"xep": {"order": [0, 1, 3, 2]}, "diem": {"value": 4}}).raise_for_status()
    kq_bai = cham(biz, P4, 3)
    kiem("ba thu tu lech nhau mot cap van khop thu tu gop (Kendall 5/6); diem lech 1 trong sai so",
         kq_bai is not None and set(giong(kq_bai).values()) == {True}, kq_bai)
    cho(lambda: len(ds_nhan(biz, P4, "accepted")) == 3, 30)
    tl = ket_qua(biz, P4)["samples"][0]["tools"]
    kiem("xep hang gop bang diem Borda", tl["xep"]["method"] == "borda" and tl["xep"]["final"] == {"order": [0, 1, 2, 3]}
         and tl["xep"]["scores"] == {"0": 8, "1": 7, "2": 2, "3": 1}, tl["xep"])
    kiem("thang diem gop bang trung vi", tl["diem"]["final"] == {"value": 4} and tl["diem"]["agrees"] == [True, True, True], tl["diem"])
    r = requests.get(G + f"/annotations/projects/{P4}/export?format=csv", headers=biz["h"])
    kiem("CSV: thu tu xep hang va diem", r.status_code == 200 and r.content.decode("utf-8-sig").splitlines()[1].endswith(",0;1;2;3,4"), r.text[:300])

    # =================================================================================
    print("\n[5] Video: theo doi doi tuong (chuoi khung)")
    P5, lo, mau = du_an(biz, "Theo doi", "video", {"modality": "video", "tools": [{"name": "vat", "kind": "track", "classes": ["o_vuong"]}]},
                        files=[os.path.join(M, "clip.mp4")])
    kiem("nap video 320x240 ~12 giay", lo["sampleCount"] == 1 and mau["clip.mp4"]["metadata"]["width"] == 320, (lo, mau))
    publish_va_duyet(biz, P5)
    for u in (l1, l2):
        tham_gia(u, P5)

    def moc(t, x, y):
        return {"t": t, "x": x, "y": y, "w": 60, "h": 60}

    t1 = nhan_task(l1, P5, 30)
    aid = t1["assignmentId"]
    tu_choi(l1, aid, {"vat": [{"labelId": "o_vuong", "keyframes": [moc(0, 10, 20)]}]}, "chuoi khung mot moc")
    tu_choi(l1, aid, {"vat": [{"labelId": "o_vuong", "keyframes": [moc(3, 10, 20), moc(1, 50, 20)]}]}, "moc thoi gian lui")
    tu_choi(l1, aid, {"vat": [{"labelId": "o_vuong", "keyframes": [moc(0, 10, 20), moc(20, 50, 20)]}]}, "moc vuot do dai video")
    tu_choi(l1, aid, {"vat": [{"labelId": "o_vuong", "keyframes": [moc(0, 10, 20), moc(6, 300, 20)]}]}, "khung tran ra ngoai khung hinh")
    nop_nhan(l1, aid, {"vat": [{"labelId": "o_vuong", "keyframes": [moc(0, 10, 20), moc(6, 200, 40)]}]}).raise_for_status()
    nop_nhan(l2, nhan_task(l2, P5, 30)["assignmentId"],
             {"vat": [{"labelId": "o_vuong", "keyframes": [moc(0.2, 14, 22), moc(3, 107, 31), moc(6, 204, 42)]}]}).raise_for_status()
    kq_bai = cham(biz, P5, 2)
    kiem("hai bai deu dat → nhan vao ket qua", kq_bai is not None and all(b["passed"] for b in kq_bai.values())
         and cho(lambda: len(ds_nhan(biz, P5, "accepted")) == 2, 30), kq_bai)
    tl = ket_qua(biz, P5)["samples"][0]["tools"]
    kiem("ket qua: mot chuoi khung (chuoi 'o giua' cua cum)",
         tl["vat"]["method"] == "cluster" and len(tl["vat"]["final"]) == 1 and len(tl["vat"]["final"][0]["keyframes"]) == 2, tl["vat"])

    # =================================================================================
    print("\n[6] Bai test dau vao + cau vang kiem tra bang diem moc (cham theo do lech so voi duong cheo anh)")
    MAU_SAC = ["red", "green", "blue", "yellow", "cyan", "magenta", "white", "black", "gray", "orange", "purple", "pink", "brown", "navy"]
    files = [anh_mau(f"mau-{m}.png", m, 400, 300) for m in MAU_SAC]
    P6, lo, mau = du_an(biz, "Cau vang diem moc", "image", {"modality": "image", "tools": [{"name": "moc", "kind": "keypoint", "classes": ["tam"]}]},
                        files=files,
                        dieu_kien={"minLevel": None, "minReputation": None, "requireEntranceTest": True, "entranceQuestionCount": 1, "entrancePassPercent": 100})
    kiem("nap 14 anh 400x300", lo["sampleCount"] == 14, lo)
    TAM = {"moc": [{"labelId": "tam", "x": 200, "y": 150}]}
    GAN = {"moc": [{"labelId": "tam", "x": 206, "y": 150}]}    # lech 6 px = 1,2% duong cheo (500) → khop
    XA = {"moc": [{"labelId": "tam", "x": 230, "y": 150}]}     # lech 30 px = 6% → truot
    mau_test, mau_vang = mau["mau-red.png"], mau["mau-green.png"]
    r = requests.post(G + f"/projects/{P6}/gold-items", headers=biz["h"], json={"items": [
        {"sampleId": mau_test["id"], "purpose": "entranceTest", "expectedPayload": TAM},
        {"sampleId": mau_vang["id"], "purpose": "qualityCheck", "expectedPayload": TAM}]})
    kiem("them cau vang diem moc (bai test + kiem tra)", r.status_code == 200, r.text)
    publish_va_duyet(biz, P6)

    def lam_test(u, tra_loi):
        at = requests.post(G + f"/projects/{P6}/entrance-test/attempts", headers=u["h"]).json()
        return requests.post(G + f"/projects/{P6}/entrance-test/attempts/{at['attemptId']}/submit", headers=u["h"],
                             json={"answers": [{"sampleId": q["sampleId"], "payload": tra_loi} for q in at["questions"]]})

    r = lam_test(l1, GAN)
    kiem("bai test: diem lech 6 px → dau (project-svc cham theo duong cheo anh)", r.status_code == 200 and r.json()["passed"] and r.json()["joinedProject"], r.text)
    r = lam_test(l2, XA)
    kiem("bai test: diem lech 30 px → truot", r.status_code == 200 and not r.json()["passed"], r.text)
    r = lam_test(l2, TAM)
    kiem("lam lai dung → dau", r.status_code == 200 and r.json()["passed"], r.text)

    def lam_het_p6(u, tra_loi_vang):
        """Lam ca bai; cau vang (mau_vang, nam lan trong de) tra loi theo tra_loi_vang. → (so cau that, co cau vang)."""
        b = bat_dau(u, P6, 30)
        for q in b["questions"]:
            la_vang = q["sampleId"] == mau_vang["id"]
            luu(u, b["attemptId"], q["sampleId"], tra_loi_vang if la_vang else (TAM if u is l1 else GAN)).raise_for_status()
        nop_bai(u, b["attemptId"]).raise_for_status()
        ids = [q["sampleId"] for q in b["questions"]]
        return len([x for x in ids if x != mau_vang["id"]]), mau_vang["id"] in ids

    n1, vang1 = lam_het_p6(l1, GAN)
    n2, vang2 = lam_het_p6(l2, XA)
    kiem("moi bai: 12 cau that + cau vang kiem tra nam lan trong de (cau vang bai test KHONG vao bai)",
         (n1, vang1, n2, vang2) == (12, True, 12, True), (n1, vang1, n2, vang2))
    kq_bai = cham(biz, P6, 2)
    kiem("task-svc cham cau vang diem moc luc nop: l1 (lech 6 px) dung, l2 (lech 30 px) sai",
         kq_bai is not None and (kq_bai[l1["id"]]["goldCorrect"], kq_bai[l2["id"]]["goldCorrect"]) == (1, 0), kq_bai)
    kiem("sai cau vang → bai l2 truot, l1 dat", kq_bai and kq_bai[l1["id"]]["passed"] and not kq_bai[l2["id"]]["passed"], kq_bai)

    # =================================================================================
    print("\n[7] Setting: nguong ghep va F1 toi thieu doi luc dang chay")
    kiem("catalog co cac nguong moi", all(requests.get(G + f"/admin/settings/{k}", headers=admin).status_code == 200 for k in (
        "labeling.threshold.keypoint", "labeling.threshold.polyline", "labeling.threshold.mask", "labeling.threshold.track",
        "labeling.threshold.relation", "labeling.threshold.ranking", "quality.consensus_min_f1")))
    # Ha nguong diem moc xuong 0,7 (lech toi 30% duong cheo van la mot diem): hai diem cach 67 px cua [2] gio se khop.
    dat_va_cho("labeling.threshold.keypoint", 0.7, ["quality", "annotation"])
    P7, _, _ = du_an(biz, "Nguong diem moc", "image", {"modality": "image", "tools": [{"name": "moc", "kind": "keypoint", "classes": ["tam"]}]},
                     files=[os.path.join(M, "anh.png")])
    publish_va_duyet(biz, P7)
    for u in (l1, l2, l3):
        tham_gia(u, P7)
    nop_nhan(l1, nhan_task(l1, P7, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 100, "y": 75}]}).raise_for_status()
    nop_nhan(l2, nhan_task(l2, P7, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 150, "y": 120}]}).raise_for_status()
    nop_nhan(l3, nhan_task(l3, P7, 30)["assignmentId"], {"moc": [{"labelId": "tam", "x": 125, "y": 98}]}).raise_for_status()
    kq_bai = cham(biz, P7, 3)
    kiem("nguong 0,7: ba diem cach toi 67 px duoc ghep → ca ba bai giong da so",
         kq_bai is not None and set(giong(kq_bai).values()) == {True} and da_so(P7) == "agreed", (kq_bai, da_so(P7)))
finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e cong cu: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
sys.exit(1 if KQ["fail"] else 0)
