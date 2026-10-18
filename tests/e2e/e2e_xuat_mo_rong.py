# E2E DINH DANG NHAN + XUAT MO RONG:
#   khung xoay (bbox rotatable), chuoi khung an / hien lai (track visible=false);
#   xuat yolo-obb, yolo-seg, voc (co da giac / goc xoay), yolo (hop bao), coco-keypoints, conll, mot;
#   bo nho dem ket qua gop (X-Results-Cache).
# Chay: python e2e_xuat_mo_rong.py   (stack demo: E2E_PROFILE=demo)
import io, json, os, sys, zipfile
import xml.etree.ElementTree as ET
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402


def lam(u, pid, payload):
    """Lam ca bai (moi cau cung payload) va nop. Tra response nop."""
    b = bat_dau(u, pid, 20)
    assert isinstance(b, dict), "khong bat dau duoc bai"
    for q in b["questions"]:
        luu(u, b["attemptId"], q["sampleId"], payload).raise_for_status()
    return nop_bai(u, b["attemptId"])


def duyet_het(chu, pid, so):
    """Du an lam trung tam: khong duyet tung nhan — dong thu bai (neu chua), doi cham, nhan bai dat vao ket qua."""
    if requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "running":
        ket_thuc_som(chu, pid)
    ok = cho(lambda: len(ds_nhan(chu, pid, "accepted")) >= so, 60)
    assert ok, f"chua du {so} nhan vao ket qua: {[(a['status']) for a in ds_nhan(chu, pid)]}"


def xuat(chu, pid, dd):
    return requests.get(G + f"/annotations/projects/{pid}/export?format={dd}", headers=chu["h"])


try:
    biz = dang_ky(["business"], "xuat")
    h.nap(biz, 3000000)
    L = [dang_ky(["labeler"], f"xuat-l{i}") for i in range(3)]

    # =================================================================================
    print("\n[1] Du an anh: khung xoay + da giac + diem moc")
    anh = h.anh_mau("xuat-anh.png", "teal", 200, 150)
    tap_nhan = {"modality": "image", "tools": [
        {"name": "khung", "kind": "bbox", "classes": ["bien"], "rotatable": True},
        {"name": "khung_thang", "kind": "bbox", "classes": ["bien"], "required": False},
        {"name": "vung", "kind": "polygon", "classes": ["mat_duong"]},
        {"name": "diem", "kind": "keypoint", "classes": ["mui", "mat"]},
    ]}
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e] sai {TAG}", "description": "x", "modality": "image", "visibility": "public"})
    r = requests.put(G + f"/projects/{r.json()['id']}/label-schema", headers=biz["h"], json={"modality": "text", "tools": [
        {"name": "x", "kind": "span", "classes": ["a"], "rotatable": True}]})
    kiem("rotatable cho cong cu khac bbox → 400", r.status_code == 400, r.text)

    P, _, _ = h.du_an(biz, "Anh xoay", "image", tap_nhan, files=[anh], red=3, tran=3)
    publish_va_duyet(biz, P)
    for u in L:
        tham_gia(u, P)

    def anh_nhan(goc, dx):
        return {"khung": [{"labelId": "bien", "x": 60 + dx, "y": 50, "w": 80, "h": 30, "angle": goc}],
                "vung": [{"labelId": "mat_duong", "points": [[10 + dx, 100], [90 + dx, 100], [90 + dx, 140], [10 + dx, 140]]}],
                "diem": [{"labelId": "mui", "x": 100 + dx, "y": 60}, {"labelId": "mat", "x": 90 + dx, "y": 40}]}

    b0 = bat_dau(L[0], P, 20)
    s0 = b0["questions"][0]["sampleId"]
    luu(L[0], b0["attemptId"], s0, {**anh_nhan(15, 0), "khung_thang": [{"labelId": "bien", "x": 10, "y": 10, "w": 20, "h": 20, "angle": 10}]}).raise_for_status()
    r = nop_bai(L[0], b0["attemptId"])
    kiem("goc xoay tren cong cu KHONG rotatable → nop bai 400 (danh sach cau sai)", r.status_code == 400 and "bai_chua_hop_le" in r.text and "rotatable" in r.text, r.text)
    luu(L[0], b0["attemptId"], s0, {**anh_nhan(45, 0), "khung": [{"labelId": "bien", "x": 150, "y": 50, "w": 80, "h": 30, "angle": 45}]}).raise_for_status()
    r = nop_bai(L[0], b0["attemptId"])
    kiem("khung xoay co dinh ra ngoai anh → nop bai 400", r.status_code == 400 and "ngoai anh" in r.text, r.text)
    luu(L[0], b0["attemptId"], s0, anh_nhan(15, 0)).raise_for_status()
    r = nop_bai(L[0], b0["attemptId"])
    kiem("sua lai khung xoay hop le → nop duoc", r.status_code == 200, r.text)
    kiem("nguoi 2 nop", lam(L[1], P, anh_nhan(16, 1)).status_code == 200)
    kiem("nguoi 3 nop", lam(L[2], P, anh_nhan(14, -1)).status_code == 200)
    duyet_het(biz, P, 3)

    kq = requests.get(G + f"/annotations/projects/{P}/results", headers=biz["h"])
    cache1 = kq.headers.get("X-Results-Cache", "")
    final = kq.json()["samples"][0]["tools"]["khung"]["final"]
    kiem("gop khung xoay: goc = trung vi 15", final and final[0].get("angle") == 15, final)
    kq2 = requests.get(G + f"/annotations/projects/{P}/results", headers=biz["h"])
    kiem("lan 1 tinh moi (misses=1), lan 2 dung lai (hits=1, misses=0)",
         cache1 == "hits=0; misses=1" and kq2.headers.get("X-Results-Cache") == "hits=1; misses=0", (cache1, kq2.headers.get("X-Results-Cache")))
    kiem("ket qua lan 2 giong het lan 1", kq2.json()["samples"] == kq.json()["samples"])
    dat_va_cho("labeling.threshold.polygon", 0.6, ["annotation"])
    kiem("doi nguong ghep → dau van tay lech → tinh lai (misses=1)",
         requests.get(G + f"/annotations/projects/{P}/results", headers=biz["h"]).headers.get("X-Results-Cache") == "hits=0; misses=1")

    r = xuat(biz, P, "yolo-obb")
    z = zipfile.ZipFile(io.BytesIO(r.content))
    dong = [l for n in z.namelist() if n.startswith("labels/") for l in z.read(n).decode().splitlines() if l.strip()]
    kiem("yolo-obb: 1 khung, 8 toa do chuan hoa", r.status_code == 200 and len(dong) == 1 and len(dong[0].split()) == 9 and all(0 <= float(x) <= 1 for x in dong[0].split()[1:]), dong)
    r = xuat(biz, P, "yolo-seg")
    z = zipfile.ZipFile(io.BytesIO(r.content))
    dong = [l for n in z.namelist() if n.startswith("labels/") for l in z.read(n).decode().splitlines() if l.strip()]
    kiem("yolo-seg: 1 da giac 4 dinh (1 + 8 so)", r.status_code == 200 and len(dong) == 1 and len(dong[0].split()) == 9, dong)
    r = xuat(biz, P, "yolo")
    z = zipfile.ZipFile(io.BytesIO(r.content))
    dong = [l for n in z.namelist() if n.startswith("labels/") for l in z.read(n).decode().splitlines() if l.strip()]
    w = float(dong[0].split()[3]) * 200 if dong else 0
    kiem("yolo (truc): khung xoay ghi hop bao (rong > 80px)", r.status_code == 200 and len(dong) == 1 and w > 80.5, dong)
    r = xuat(biz, P, "voc")
    z = zipfile.ZipFile(io.BytesIO(r.content))
    x = ET.fromstring(z.read([n for n in z.namelist() if n.startswith("Annotations/")][0]))
    ten = sorted(o.find("name").text for o in x.findall("object"))
    kiem("voc: co khung (kem angle) va da giac (kem polygon)", ten == ["khung/bien", "vung/mat_duong"]
         and any(o.find("angle") is not None for o in x.findall("object")) and any(o.find("polygon") is not None for o in x.findall("object")), ET.tostring(x)[:400])
    r = xuat(biz, P, "coco-keypoints")
    c = r.json()
    kiem("coco-keypoints: category co 2 diem, 1 annotation du 2 diem", r.status_code == 200 and c["categories"][0]["keypoints"] == ["mui", "mat"]
         and len(c["annotations"]) == 1 and c["annotations"][0]["num_keypoints"] == 2, c.get("annotations"))
    kiem("conll cho du an anh → 400", xuat(biz, P, "conll").status_code == 400)

    # =================================================================================
    print("\n[2] Du an van ban: CoNLL")
    T, _, _ = h.du_an(biz, "NER", "text", {"modality": "text", "tools": [{"name": "ner", "kind": "span", "classes": ["PER", "LOC"]}]},
                      rows=[{"text": "Ong Nguyen Van A song o Ha Noi", "name": "c1"}], red=2, tran=2)
    publish_va_duyet(biz, T)
    for u in L[:2]:
        tham_gia(u, T)
        lam(u, T, {"ner": [{"labelId": "PER", "start": 4, "end": 16}, {"labelId": "LOC", "start": 24, "end": 30}]}).raise_for_status()
    duyet_het(biz, T, 2)
    r = xuat(biz, T, "conll")
    dong = [l for l in r.text.splitlines() if l and not l.startswith("#")]
    kiem("conll: BIO dung", dong == ["Ong\tO", "Nguyen\tB-PER", "Van\tI-PER", "A\tI-PER", "song\tO", "o\tO", "Ha\tB-LOC", "Noi\tI-LOC"], dong)

    # =================================================================================
    print("\n[3] Du an video: track an / hien lai + MOT")
    clip = os.path.join(h.M, "clip.mp4")
    V, _, mauV = h.du_an(biz, "Track", "video", {"modality": "video", "tools": [{"name": "doi_tuong", "kind": "track", "classes": ["nguoi"]}]},
                         files=[clip], red=2, tran=2)
    publish_va_duyet(biz, V)
    chuoi = [{"labelId": "nguoi", "keyframes": [
        {"t": 0, "x": 10, "y": 10, "w": 40, "h": 60}, {"t": 2, "x": 50, "y": 10, "w": 40, "h": 60, "visible": False},
        {"t": 4, "x": 90, "y": 10, "w": 40, "h": 60}, {"t": 6, "x": 130, "y": 10, "w": 40, "h": 60}]}]
    for u in L[:2]:
        tham_gia(u, V)
        r = lam(u, V, {"doi_tuong": chuoi})
        kiem("nop chuoi co doan an", r.status_code == 200, r.text)
    duyet_het(biz, V, 2)
    r = xuat(biz, V, "mot")
    z = zipfile.ZipFile(io.BytesIO(r.content))
    gt = [n for n in z.namelist() if n.endswith("gt/gt.txt")]
    khung = [int(l.split(",")[0]) for n in gt for l in z.read(n).decode().splitlines() if l.strip()]
    fps = mauV and list(mauV.values())[0]["metadata"].get("fps")
    kiem("mot: co dong o 0–2s va 4–6s, KHONG co dong trong doan an (2–4s)",
         r.status_code == 200 and khung and min(khung) == 1 and any(f <= 20 for f in khung) and not any(22 <= f <= 40 for f in khung) and any(f >= 42 for f in khung),
         (fps, khung[:5], khung[-5:]))
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
