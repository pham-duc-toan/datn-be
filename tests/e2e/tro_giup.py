"""Helper dung chung cua e2e_tong_the va ep_race_quality (dang ky, tao du an, dat setting...)."""
# E2E TONG THE — tu tao tai khoan + du an moi moi lan chay (khong phu thuoc du lieu seed).
# Phu: xac thuc, nap tien, vong doi du an + saga ky quy, bai test dau vao, nhan/nop/bo qua task,
# dong thuan + redundancy thich ung (majority / posterior / voi), duyet + khieu nai, vong tien
# (treo → giai phong → rut, tu duyet / admin duyet / tu choi), cua so ngau nhien khi lay task,
# quan ly du an, phan quyen (RBAC + BOLA), setting, va kiem tra tong (doi soat, DLQ, outbox).
import json, os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
sys.path.insert(0, os.path.join(GOC_REPO, "services", "quality"))
from app import redundancy as rd  # noqa: E402  — CHINH ham quality-svc dung, de tinh dap an mong doi


PW = "Matkhau@123"
KQ = {"pass": 0, "fail": 0, "loi": []}
GOC = {}
TAG = uuid.uuid4().hex[:6]


def kiem(ten, dk, ct=""):
    KQ["pass" if dk else "fail"] += 1
    if not dk:
        KQ["loi"].append(ten)
    print(("  PASS " if dk else "  FAIL ") + ten + ("" if dk else " | " + str(ct)[:600]), flush=True)


def cho(f, giay=30, buoc=0.5):
    het = time.time() + giay
    while time.time() < het:
        v = f()
        if v:
            return v
        time.sleep(buoc)
    return None


def sql(db, q):
    return subprocess.run(["docker", "exec", sql_container(db), "psql", "-U", f"{db}_user", "-d", f"{db}_db", "-tAc", q],
                          capture_output=True, text=True).stdout.strip()


def login_raw(email, pw=PW):
    return requests.post(G + "/auth/login", json={"email": email, "password": pw})


def hdr(tok):
    return {"Authorization": "Bearer " + tok}


def dang_ky(vai, ten):
    email = f"e2e-{TAG}-{ten}@crowd.local"
    r = requests.post(G + "/auth/register", json={"email": email, "password": PW, "displayName": ten, "roles": vai})
    assert r.status_code in (200, 201), r.text
    t = login_raw(email).json()
    h = hdr(t["accessToken"])
    uid = requests.get(G + "/me", headers=h).json()["userId"]
    return {"h": h, "id": uid, "email": email, "refresh": t["refreshToken"]}


admin = hdr(login_raw("admin@crowd.local").json()["accessToken"])


def dat(key, value):
    if key not in GOC:
        GOC[key] = requests.get(G + f"/admin/settings/{key}", headers=admin).json()["value"]
    return requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": value, "reason": "e2e tong the"})


def dat_va_cho(key, value, dbs):
    r = dat(key, value)
    assert r.status_code == 200, (key, r.text)
    ver = r.json()["version"]
    for db in dbs:
        ok = cho(lambda: sql(db, f"select version from settings_replica where key='{key}'") == str(ver), 20)
        assert ok, f"{db} chua nhan {key} v{ver}"
    time.sleep(0.5)


def vi(u):
    return requests.get(G + "/ledger/me/balance", headers=u["h"]).json()


def tao_du_an(chu, ten, mau, red, tran, gia, gold_pct=0, dieu_kien=None, vang=None, ngan_sach=None, classes=("a", "b")):
    """mau: {ten_mau: text}. vang: [(ten_mau, nhan_dung, purpose)]. Tra (pid, {ten_mau: sample_id})."""
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-tong] {ten} {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json={"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": list(classes)}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    ns = ngan_sach if ngan_sach is not None else len(mau) * tran * gia * 2
    requests.put(G + f"/projects/{pid}/pricing", headers=chu["h"], json={"unitPriceVnd": gia, "redundancy": red, "maxRedundancy": tran,
                                                                         "budgetVnd": ns, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    requests.put(G + f"/projects/{pid}/quality-control", headers=chu["h"], json={"goldCheckPercent": gold_pct}).raise_for_status()
    if dieu_kien:
        requests.put(G + f"/projects/{pid}/eligibility", headers=chu["h"], json=dieu_kien).raise_for_status()
    requests.post(G + f"/projects/{pid}/datasets/manifest", headers=chu["h"],
                  json={"name": "lo", "rows": [{"text": v, "name": k} for k, v in mau.items()]}).raise_for_status()
    cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json() if d["status"] == "ready"], 40)
    ids = {m["originalName"]: m["id"] for m in requests.get(G + f"/projects/{pid}/samples?pageSize=200", headers=chu["h"]).json()["items"]}
    if vang:
        r = requests.post(G + f"/projects/{pid}/gold-items", headers=chu["h"], json={"items": [
            {"sampleId": ids[t], "purpose": p, "expectedPayload": {"nhan": {"labelIds": [d]}}} for t, d, p in vang]})
        assert r.status_code == 200, r.text
    return pid, ids


def publish_va_duyet(chu, pid):
    requests.post(G + f"/projects/{pid}/publish", headers=chu["h"]).raise_for_status()
    ok = cho(lambda: requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "pendingApproval", 30)
    assert ok, requests.get(G + f"/projects/{pid}", headers=chu["h"]).text
    requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()


def tham_gia(u, pid):
    r = cho(lambda: requests.post(G + f"/projects/{pid}/join", headers=u["h"]) if True else None, 1)
    return r


def nhan_task(u, pid, giay=15):
    """200 → dict; 204 → None. 403 ngay sau join / duyet: thu lai trong `giay`."""
    het = time.time() + giay
    while True:
        r = requests.post(G + f"/tasks/projects/{pid}/next", headers=u["h"])
        if r.status_code == 200:
            return r.json()
        if r.status_code == 204:
            return None
        if time.time() > het:
            raise AssertionError(f"next {r.status_code} {r.text}")
        time.sleep(0.5)


def nop(u, aid, lop):
    return requests.post(G + f"/tasks/assignments/{aid}/submit", headers=u["h"], json={"payload": {"nhan": {"labelIds": [lop]}}})


def lam_het(u, pid, tra_loi, toi_da=200):
    """Lam het task cua du an; tra_loi(text) → lop. Tra so task da nop."""
    so = 0
    for _ in range(toi_da):
        t = nhan_task(u, pid)
        if t is None:
            return so
        nop(u, t["assignmentId"], tra_loi(t["content"]["text"])).raise_for_status()
        so += 1
    return so


def ds(j):
    return j if isinstance(j, list) else j.get("items", [])


def ds_nhan(chu, pid):
    return requests.get(G + f"/annotations/projects/{pid}?pageSize=100", headers=chu["h"]).json()["items"]


