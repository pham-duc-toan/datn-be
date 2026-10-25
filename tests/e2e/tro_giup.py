"""Helper dung chung cua cac bo E2E (dang ky, tao du an, lam bai, dat setting...).

Du an lam trung tam (kien truc 3.18): moi labeler lam MOT bai gom moi cau cua du an, nop ca bai; du an dong
thu bai (du suat / het han / ket thuc som) thi quality-svc cham mot lan, ledger tra thuong bai dat.
"""
import hashlib, json, os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
sys.path.insert(0, os.path.join(GOC_REPO, "services", "quality"))
from tai_khoan_e2e import mo_rong_gioi_han, xac_thuc_email  # noqa: E402


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


def dang_ky(vai, ten, xac_minh=True, xac_thuc=True):
    """Tao tai khoan moi. Mac dinh bam link xac thuc email (chua xac thuc thi chua rut tien / publish
    duoc); doanh nghiep: tu lam KYB + admin duyet. xac_minh / xac_thuc = False de test chinh cac luong do."""
    email = f"e2e-{TAG}-{ten}@crowd.local"
    r = requests.post(G + "/auth/register", json={"email": email, "username": "u" + hashlib.sha1((email).encode()).hexdigest()[:20], "password": PW, "displayName": ten, "roles": vai})
    assert r.status_code in (200, 201), r.text
    if xac_thuc:
        xac_thuc_email(email)
    t = login_raw(email).json()
    h = hdr(t["accessToken"])
    uid = requests.get(G + "/me", headers=h).json()["userId"]
    u = {"h": h, "id": uid, "email": email, "username": "u" + hashlib.sha1(email.encode()).hexdigest()[:20], "refresh": t["refreshToken"]}
    if "business" in vai and xac_minh:
        xac_minh_kyb(u)
    return u


PDF_MAU = b"%PDF-1.4" + bytes([10]) + b"% giay phep kinh doanh mau (E2E)" + bytes([10]) + b"%%EOF" + bytes([10])


def nop_kyb(u, ten_cong_ty="Cong ty E2E", mst="0312345678"):
    """Khai ho so + tai giay phep (PUT thang len kho) + nop. Tra response nop."""
    requests.put(G + "/kyb/me", headers=u["h"], json={"companyName": ten_cong_ty, "taxCode": mst, "address": "Ha Noi"}).raise_for_status()
    up = requests.post(G + "/kyb/me/license", headers=u["h"], json={"fileName": "giay-phep.pdf", "sizeBytes": len(PDF_MAU)})
    up.raise_for_status()
    requests.put(up.json()["uploadUrl"], data=PDF_MAU).raise_for_status()
    return requests.post(G + "/kyb/me/submit", headers=u["h"])


def lam_moi_token(u):
    """POST /auth/refresh: lay access token moi (mang claim email_verified / kyb_verified moi nhat) va
    cap nhat u tai cho. Frontend lam y het sau khi xac thuc email / duoc duyet KYB."""
    r = requests.post(G + "/auth/refresh", json={"refreshToken": u["refresh"]})
    assert r.status_code == 200, r.text
    u["h"].update(hdr(r.json()["accessToken"]))
    u["refresh"] = r.json()["refreshToken"]
    return u


def xac_minh_kyb(u):
    """KYB tron goi: nop → admin duyet → lam moi token (claim kyb_verified — project-svc doc claim khi publish)."""
    nop_kyb(u).raise_for_status()
    requests.post(G + f"/kyb/admin/{u['id']}/approve", headers=admin).raise_for_status()
    lam_moi_token(u)


admin = hdr(login_raw("admin@crowd.local").json()["accessToken"])
GOC.update(mo_rong_gioi_han(admin))  # cac bo goi tra GOC luc ket thuc → han muc ve nhu cu


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


# He so tran nguoi lam (10/2026) = 1 cho cac bo E2E: chung viet theo "so suat = so nguoi lam, du suat thi dong thu
# bai". Bo e2e_suat_thuong.py dat E2E_HE_SO_NGUOI_LAM=giu va tu dat he so de kiem cach tinh moi.
if os.environ.get("E2E_HE_SO_NGUOI_LAM") != "giu":
    _he_so = requests.get(G + "/admin/settings/project.participant_multiplier", headers=admin).json()["value"]
    GOC.setdefault("project.participant_multiplier", _he_so)
    if _he_so != 1:
        dat_va_cho("project.participant_multiplier", 1, ["task"])


def vi(u):
    return requests.get(G + "/ledger/me/balance", headers=u["h"]).json()


def tao_du_an(chu, ten, mau, red=None, tran=None, gia=None, gold_pct=0, dieu_kien=None, vang=None, ngan_sach=None, classes=("a", "b"),
              thuong=None, suat=None, nguong=60):
    """mau: {ten_mau: text}. vang: [(ten_mau, nhan_dung, purpose)]. Tra (pid, {ten_mau: sample_id}).

    Gia CA DU AN (3.18): thuong mac dinh = gia x so mau (toi thieu 10.000), so suat mac dinh = max(3, tran,
    red) — giu tham so cu (red, tran, gia, ngan_sach, gold_pct) de cac bo cu goi duoc; ngan_sach / gold_pct
    khong con nghia (ky quy = suat x (thuong + phi), cau vang qualityCheck chen vao moi bai)."""
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-tong] {ten} {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json={"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": list(classes)}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    dat_gia(chu, pid, thuong if thuong is not None else max(10000, (gia or 1000) * len(mau)),
            suat if suat is not None else max(3, tran or 0, red or 0), nguong)
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


def dat_gia(chu, pid, thuong, suat, nguong=60, deadline="2027-12-31T00:00:00Z"):
    r = requests.put(G + f"/projects/{pid}/pricing", headers=chu["h"],
                     json={"rewardVnd": thuong, "slots": suat, "passThresholdPercent": nguong, "deadline": deadline})
    assert r.status_code == 200, r.text
    return r


def cho_video_san_sang(headers, pid, giay=300):
    """Video vua nap: media-svc chuan hoa xong (video.prepared) project-svc moi cho publish."""
    ok = cho(lambda: requests.get(G + f"/projects/{pid}/readiness", headers=headers).json().get("videoPreparingCount", 0) == 0, giay)
    assert ok, "video chua chuan bi xong sau %ss" % giay


def publish_va_duyet(chu, pid):
    cho_video_san_sang(chu["h"], pid)
    requests.post(G + f"/projects/{pid}/publish", headers=chu["h"]).raise_for_status()
    ok = cho(lambda: requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "pendingApproval", 30)
    assert ok, requests.get(G + f"/projects/{pid}", headers=chu["h"]).text
    requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()
    # task-svc mo thu bai khi nhan project.published.
    cho(lambda: requests.get(G + f"/tasks/projects/{pid}/slots", headers=chu["h"]).status_code == 200, 30)


def tham_gia(u, pid):
    r = cho(lambda: requests.post(G + f"/projects/{pid}/join", headers=u["h"]) if True else None, 1)
    return r


def bat_dau(u, pid, giay=15):
    """POST attempt → dict bai lam (tao moi hoac bai dang lam). 403 ngay sau join / duyet: thu lai trong `giay`.
    Loi nghiep vu khac (het suat, da nop...) → tra Response de noi goi kiem."""
    het = time.time() + giay
    while True:
        r = requests.post(G + f"/tasks/projects/{pid}/attempt", headers=u["h"])
        if r.status_code in (200, 201):
            return r.json()
        if r.status_code != 403 or time.time() > het:
            return r
        time.sleep(0.5)


def luu(u, aid, sid, data):
    """data = dict nhan theo ten cong cu, hoac None (bo trong)."""
    return requests.put(G + f"/tasks/attempts/{aid}/answers/{sid}", headers=u["h"], json={"data": data})


def nop_bai(u, aid):
    return requests.post(G + f"/tasks/attempts/{aid}/submit", headers=u["h"])


def lam_bai(u, pid, tra_loi, nop=True):
    """Lam CA BAI: tra_loi(text) → lop | None (bo trong) | dict (nhan day du). Tra (bai, response nop | None)."""
    b = bat_dau(u, pid)
    assert isinstance(b, dict), f"bat dau bai {b.status_code} {b.text}"
    for q in b["questions"]:
        x = tra_loi((q.get("content") or {}).get("text"))
        if x is None:
            continue
        luu(u, b["attemptId"], q["sampleId"], x if isinstance(x, dict) else {"nhan": {"labelIds": [x]}}).raise_for_status()
    return b, (nop_bai(u, b["attemptId"]) if nop else None)


def lam_het(u, pid, tra_loi, toi_da=200):
    """Tuong thich bo cu: lam va nop ca bai. Tra so cau DA TRA LOI (ke ca cau vang)."""
    b, r = lam_bai(u, pid, tra_loi)
    r.raise_for_status()
    return r.json()["answeredCount"]


def bai_cua_toi(u, pid):
    for b in requests.get(G + "/tasks/me/attempts", headers=u["h"]).json():
        if b["projectId"] == pid:
            return b
    return None


def cho_cham(u, pid, giay=60):
    """Doi bai cua u o du an duoc cham (passed khac null). Tra AttemptSummary."""
    return cho(lambda: (bai_cua_toi(u, pid) or {}).get("passed") is not None and bai_cua_toi(u, pid), giay)


def ket_thuc_som(chu, pid):
    """Doanh nghiep ket thuc som → dong thu bai, cham. Doi du an sang grading."""
    r = requests.post(G + f"/projects/{pid}/finish", headers=chu["h"])
    assert r.status_code in (200, 202), r.text
    assert cho(lambda: requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] in ("grading", "completed"), 30)


# ---- Lop tuong thich cho cac bo viet theo kieu "tung task" (truoc 3.18) ----
# nhan_task tra CAU CHUA LAM ke tiep trong bai cua labeler (bat dau bai neu chua co); nop luu cau do.
# Het cau → nhan_task tu NOP CA BAI va tra None (nhu "het task"). "assignmentId" = "<attemptId>|<sampleId>".
_BAI = {}


def nhan_task(u, pid, giay=15):
    k = (u["email"], pid)
    st = _BAI.get(k)
    if st is None or st.get("nop"):
        b = bat_dau(u, pid, giay)
        if not isinstance(b, dict):
            if b.status_code == 409 or "da_nop_bai" in b.text or "het_suat" in b.text or "dong_thu_bai" in b.text:
                return None
            raise AssertionError(f"bat dau bai {b.status_code} {b.text}")
        st = {"bai": b, "xong": {q["sampleId"] for q in b["questions"] if q.get("answer")}, "nop": False}
        _BAI[k] = st
    b = st["bai"]
    for q in b["questions"]:
        if q["sampleId"] not in st["xong"]:
            return dict(q, assignmentId=f"{b['attemptId']}|{q['sampleId']}", attemptId=b["attemptId"], projectId=pid,
                        labelSchema=b["labelSchema"], rewardVnd=b["rewardVnd"], expiresAt=b["expiresAt"],
                        sensitiveContent=b.get("sensitiveContent", False))
    nop_bai(u, b["attemptId"]).raise_for_status()
    st["nop"] = True
    return None


def nop(u, aid, lop):
    """aid tu nhan_task; lop = ten lop (cong cu "nhan") hoac dict nhan day du."""
    attempt_id, sid = aid.split("|")
    r = luu(u, attempt_id, sid, lop if isinstance(lop, dict) else {"nhan": {"labelIds": [lop]}})
    for (email, _p), st in _BAI.items():
        if email == u["email"] and st["bai"]["attemptId"] == attempt_id:
            st["xong"].add(sid)
    return r


def nop_ca_bai(u, pid):
    """Nop bai dang lam do nhan_task / nop tao (khi khong lam het cau)."""
    st = _BAI.get((u["email"], pid))
    if st and not st["nop"]:
        r = nop_bai(u, st["bai"]["attemptId"])
        st["nop"] = r.status_code == 200
        return r
    return None


def ds(j):
    return j if isinstance(j, list) else j.get("items", [])


def ds_nhan(chu, pid, status=None):
    q = f"?pageSize=200{'&status=' + status if status else ''}"
    return requests.get(G + f"/annotations/projects/{pid}{q}", headers=chu["h"]).json()["items"]


