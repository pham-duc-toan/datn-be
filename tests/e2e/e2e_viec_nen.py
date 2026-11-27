# E2E VIEC CHAY NEN CUA DOANH NGHIEP (kien truc 3.41): ZIP anh upload thang len kho + phieu giai nen nen; tao file ket qua
# qua outbox + RabbitMQ (export.requested). Khong viec nang nao con nam trong request.
# Tu tao tai khoan + du an moi. Chay: python e2e_viec_nen.py   (stack demo: E2E_PROFILE=demo)
import io, os, sys, zipfile
import requests
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import dat_cau_hoi_cong_cu  # noqa: E402
from tro_giup import *  # noqa: E402,F401,F403


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def anh(mau, kieu="PNG"):
    """Anh 40x30 mot mau — moi mau mot noi dung (sha256) khac nhau."""
    buf = io.BytesIO()
    Image.new("RGB", (40, 30), mau).save(buf, kieu)
    return buf.getvalue()


def zip_tu(cac_file):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for ten, noi_dung in cac_file:
            z.writestr(ten, noi_dung)
    return buf.getvalue()


def du_an_anh(chu, ten):
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-nen] {ten} {TAG}", "description": "e2e", "modality": "image", "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    requests.patch(G + f"/projects/{pid}", headers=chu["h"], json={"labelSchema": {"modality": "image", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]}]}}).raise_for_status()
    dat_cau_hoi_cong_cu(chu["h"], pid)
    return pid


def trong_kho(khoa, bucket="datasets"):
    """Object con trong MinIO khong — MinIO luu moi object thanh mot thu muc /data/<bucket>/<key>/ (co xl.meta)."""
    r = subprocess.run(["docker", "exec", TIEN_TO_CONTAINER + "minio", "sh", "-c", f"test -e /data/{bucket}/{khoa}/xl.meta && echo co"],
                       capture_output=True, text=True)
    return r.stdout.strip() == "co"


def xin_link_zip(pid, h, ten="anh.zip", kich_thuoc=1024):
    return requests.post(G + f"/projects/{pid}/uploads", headers=h, json={"files": [{"name": ten, "sizeBytes": kich_thuoc}]})


try:
    biz = dang_ky(["business"], "nen-biz")
    biz2 = dang_ky(["business"], "nen-biz2")

    # =================================================================================
    print("\n[1] ZIP anh: upload thang len kho, dang ky phieu → 202, giai nen nen")
    P = du_an_anh(biz, "zip")
    do, xanh, vang = anh((200, 0, 0)), anh((0, 200, 0)), anh((200, 200, 0), "JPEG")
    noi_dung = zip_tu([("a/do.png", do), ("xanh.png", xanh), ("vang.jpg", vang), ("trung-do.png", do),
                       ("ghi-chu.txt", b"khong phai anh"), ("__MACOSX/._do.png", b"rac")])

    r = xin_link_zip(P, biz["h"], kich_thuoc=len(noi_dung))
    kiem("xin link ZIP → khoa trong thu muc zip cua du an, link PUT thang len kho",
         r.status_code == 200 and r.json()[0]["key"].startswith(f"{P}/zip/") and r.json()[0]["key"].endswith(".zip"), r.text[:300])
    slot = r.json()[0]
    requests.put(slot["uploadUrl"], data=noi_dung).raise_for_status()

    r = requests.post(G + f"/projects/{P}/samples/zip", headers=biz["h"], json={"key": slot["key"], "name": "anh-mau.zip"})
    kiem("dang ky ZIP → 202 ngay, phieu queued (chua giai nen trong request)",
         r.status_code == 202 and r.json()["status"] in ("queued", "processing") and r.json()["name"] == "anh-mau.zip", (r.status_code, r.text[:300]))
    phieu = r.json()
    r2 = requests.post(G + f"/projects/{P}/samples/zip", headers=biz["h"], json={"key": slot["key"], "name": "anh-mau.zip"})
    kiem("dang ky lai cung key → tra lai CUNG phieu (khong giai nen hai lan)", r2.status_code == 202 and r2.json()["id"] == phieu["id"], r2.text[:200])

    xong = cho(lambda: next((z for z in trang_thai_nap(P, biz["h"])["zipImports"] if z["id"] == phieu["id"] and z["status"] in ("done", "failed")), None), 90) or {}
    kiem("phieu giai nen xong: 3 anh hop le, bo qua 2 (file txt + anh trung noi dung)",
         xong.get("status") == "done" and xong.get("ready") == 3 and xong.get("skipped") == 2, xong)
    mau = requests.get(G + f"/projects/{P}/samples?pageSize=100", headers=biz["h"]).json()["items"]
    kiem("3 mau ready kem kich thuoc anh, ten theo duong dan trong ZIP",
         len(mau) == 3 and all(m["ingestState"] == "ready" and m["metadata"].get("width") == 40 and m["metadata"].get("height") == 30 for m in mau)
         and sorted(m["originalName"] for m in mau) == ["a/do.png", "vang.jpg", "xanh.png"], [(m["originalName"], m["ingestState"], m["metadata"]) for m in mau])
    kiem("file ZIP da xoa khoi kho sau khi giai nen; anh nam o khoa rieng cua tung mau",
         not trong_kho(slot["key"]) and all(trong_kho(f"{P}/{m['id']}.{'jpg' if m['originalName'].endswith('.jpg') else 'png'}") for m in mau),
         (trong_kho(slot["key"]), [m["id"] for m in mau]))

    # =================================================================================
    print("\n[2] ZIP hong / trung / sai du an")
    ma, z = nap_zip(P, biz["h"], zip_tu([("lai-do.png", do), ("lai-xanh.png", xanh)]), "trung.zip")
    kiem("ZIP toan anh trung noi dung → phieu failed kem ly do, khong tao mau", ma == 202 and z.get("status") == "failed" and "trùng" in (z.get("error") or ""), z)

    ma, z = nap_zip(P, biz["h"], os.urandom(4096), "hong.zip")
    kiem("file khong phai ZIP → phieu failed 'không phải ZIP'", ma == 202 and z.get("status") == "failed" and "ZIP" in (z.get("error") or ""), z)
    kiem("so mau giu nguyen 3 sau hai ZIP hong",
         len(requests.get(G + f"/projects/{P}/samples?pageSize=100", headers=biz["h"]).json()["items"]) == 3)

    P2 = du_an_anh(biz2, "zip-khac")
    r = requests.post(G + f"/projects/{P2}/samples/zip", headers=biz2["h"], json={"key": slot["key"], "name": "an-cap.zip"})
    kiem("du an khac dang ky key ZIP cua du an nay → 400 (khong dung duoc file du an khac)", r.status_code == 400, (r.status_code, r.text[:200]))
    r = requests.post(G + f"/projects/{P}/samples/zip", headers=biz2["h"], json={"key": slot["key"], "name": "x.zip"})
    kiem("nguoi khong phai chu du an → 403/404", r.status_code in (403, 404), r.status_code)
    r = requests.post(G + f"/projects/{P}/samples/zip", headers=biz["h"], json={"key": f"{P}/zip/chua-upload.zip", "name": "x.zip"})
    kiem("key chua co file trong kho → 400 khong_tim_thay_file", r.status_code == 400 and code(r) == "khong_tim_thay_file", r.text[:200])
    r = requests.post(G + f"/projects/{P}/samples/zip", headers=biz["h"], json={"key": f"{P}/raw/a.png", "name": "x.zip"})
    kiem("key khong phai thu muc zip → 400", r.status_code == 400, r.text[:200])

    st = trang_thai_nap(P, biz["h"])
    kiem("trang thai nap liet ke ca 3 phieu ZIP, moi nhat truoc", [z["status"] for z in st["zipImports"]] == ["failed", "failed", "done"], st["zipImports"])
    r = requests.delete(G + f"/projects/{P}/samples/failed", headers=biz["h"])
    st = trang_thai_nap(P, biz["h"])
    kiem("xoa mau loi → xoa luon phieu ZIP loi, giu phieu da xong", r.status_code == 200 and [z["status"] for z in st["zipImports"]] == ["done"], st["zipImports"])

    # =================================================================================
    print("\n[3] Tao file ket qua chay nen: dinh dang kiem ngay, file tao sau")
    r = requests.post(G + f"/annotations/projects/{P}/exports", headers=biz["h"], json={"format": "conll"})
    kiem("conll cho du an anh → 400 ngay luc yeu cau", r.status_code == 400 and code(r) == "dinh_dang_khong_hop_du_an", r.text[:200])
    r = requests.post(G + f"/annotations/projects/{P}/exports", headers=biz2["h"], json={"format": "json"})
    kiem("nguoi khong phai chu du an tao file → 404", r.status_code == 404, r.status_code)
    r = requests.post(G + f"/annotations/projects/{P}/exports", headers=biz["h"], json={"format": "CSV"})
    kiem("du an chua co ket qua van tao duoc file (rong) → 202, dinh dang chuan hoa chu thuong", r.status_code == 202 and r.json()["format"] == "csv", r.text[:200])
    job = r.json()
    r = requests.get(G + f"/annotations/projects/{P}/exports/{job['id']}/file", headers=biz["h"])
    kiem("tai file khi chua xong → 409 file_chua_san_sang (hoac da xong → 200)",
         r.status_code == 200 or (r.status_code == 409 and code(r) == "file_chua_san_sang"), (r.status_code, r.text[:200]))
    j = cho(lambda: (lambda x: x if x["status"] in ("ready", "failed") else None)(
        requests.get(G + f"/annotations/projects/{P}/exports/{job['id']}", headers=biz["h"]).json()), 60) or {}
    kiem("file CSV rong: ready, 0 mau", j.get("status") == "ready" and j.get("sampleCount") == 0, j)
    r = requests.get(G + j.get("downloadPath", "/x"), headers=biz["h"])
    kiem("tai file → 200, ten file + dung luong khop job",
         r.status_code == 200 and "ket-qua-" in r.headers.get("Content-Disposition", "") and int(r.headers.get("Content-Length", -1)) == j.get("sizeBytes"),
         (r.status_code, dict(r.headers)))
    khoa_file = sql("project", f"select storage_key from export_jobs where id = '{job['id']}'")
    kiem("file luu trong kho duoi thu muc exports cua du an", khoa_file.startswith(f"{P}/exports/") and trong_kho(khoa_file), khoa_file)
finally:
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("  -", x)
