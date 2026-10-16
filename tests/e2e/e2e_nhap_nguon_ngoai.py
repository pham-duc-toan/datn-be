# E2E NHAP DU LIEU TU NGUON NGOAI (FB-11): bucket S3 (dung MinIO cua stack lam "S3 cua doanh nghiep"),
# danh sach URL, Google Drive (kiem thong so); chan SSRF (metadata cloud, service noi bo); bi mat S3 khong
# o lai trong database sau khi nhap.
# Chay: python e2e_nhap_nguon_ngoai.py   (stack demo: E2E_PROFILE=demo)
import os, struct, subprocess, sys, time, zlib
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import TIEN_TO_CONTAINER, _doc_env  # noqa: E402

MINIO = f"{TIEN_TO_CONTAINER}minio"
MAT_KHAU_MINIO = _doc_env("MINIO_PASSWORD") or "dev_minio_pw"
BUCKET = f"e2e-nguon-{TAG}"


def png(r, g, b, w=8, h=8):
    """PNG mau don sac, moi mau mot noi dung khac nhau (khong bi coi la trung)."""
    def khoi(loai, du):
        return struct.pack(">I", len(du)) + loai + du + struct.pack(">I", zlib.crc32(loai + du) & 0xFFFFFFFF)
    hang = b"".join(b"\x00" + bytes([r, g, b]) * w for _ in range(h))
    return b"\x89PNG\r\n\x1a\n" + khoi(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + khoi(b"IDAT", zlib.compress(hang)) + khoi(b"IEND", b"")


def mc(*args, du=None):
    return subprocess.run(["docker", "exec", "-i", MINIO, "/usr/bin/mc", *args], input=du, capture_output=True)


def tao_du_an_anh(chu, ten):
    pid = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e] {ten} {TAG}", "description": "x", "modality": "image", "visibility": "public"}).json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json={"modality": "image", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]}]}).raise_for_status()
    return pid


def cho_lo(chu, pid, lo_id, giay=90):
    def xong():
        for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json():
            if d["id"] == lo_id and d["status"] in ("ready", "failed"):
                return d
        return None
    return cho(xong, giay, 1)


try:
    print("\n[0] Chuan bi 'S3 cua doanh nghiep': bucket rieng trong MinIO, 3 anh + 1 file khong phai anh")
    mc("alias", "set", "local", "http://localhost:9000", "datn", MAT_KHAU_MINIO)
    mc("mb", "-p", f"local/{BUCKET}")
    for i, mau in enumerate([(255, 0, 0), (0, 255, 0), (0, 0, 255)]):
        mc("pipe", f"local/{BUCKET}/anh/{i}.png", du=png(*mau))
    mc("pipe", f"local/{BUCKET}/anh/ghi-chu.txt", du=b"khong phai anh")
    mc("pipe", f"local/{BUCKET}/url/4.png", du=png(9, 9, 9))
    mc("anonymous", "set", "download", f"local/{BUCKET}/url")
    kiem("bucket nguon co 5 doi tuong", len(mc("ls", "-r", f"local/{BUCKET}").stdout.splitlines()) == 5)

    biz = dang_ky(["business"], "nhap")
    P = tao_du_an_anh(biz, "Nhap S3")

    print("\n[1] Nhap tu S3")
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "tu s3", "source": "s3", "s3": {
        "endpoint": "http://minio:9000", "bucket": BUCKET, "prefix": "anh/", "accessKey": "datn", "secretKey": MAT_KHAU_MINIO}})
    kiem("tao lo nhap S3 → 202 pending", r.status_code == 202 and r.json()["status"] == "pending", r.text)
    lo = cho_lo(biz, P, r.json()["id"])
    kiem("lo S3 xong: 3 anh, bo qua 1 (file .txt khong phai anh)", lo and lo["status"] == "ready" and lo["sampleCount"] == 3 and lo["skippedCount"] == 1, lo)
    spec = sql("project", f"select import_spec::text from datasets where id = '{r.json()['id']}'")
    kiem("nhap xong: database KHONG con bi mat S3 (khong secretCipher, khong mat khau)",
         "secretCipher" not in spec and MAT_KHAU_MINIO not in spec and "accessKey" in spec, spec)
    mau = requests.get(G + f"/projects/{P}/samples?pageSize=50", headers=biz["h"]).json()["items"]
    kiem("mau mang ten goc + kich thuoc anh", sorted(m["originalName"] for m in mau) == ["0.png", "1.png", "2.png"] and all(m["metadata"]["width"] == 8 for m in mau),
         [(m["originalName"], m["metadata"]) for m in mau])

    print("\n[2] Nhap tu danh sach URL + chan SSRF")
    urls = [f"http://minio:9000/{BUCKET}/url/4.png",
            "http://169.254.169.254/latest/meta-data/",
            "http://identity:8101/internal/session-revocations",
            f"http://minio:9000/{BUCKET}/url/khong-co.png"]
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "tu url", "source": "urls", "urls": urls})
    lo = cho_lo(biz, P, r.json()["id"])
    kiem("lo URL: 1 anh, bo qua 3", lo and lo["sampleCount"] == 1 and lo["skippedCount"] == 3, lo)
    loi = (lo or {}).get("errorSummary") or ""
    kiem("metadata cloud 169.254.169.254 bi chan (dia chi noi bo)", "169.254.169.254" in loi and "noi bo" in loi, loi)
    kiem("service noi bo identity:8101 bi chan", "identity" in loi, loi)
    kiem("file khong ton tai → HTTP 404 ghi vao loi", "404" in loi, loi)

    print("\n[3] Kiem thong so")
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "x", "source": "urls", "urls": ["ftp://a.com/x.png"]})
    kiem("URL ftp → 400 url_khong_hop_le", r.status_code == 400 and "url_khong_hop_le" in r.text, r.text)
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "x", "source": "gdrive", "googleDrive": {"links": ["https://drive.google.com/drive/folders/1FolderIdXyz123"]}})
    kiem("thu muc Drive khi chua cau hinh khoa API → 400", r.status_code == 400 and "chua_cau_hinh_google_drive" in r.text, r.text)
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "x", "source": "gdrive", "googleDrive": {"links": ["https://example.com/a"]}})
    kiem("link khong phai Drive → 400", r.status_code == 400 and "link_drive_khong_hop_le" in r.text, r.text)
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "x", "source": "s3", "s3": {"bucket": BUCKET}})
    kiem("S3 thieu khoa → 400", r.status_code == 400 and "thieu_thong_so_s3" in r.text, r.text)
    T, _ = tao_du_an(biz, "Van ban", {"t1": "mot"}, 1, 1, 1000)
    r = requests.post(G + f"/projects/{T}/datasets/import", headers=biz["h"], json={"name": "x", "source": "urls", "urls": ["https://a.com/x.png"]})
    kiem("du an van ban → 400 nhap_ngoai_chi_cho_file", r.status_code == 400 and "nhap_ngoai_chi_cho_file" in r.text, r.text)
    lab = dang_ky(["labeler"], "nhap-lab")
    kiem("labeler khong nhap duoc vao du an nguoi khac", requests.post(G + f"/projects/{P}/datasets/import", headers=lab["h"],
         json={"name": "x", "source": "urls", "urls": ["https://a.com/x.png"]}).status_code in (403, 404))

    print("\n[4] S3 sai khoa")
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "sai", "source": "s3", "s3": {
        "endpoint": "http://minio:9000", "bucket": BUCKET, "prefix": "anh/", "accessKey": "datn", "secretKey": "sai-mat-khau"}})
    lo = cho_lo(biz, P, r.json()["id"])
    kiem("sai khoa → lo failed, loi 'S3 tu choi'", lo and lo["status"] == "failed" and "S3 tu choi" in (lo.get("errorSummary") or ""), lo)
    r = requests.post(G + f"/projects/{P}/datasets/import", headers=biz["h"], json={"name": "noi bo", "source": "s3", "s3": {
        "endpoint": "http://db-project:5432", "bucket": "x", "accessKey": "a", "secretKey": "b"}})
    lo = cho_lo(biz, P, r.json()["id"])
    kiem("endpoint S3 la service noi bo (db-project) → bi chan", lo and lo["status"] == "failed" and "noi bo" in (lo.get("errorSummary") or ""), lo)

    print("\n[5] Xoa file goc cua du an da dong — chi khi doanh nghiep dong y (muc 3.14)")
    def so_file(pid):
        return len([l for l in mc("ls", "-r", f"local/datasets/{pid}/").stdout.splitlines() if l.strip()])
    kiem("du an P co file trong kho", so_file(P) >= 4, so_file(P))
    Q = tao_du_an_anh(biz, "Giu file")
    rq = requests.post(G + f"/projects/{Q}/datasets/import", headers=biz["h"], json={"name": "q", "source": "urls", "urls": [f"http://minio:9000/{BUCKET}/url/4.png"]})
    cho_lo(biz, Q, rq.json()["id"])
    r = requests.put(G + f"/projects/{P}/retention", headers=biz["h"], json={"deleteFilesAfterClose": True})
    kiem("doanh nghiep dong y xoa file sau khi dong", r.status_code == 200 and r.json()["deleteFilesAfterClose"] is True, r.text)
    kiem("labeler khong doi duoc", requests.put(G + f"/projects/{P}/retention", headers=lab["h"], json={"deleteFilesAfterClose": True}).status_code in (403, 404))
    for pid in (P, Q):
        requests.post(G + f"/projects/{pid}/cancel", headers=biz["h"], json={"reason": "e2e"}).raise_for_status()
        sql("project", f"update projects set closed_at = now() - interval '200 days' where id = '{pid}'")
    dat_va_cho("project.housekeeping_interval", 10, ["project"])
    ok = cho(lambda: so_file(P) == 0, 40, 2)
    kiem("qua han giu → file cua P bi xoa", ok, so_file(P))
    kiem("P ghi filesPurgedAt", requests.get(G + f"/projects/{P}", headers=biz["h"]).json().get("filesPurgedAt") is not None)
    kiem("Q (khong dong y) VAN giu file", so_file(Q) >= 1, so_file(Q))
    kiem("mau trong database van con (lich su)", len(requests.get(G + f"/projects/{P}/samples?pageSize=50", headers=biz["h"]).json()["items"]) == 4)
finally:
    mc("rb", "--force", f"local/{BUCKET}")
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
