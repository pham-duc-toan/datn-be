# E2E setting dong: admin doi setting qua API → service ap dung cho thao tac moi.
import json, os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
SO_KHOA = len(json.load(open(os.path.join(GOC_REPO, "shared", "settings", "catalog.json"), encoding="utf-8")))

PW = "Matkhau@123"
KQ = {"pass": 0, "fail": 0}
GOC = {}   # gia tri ban dau de tra lai cuoi bai


def kiem(ten, dk, ct=""):
    KQ["pass" if dk else "fail"] += 1
    print(("  PASS " if dk else "  FAIL ") + ten + ("" if dk else " | " + str(ct)[:600]))


def login(e, pw=PW):
    r = requests.post(G + "/auth/login", json={"email": e, "password": pw}); r.raise_for_status()
    return {"Authorization": "Bearer " + r.json()["accessToken"]}


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


admin = login("admin@crowd.local"); biz = login("doanhnghiep1@crowd.local")
lab = {"lab1": login("labeler1@crowd.local"), "lab2": login("labeler2@crowd.local")}
ID = {"lab1": "1f5e19a7-9f58-589b-8c74-45334ff35d05", "lab2": "0398f264-d694-5176-9614-844201175b74"}


def dat(key, value, ly_do="e2e"):
    if key not in GOC:
        GOC[key] = requests.get(G + f"/admin/settings/{key}", headers=admin).json()["value"]
    r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": value, "reason": ly_do})
    return r


def dat_va_cho(key, value, db_kiem=("project",)):
    r = dat(key, value)
    assert r.status_code == 200, r.text
    ver = r.json()["version"]
    # Cho moi service lien quan nhan setting.changed (ban sao trong DB).
    for db in db_kiem:
        ok = cho(lambda: sql(db, f"select version from settings_replica where key='{key}'") == str(ver), 15)
        assert ok, f"{db} chua nhan {key} v{ver}"
    time.sleep(0.3)
    return r.json()


try:
    print("\n[1] API admin/settings")
    r = requests.get(G + "/admin/settings", headers=admin)
    ds = r.json() if r.status_code == 200 else []
    kiem(f"admin liet ke setting ({SO_KHOA} khoa = catalog.json)", r.status_code == 200 and len(ds) == SO_KHOA, (r.status_code, len(ds)))
    kiem("moi setting co kieu, mac dinh, gioi han, nhom", all({"key", "type", "defaultValue", "value", "group", "effect"} <= set(x) for x in ds), ds[:1])
    r = requests.get(G + "/admin/settings", headers=biz)
    kiem("doanh nghiep xem setting → 403", r.status_code == 403, r.status_code)
    r = requests.get(G + "/admin/settings", headers={})
    kiem("khong token → 401", r.status_code == 401, r.status_code)
    r = dat("fee.platform_percent", 95)
    kiem("phi 95% vuot tran 90 → 400", r.status_code == 400, r.text)
    r = dat("fee.platform_percent", "30")
    kiem("phi kieu chuoi → 400", r.status_code == 400, r.text)
    r = dat("fee.platform_percent", 25.5)
    kiem("phi so le → 400", r.status_code == 400, r.text)
    r = dat("project.auto_approve", 1)
    kiem("bool nhan so → 400", r.status_code == 400, r.text)
    r = dat("payment.deposit_min_vnd", 600000000)
    kiem("nap toi thieu > nap toi da → bi chan", r.status_code in (400, 409), r.text)
    r = requests.put(G + "/admin/settings/khong.ton.tai", headers=admin, json={"value": 1})
    kiem("khoa khong ton tai → 404", r.status_code == 404, r.status_code)

    print("\n[2] Phi nen tang: 30% → 20% (du an moi tinh theo phi moi)")
    v = dat_va_cho("fee.platform_percent", 20)
    h = requests.get(G + "/admin/settings/fee.platform_percent/history", headers=admin).json()
    kiem("lich su ghi gia tri cu → moi + nguoi doi + ly do", len(h) >= 1 and h[0]["oldValue"] == 30 and h[0]["newValue"] == 20 and h[0]["reason"] == "e2e", h[:1])
    r = requests.post(G + "/projects", headers=biz, json={"name": "[e2e-setting] Phi", "description": "x", "modality": "text", "visibility": "public"})
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=biz, json={"modality": "text", "tools": [
        {"name": "cam_xuc", "kind": "classification", "classes": ["tich_cuc", "tieu_cuc"]}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=biz, json={"markdown": "x", "examples": []}).raise_for_status()
    requests.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 1000, "redundancy": 2, "budgetVnd": 100000, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    requests.put(G + f"/projects/{pid}/quality-control", headers=biz, json={"goldCheckPercent": 0}).raise_for_status()
    rows = [{"text": "Hang dep, giao nhanh", "name": "a"}, {"text": "Chat luong te", "name": "b"}]
    requests.post(G + f"/projects/{pid}/datasets/manifest", headers=biz, json={"name": "lo", "rows": rows}).raise_for_status()
    cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=biz).json() if d["status"] == "ready"], 30)
    rd = requests.get(G + f"/projects/{pid}/readiness", headers=biz).json()
    kiem("readiness: phi 20%, 200d/nhan, ky quy 2 x 2 x 1200 = 4.800",
         rd["platformFeePercent"] == 20 and rd["platformFeePerLabelVnd"] == 200 and rd["estimatedCostVnd"] == 4800, rd)

    print("\n[3] Tu duyet du an (project.auto_approve) + tu duyet nhan khop dong thuan (annotation.auto_approve_agreed)")
    dat_va_cho("project.auto_approve", True)
    dat_va_cho("annotation.auto_approve_agreed", True, ("annotation",))
    requests.post(G + f"/projects/{pid}/publish", headers=biz).raise_for_status()
    st = cho(lambda: requests.get(G + f"/projects/{pid}", headers=biz).json()["status"] == "running", 25)
    kiem("ky quy xong → du an CHAY ngay, khong qua hang doi admin", st, requests.get(G + f"/projects/{pid}", headers=biz).json()["status"])
    kiem("phi chot vao du an la 20%", sql("project", f"select platform_fee_percent from projects where id='{pid}'") == "20",
         sql("project", f"select platform_fee_percent from projects where id='{pid}'"))
    for k in lab:
        cho(lambda: requests.post(G + f"/projects/{pid}/join", headers=lab[k]).status_code in (200, 201, 204, 409), 10)
    # Ca hai chon giong nhau → dong thuan agreed → he thong tu duyet.
    for k in lab:
        for _ in range(10):
            r = requests.post(G + f"/tasks/projects/{pid}/next", headers=lab[k])
            if r.status_code == 403:
                time.sleep(1); continue
            if r.status_code == 204:
                break
            t = r.json()
            nhan = "tich_cuc" if "dep" in t["content"]["text"] else "tieu_cuc"
            requests.post(G + f"/tasks/assignments/{t['assignmentId']}/submit", headers=lab[k],
                          json={"payload": {"cam_xuc": {"labelIds": [nhan]}}}).raise_for_status()

    def da_duyet_het():
        ds = requests.get(G + f"/annotations/projects/{pid}?pageSize=50", headers=biz).json()["items"]
        return ds if len(ds) == 4 and all(a["status"] == "approved" for a in ds) else None

    ds = cho(da_duyet_het, 30)
    kiem("4 nhan khop dong thuan duoc HE THONG duyet (khong ai bam)", ds is not None,
         [(a["status"], a["consensusAgrees"]) for a in requests.get(G + f"/annotations/projects/{pid}?pageSize=50", headers=biz).json()["items"]])
    kiem("nhan tu duyet khong co reviewer, lich su 'auto_approved'",
         sql("annotation", f"select count(*) from annotations where project_id='{pid}' and reviewer_id is null and status='Approved'") == "4", "")
    het = cho(lambda: sql("ledger", f"select count(*) from holds where project_id='{pid}'") == "4", 20)
    kiem("ledger treo thu lao 4 luot (annotation.approved da phat)", het, sql("ledger", f"select count(*) from holds where project_id='{pid}'"))

    print("\n[4] Gioi han dong: ten du an, so file upload, mat khau")
    dat_va_cho("project.name_max_length", 10)
    r = requests.post(G + "/projects", headers=biz, json={"name": "Ten du an dai hon muoi", "description": "x", "modality": "text", "visibility": "public"})
    kiem("ten 22 ky tu > 10 → 400", r.status_code == 400, r.text)
    r = requests.post(G + "/projects", headers=biz, json={"name": "Ngan", "description": "x", "modality": "image", "visibility": "public"})
    kiem("ten 4 ky tu → tao duoc", r.status_code in (200, 201), r.text)
    pid2 = r.json()["id"]
    dat_va_cho("upload.max_files", 2)
    r = requests.post(G + f"/projects/{pid2}/uploads", headers=biz, json={"files": [{"name": f"{i}.png", "sizeBytes": 100} for i in range(3)]})
    kiem("xin 3 link upload khi tran 2 → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/projects/{pid2}/uploads", headers=biz, json={"files": [{"name": f"{i}.png", "sizeBytes": 100} for i in range(2)]})
    kiem("xin 2 link → 200", r.status_code == 200, r.text)
    dat_va_cho("identity.password_min_length", 12, ("identity",))
    email = f"e2e-setting-{uuid.uuid4().hex[:8]}@crowd.local"
    r = requests.post(G + "/auth/register", json={"email": email, "password": "Abc@12345", "displayName": "x", "roles": ["labeler"]})
    kiem("mat khau 9 ky tu khi toi thieu 12 → 400", r.status_code == 400 and "12" in r.text, r.text)
    r = requests.post(G + "/auth/register", json={"email": email, "password": "Abc@12345678", "displayName": "x", "roles": ["labeler"]})
    kiem("mat khau 12 ky tu → dang ky duoc", r.status_code in (200, 201), r.text)

    print("\n[5] Gateway (khong DB) doi tran upload ZIP theo setting")
    dat("dataset.zip_max_bytes", 1048576)
    time.sleep(2)
    r = requests.post(G + f"/projects/{pid2}/datasets", headers=biz, files={"file": ("x.zip", os.urandom(2 * 1048576))}, data={"name": "lon"})
    kiem("ZIP 2 MB khi tran 1 MB → 413", r.status_code == 413, (r.status_code, r.text[:200]))

    print("\n[6] Rut tien: tu duyet theo nguong, con lai cho admin duyet / tu choi")
    if requests.get(G + "/ledger/me/balance", headers=lab["lab2"]).json()["availableVnd"] < 70000:
        print("  SKIP rut tien: vi lab2 seed khong du 70.000 (da phu bang tai khoan moi trong e2e_tong_the muc 8)")
    else:
        dat_va_cho("ledger.withdraw_min_vnd", 10000, ("ledger",))
        dat_va_cho("ledger.withdraw_auto_approve_max_vnd", 20000, ("ledger",))
        so_du = lambda: requests.get(G + "/ledger/me/balance", headers=lab["lab2"]).json()["availableVnd"]
        truoc = so_du()

        def rut(so):
            return requests.post(G + "/ledger/withdrawals", headers=dict(lab["lab2"], **{"Idempotency-Key": uuid.uuid4().hex}),
                                 json={"amountVnd": so, "bankAccount": "VCB 0123456789"})

        r = rut(15000)
        kiem("rut 15.000 (≤ nguong 20.000) → tu duyet, gui cong", r.status_code == 200 and r.json()["state"] in ("requested", "completed"), r.text)
        w1 = r.json()["id"]
        xong = cho(lambda: sql("ledger", f"select state from withdrawals where id='{w1}'") == "Completed", 30)
        kiem("lenh tu duyet chuyen khoan xong", xong, sql("ledger", f"select state from withdrawals where id='{w1}'"))
        r = rut(30000)
        kiem("rut 30.000 (> nguong) → cho admin duyet", r.status_code == 200 and r.json()["state"] == "pendingApproval", r.text)
        w2 = r.json()["id"]
        kiem("tien da giu ngay (kha dung giam 45.000)", so_du() == truoc - 45000, (truoc, so_du()))
        time.sleep(3)
        kiem("lenh cho duyet CHUA gui payment-svc", sql("payment", f"select count(*) from payouts where id='{w2}'") == "0", "")
        hang = requests.get(G + "/ledger/admin/withdrawals", headers=admin).json()
        kiem("admin thay lenh trong hang doi", any(x["id"] == w2 for x in hang["items"]), hang)
        kiem("labeler khong vao duoc hang doi admin", requests.get(G + "/ledger/admin/withdrawals", headers=lab["lab2"]).status_code == 403)
        r = requests.post(G + f"/ledger/admin/withdrawals/{w2}/approve", headers=admin)
        kiem("admin duyet → requested", r.status_code == 200 and r.json()["state"] == "requested", r.text)
        r = requests.post(G + f"/ledger/admin/withdrawals/{w2}/approve", headers=admin)
        kiem("duyet lan 2 → 409", r.status_code == 409, r.text)
        xong = cho(lambda: sql("ledger", f"select state from withdrawals where id='{w2}'") == "Completed", 30)
        kiem("lenh admin duyet chuyen khoan xong", xong, sql("ledger", f"select state from withdrawals where id='{w2}'"))
        r = rut(25000); w3 = r.json()["id"]
        r = requests.post(G + f"/ledger/admin/withdrawals/{w3}/reject", headers=admin, json={"reason": ""})
        kiem("tu choi khong ly do → 400", r.status_code == 400, r.text)
        r = requests.post(G + f"/ledger/admin/withdrawals/{w3}/reject", headers=admin, json={"reason": "Sai so tai khoan"})
        kiem("admin tu choi → rejected + ly do", r.status_code == 200 and r.json()["state"] == "rejected" and r.json()["failureReason"] == "Sai so tai khoan", r.text)
        kiem("tien ve lai vi (kha dung = truoc - 45.000)", so_du() == truoc - 45000, (truoc, so_du()))
        rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
        kiem("doi soat so cai van lanh", all(not v for k, v in rec.items() if isinstance(v, list)), rec)

    print("\n[7] Nap tien chuyen khoan thu cong")
    vi_dn = lambda: requests.get(G + "/ledger/me/balance", headers=biz).json()["businessAvailableVnd"]
    truoc_dn = vi_dn()
    k = uuid.uuid4().hex
    r = requests.post(G + "/payments/deposits/manual", headers=dict(biz, **{"Idempotency-Key": k}), json={"amountVnd": 500000})
    d = r.json()
    kiem("tao lenh chuyen khoan: co ma noi dung + thong tin tai khoan, khong co link cong",
         r.status_code == 200 and d["transferCode"].startswith("CROWD") and d["bankInfo"] and d["checkoutUrl"] is None and d["provider"] == "manual_transfer", d)
    r2 = requests.post(G + "/payments/deposits/manual", headers=dict(biz, **{"Idempotency-Key": k}), json={"amountVnd": 500000})
    kiem("cung Idempotency-Key → cung lenh", r2.json()["intentId"] == d["intentId"], r2.text)
    r = requests.post(G + f"/payments/deposits/{d['intentId']}/transferred", headers=biz)
    kiem("bao da chuyen → cho admin (nguong tu duyet mac dinh 0)", r.status_code == 200 and r.json()["status"] == "awaitingApproval", r.text)
    hang = requests.get(G + "/payments/admin/deposits", headers=admin).json()
    kiem("admin thay lenh cho doi chieu", any(x["intentId"] == d["intentId"] for x in hang["items"]), hang)
    r = requests.post(G + f"/payments/admin/deposits/{d['intentId']}/approve", headers=admin, json={"bankTxnRef": "FT" + k[:10].upper()})
    kiem("admin duyet → succeeded", r.status_code == 200 and r.json()["status"] == "succeeded", r.text)
    vao = cho(lambda: vi_dn() == truoc_dn + 500000, 20)
    kiem("ledger cong 500.000 vao vi doanh nghiep", vao, (truoc_dn, vi_dn()))
    # Dung lai ma sao ke cho lenh khac → chan.
    d2 = requests.post(G + "/payments/deposits/manual", headers=dict(biz, **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 100000}).json()
    r = requests.post(G + f"/payments/admin/deposits/{d2['intentId']}/approve", headers=admin, json={"bankTxnRef": "FT" + k[:10].upper()})
    kiem("mot ma sao ke dung cho hai lenh → 409", r.status_code == 409, r.text)
    r = requests.post(G + f"/payments/admin/deposits/{d2['intentId']}/reject", headers=admin, json={"reason": "Khong thay tien ve"})
    kiem("admin tu choi lenh → rejected", r.status_code == 200 and r.json()["status"] == "rejected", r.text)
    dat_va_cho("payment.manual_transfer_auto_approve_max_vnd", 1000000, ("payment",))
    d3 = requests.post(G + "/payments/deposits/manual", headers=dict(biz, **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 300000}).json()
    r = requests.post(G + f"/payments/deposits/{d3['intentId']}/transferred", headers=biz)
    kiem("duoi nguong tu duyet 1.000.000 → succeeded ngay", r.status_code == 200 and r.json()["status"] == "succeeded", r.text)
    vao = cho(lambda: vi_dn() == truoc_dn + 800000, 20)
    kiem("ledger cong them 300.000", vao, (truoc_dn, vi_dn()))
    dat_va_cho("payment.manual_transfer_enabled", False, ("payment",))
    r = requests.post(G + "/payments/deposits/manual", headers=dict(biz, **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 100000})
    kiem("tat chuyen khoan thu cong → 409", r.status_code == 409, r.text)
    dat_va_cho("payment.deposit_min_vnd", 200000, ("payment",))
    r = requests.post(G + "/payments/deposits", headers=dict(biz, **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 100000})
    kiem("nap qua cong 100.000 < toi thieu moi 200.000 → 400", r.status_code == 400, r.text)

    print("\n[8] quality-svc (Python) nhan setting")
    dat("quality.reputation_gold_weight", 0.8)
    ok = cho(lambda: sql("quality", "select value from settings_replica where key='quality.reputation_gold_weight'") == "0.8", 15)
    kiem("quality-svc ghi ban sao 0.8", ok, sql("quality", "select value, version from settings_replica where key='quality.reputation_gold_weight'"))

finally:
    print("\n[9] Tra setting ve gia tri ban dau")
    for key, v in GOC.items():
        r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e: tra lai"})
        if r.status_code != 200:
            print("  KHONG tra lai duoc", key, r.text)
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
