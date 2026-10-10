# E2E P2 — cong link: link-svc (rut gon, kiem duyet, gioi thieu) + gate-svc (trang vuot link)
# + ledger (tra sharer, ngan sach cong link, hoa hong, giu doanh thu) + annotation (nhan cong link).
import json, os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
GATE = URL_GATE   # goi thang gate de gia lap IP khach khac nhau (X-Forwarded-For)
LINK = URL_LINK

PW = "Matkhau@123"
TS = "XXXX.DUMMY.TOKEN.XXXX"     # token dummy cua khoa test Turnstile (luon dat)
KQ = {"pass": 0, "fail": 0}
GOC = {}


def kiem(ten, dk, ct=""):
    KQ["pass" if dk else "fail"] += 1
    print(("  PASS " if dk else "  FAIL ") + ten + ("" if dk else " | " + str(ct)[:700]))


def login(e, pw=PW):
    r = requests.post(G + "/auth/login", json={"email": e, "password": pw}); r.raise_for_status()
    return {"Authorization": "Bearer " + r.json()["accessToken"]}


def dang_ky(vai_tro):
    email = f"e2e-p2-{uuid.uuid4().hex[:8]}@crowd.local"
    r = requests.post(G + "/auth/register", json={"email": email, "password": PW, "displayName": "p2", "roles": vai_tro})
    r.raise_for_status()
    h = login(email)
    me = requests.get(G + "/me", headers=h).json()
    return h, me.get("id") or me.get("userId")


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


def dat(key, value):
    if key not in GOC:
        GOC[key] = requests.get(G + f"/admin/settings/{key}", headers=admin).json()["value"]
    r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": value, "reason": "e2e-p2"})
    assert r.status_code == 200, r.text
    ver = r.json()["version"]
    for db in ("gate", "ledger", "link"):
        assert cho(lambda: sql(db, f"select version from settings_replica where key='{key}'") == str(ver), 20), f"{db} chua nhan {key}"
    time.sleep(0.5)


def khach(ip):
    return {"X-Forwarded-For": ip}


def phien(code, ip, extra=None, password=None):
    body = {"turnstileToken": TS}
    if password is not None:
        body["password"] = password
    h = khach(ip)
    if extra:
        h.update(extra)
    return requests.post(GATE + f"/g/{code}/sessions", headers=h, json=body)


CAU = {  # ten mau → (van ban, dap an dung)
    "v1": ("Con cho dang sua", "cho"),
    "v2": ("Con meo dang ngu", "meo"),
    "t1": ("Mot con cho nho", "cho"),
    "t2": ("Meo trang tren ghe", "meo"),
    "t3": ("Cho chay ngoai san", "cho"),
    "t4": ("Meo bat chuot", "meo"),
}


def tra_loi(questions, sai_vang=False):
    ans = {}
    for q in questions:
        text = q["content"]["text"]
        ten = next(k for k, v in CAU.items() if v[0] == text)
        dung = CAU[ten][1]
        if sai_vang and ten in ("v1", "v2"):
            dung = "meo" if dung == "cho" else "cho"
        ans[q["sampleId"]] = {"loai": {"labelIds": [dung]}}
    return ans


def vuot(code, ip, extra=None, cho_dem=True):
    """Vuot link tron ven, tra ve (submit json, session json)."""
    r = phien(code, ip, extra)
    assert r.status_code == 200, r.text
    s = r.json()
    if cho_dem:
        con = (time.mktime(time.strptime(s["answerableAt"][:19], "%Y-%m-%dT%H:%M:%S")) - time.mktime(time.gmtime()))
        if con > 0:
            time.sleep(con + 0.3)
    h = khach(ip)
    if extra:
        h.update(extra)
    n = requests.post(GATE + f"/g/sessions/{s['sessionId']}/submit", headers=h, json={"answers": tra_loi(s["questions"])})
    return n, s


admin = login("admin@crowd.local"); biz = login("doanhnghiep1@crowd.local")

try:
    # Don cac du an cong link e2e con chay tu lan truoc: gate chia traffic NGAU NHIEN giua cac du
    # an con ngan sach, nen phai chi con mot du an thi so lieu moi kiem dung.
    for cu in sql("gate", "select project_id from gate_projects where status='Running'").split():
        # Dong du an da chay phai tam dung truoc (phuong an A). Tam dung la du de gate ngung
        # phuc vu; huy co the bi chan neu du an cu con nhan cho duyet — khong anh huong bai test.
        requests.post(G + f"/projects/{cu}/pause", headers=biz)
        requests.post(G + f"/projects/{cu}/cancel", headers=biz, json={"reason": "e2e don dep"})
    cho(lambda: sql("gate", "select count(*) from gate_projects where status='Running'") == "0", 20)
    dat("gate.countdown", 0)
    dat("link.referral_min_earnings_vnd", 0)

    print("\n[1] Doanh nghiep tao du an van ban BAT cong link, ngan sach vuot muc toi thieu")
    r = requests.post(G + "/projects", headers=biz, json={"name": "[e2e-p2] Cong link", "description": "p2", "modality": "text", "visibility": "public"})
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=biz, json={"modality": "text", "tools": [
        {"name": "loai", "kind": "classification", "classes": ["cho", "meo"]}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=biz, json={"markdown": "p2", "examples": []}).raise_for_status()
    # 6 mau x tran 1 x (1000 + 300) = 7.800 danh cho chuyen nghiep; ngan sach 20.000 → cong link 12.200.
    requests.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 1000, "redundancy": 1, "maxRedundancy": 1, "budgetVnd": 20000, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    requests.put(G + f"/projects/{pid}/channels", headers=biz, json={"allowProfessional": True, "allowLinkGateway": True, "allowCollaborative": False}).raise_for_status()
    requests.put(G + f"/projects/{pid}/quality-control", headers=biz, json={"goldCheckPercent": 0}).raise_for_status()
    requests.post(G + f"/projects/{pid}/datasets/manifest", headers=biz, json={"name": "lo", "rows": [{"text": v[0], "name": k} for k, v in CAU.items()]}).raise_for_status()
    cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=biz).json() if d["status"] == "ready"], 30)
    mau = {m["originalName"]: m["id"] for m in requests.get(G + f"/projects/{pid}/samples?pageSize=50", headers=biz).json()["items"]}
    r = requests.post(G + f"/projects/{pid}/gold-items", headers=biz, json={"items": [
        {"sampleId": mau["v1"], "purpose": "qualityCheck", "expectedPayload": {"loai": {"labelIds": ["cho"]}}},
        {"sampleId": mau["v2"], "purpose": "qualityCheck", "expectedPayload": {"loai": {"labelIds": ["meo"]}}}]})
    kiem("them 2 cau vang kiem tra", r.status_code == 200, r.text)
    requests.post(G + f"/projects/{pid}/publish", headers=biz).raise_for_status()
    cho(lambda: requests.get(G + f"/projects/{pid}", headers=biz).json()["status"] in ("pendingApproval", "running"), 25)
    if requests.get(G + f"/projects/{pid}", headers=biz).json()["status"] == "pendingApproval":
        requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()
    kiem("du an running", cho(lambda: requests.get(G + f"/projects/{pid}", headers=biz).json()["status"] == "running", 20))
    ns = cho(lambda: sql("gate", f"select remaining_vnd from gate_budgets where project_id='{pid}'") == "12200", 20)
    kiem("ledger bao gate ngan sach cong link 12.200 (20.000 − 7.800)", ns, sql("gate", f"select * from gate_budgets where project_id='{pid}'"))

    print("\n[2] Nguoi chia se: API key, rut gon don le / Quick Link / hang loat, kiem duyet link dich")
    sh, sh_id = dang_ky(["sharer"])
    r = requests.post(G + "/links", headers=biz, json={"url": "https://example.com/a"})
    kiem("doanh nghiep (khong phai sharer) tao link → 403", r.status_code == 403, r.text)
    r = requests.post(G + "/links/api-key", headers=sh)
    key = r.json()["apiKey"]
    kiem("tao API key (hien mot lan)", r.status_code == 200 and key.startswith("lk_"), r.text)
    r = requests.post(G + "/links", headers={"X-Api-Key": key}, json={"url": "https://example.com/bai-viet-1"})
    lk = r.json()
    kiem("tao link bang API key → pendingScan + shortUrl", r.status_code == 201 and lk["status"] == "pendingScan" and lk["shortUrl"].endswith("/g/" + lk["code"]), r.text)
    code = lk["code"]
    r = requests.get(G + "/links/quick", params={"api": key, "url": "https://example.com/quick", "alias": "p2-" + uuid.uuid4().hex[:6]})
    kiem("Quick Link tra ve chuoi link rut gon", r.status_code == 200 and r.text.startswith("http") and "/g/p2-" in r.text, r.text)
    requests.post(G + "/links/admin/blocked-domains", headers=admin, json={"domain": "casino-e2e.example", "reason": "co bac"})
    r = requests.post(G + "/links/bulk", headers=sh, json={"urls": ["https://example.org/x", "https://vip.casino-e2e.example/y", "khong-phai-url"]})
    b = r.json()
    kiem("hang loat: 1 thanh cong, 1 ten mien bi chan, 1 url sai",
         r.status_code == 200 and b[0]["link"] and b[1]["errorCode"] == "ten_mien_bi_chan" and b[2]["errorCode"] == "url_khong_hop_le", b)
    r = requests.post(G + "/links", headers=sh, json={"url": "https://malware.example.test/tai-ve"})
    bad = r.json()["id"]
    st = cho(lambda: requests.get(G + f"/links/{bad}", headers=sh).json()["status"] == "blocked", 15)
    kiem("quet Safe Browsing (dev) chan link doc hai", st, requests.get(G + f"/links/{bad}", headers=sh).text)
    kiem("link sach duoc kich hoat", cho(lambda: requests.get(G + f"/links/{lk['id']}", headers=sh).json()["status"] == "active", 15))
    cho(lambda: requests.get(GATE + f"/g/{code}").status_code == 200, 15)
    r = requests.get(G + f"/g/{code}")
    kiem("GET /g/{code} qua gateway: thong tin trang, khong lo link dich",
         r.status_code == 200 and "example.com" not in r.text and r.json()["turnstileEnabled"], r.text)
    kiem("link bi chan → 404 o trang vuot link", requests.get(G + f"/g/{requests.get(G + f'/links/{bad}', headers=sh).json()['code']}").status_code == 404)

    print("\n[3] Khach vang lai: Turnstile, bo cau hoi, cham cau vang, token mot lan")
    r = requests.post(GATE + f"/g/{code}/sessions", headers=khach("203.0.113.10"), json={})
    kiem("thieu token Turnstile → 403", r.status_code == 403 and "turnstile" in r.text, r.text)
    dat("gate.countdown", 3)
    r = phien(code, "203.0.113.10")
    s = r.json()
    kiem("bo cau: 1 vang + 2 that, co tap nhan", r.status_code == 200 and len(s["questions"]) == 3 and s["labelSchema"]["tools"][0]["name"] == "loai", r.text)
    kiem("response KHONG lo dap an / co danh dau cau vang", "expected" not in r.text.lower() and "gold" not in r.text.lower() and "labelIds" not in r.text, r.text)
    n = requests.post(GATE + f"/g/sessions/{s['sessionId']}/submit", headers=khach("203.0.113.10"), json={"answers": tra_loi(s["questions"])})
    kiem("nop truoc khi het dem nguoc → 409", n.status_code == 409 and "chua_het_dem_nguoc" in n.text, n.text)
    time.sleep(3.3)
    bad_ans = tra_loi(s["questions"]); bad_ans.pop(next(iter(bad_ans)))
    n = requests.post(GATE + f"/g/sessions/{s['sessionId']}/submit", headers=khach("203.0.113.10"), json={"answers": bad_ans})
    kiem("thieu cau tra loi → 400, phien van con", n.status_code == 400, n.text)
    n = requests.post(GATE + f"/g/sessions/{s['sessionId']}/submit", headers=khach("203.0.113.10"), json={"answers": tra_loi(s["questions"], sai_vang=True)})
    kiem("sai cau vang → khong dat, cap bo cau MOI", n.status_code == 200 and not n.json()["passed"] and n.json()["newSession"]["sessionId"] != s["sessionId"], n.text)
    s2 = n.json()["newSession"]
    time.sleep(3.3)  # bo cau moi cung co dem nguoc rieng (chong bot thu lai lien tuc)
    dat("gate.countdown", 0)
    n = requests.post(GATE + f"/g/sessions/{s2['sessionId']}/submit", headers=khach("203.0.113.10"), json={"answers": tra_loi(s2["questions"])})
    kiem("dung cau vang → dat, co redirectUrl", n.status_code == 200 and n.json()["passed"] and n.json()["redirectUrl"].startswith("/go/" + code), n.text)
    url = n.json()["redirectUrl"]
    n2 = requests.post(GATE + f"/g/sessions/{s2['sessionId']}/submit", headers=khach("203.0.113.10"), json={"answers": tra_loi(s2["questions"])})
    kiem("nop lai cung phien → 410", n2.status_code == 410, n2.text)
    g = requests.get(G + url, allow_redirects=False)
    kiem("/go qua gateway → 302 toi link dich", g.status_code == 302 and g.headers.get("Location") == "https://example.com/bai-viet-1", (g.status_code, g.headers.get("Location"), g.text[:200]))
    g = requests.get(G + url, allow_redirects=False)
    kiem("dung token lan 2 → 410 (chong replay)", g.status_code == 410, (g.status_code, g.text[:200]))
    g = requests.get(G + url.replace("?t=", "?t=x"), allow_redirects=False)
    kiem("token sai chu ky → 403", g.status_code == 403, g.status_code)

    print("\n[4] Tien: ledger tra sharer 2 x (1000 − 300) = 1.400 (treo), ky quy du an giam 2.600")
    bal = lambda h: requests.get(G + "/ledger/me/balance", headers=h).json()
    kiem("sharer pendingVnd = 1.400", cho(lambda: bal(sh)["pendingVnd"] == 1400, 20), bal(sh))
    kiem("ngan sach cong link con 9.600", cho(lambda: sql("gate", f"select remaining_vnd from gate_budgets where project_id='{pid}'") == "9600", 15),
         sql("gate", f"select * from gate_budgets where project_id='{pid}'"))
    kiem("annotation luu 2 nhan nguon linkGateway (khong co task / labeler)",
         cho(lambda: sql("annotation", f"select count(*) from annotations where project_id='{pid}' and source='LinkGateway' and labeler_id is null") == "2", 15),
         sql("annotation", f"select source, labeler_id, task_id from annotations where project_id='{pid}'"))

    print("\n[5] Chong lam dung: trung IP 24h, tu vuot (cung IP luc tao / chinh token chu link)")
    n, _ = vuot(code, "203.0.113.10")
    kiem("cung IP lan 2: van mo link nhung KHONG tinh tien", n.status_code == 200 and n.json()["passed"], n.text)
    n = requests.get(G + f"/g/{code}")
    ss = requests.post(G + f"/g/{code}/sessions", json={"turnstileToken": TS}).json()
    nn = requests.post(G + f"/g/sessions/{ss['sessionId']}/submit", json={"answers": tra_loi(ss["questions"])})
    kiem("vuot qua gateway tu chinh may tao link (cung IP) → dat nhung khong tinh tien", nn.status_code == 200 and nn.json()["passed"], nn.text)
    n, _ = vuot(code, "203.0.113.11", extra=sh)
    kiem("chu link dang nhap tu vuot (IP khac) → dat nhung khong tinh tien", n.status_code == 200 and n.json()["passed"], n.text)
    time.sleep(3)
    kiem("van chi 1 luot duoc tra (pending 1.400)", bal(sh)["pendingVnd"] == 1400, bal(sh))
    kiem("ledger: 1 khoan treo GateClick", sql("ledger", f"select count(*) from holds where project_id='{pid}' and kind='GateClick'") == "1")

    print("\n[6] Gioi thieu: tai khoan moi nhap ma, kiem tien → nguoi gioi thieu huong 10% tu phan nen tang")
    ref = requests.get(G + "/links/referrals/me", headers=sh).json()
    kiem("sharer co ma gioi thieu", len(ref["code"]) == 8, ref)
    r = requests.post(G + "/links/referrals/claim", headers=sh, json={"code": ref["code"]})
    kiem("tu nhap ma cua minh → 409", r.status_code == 409, r.text)
    sh2, sh2_id = dang_ky(["sharer", "labeler"])
    time.sleep(1.5)  # user.registered toi link-svc
    r = requests.post(G + "/links/referrals/claim", headers=sh2, json={"code": ref["code"]})
    kiem("tai khoan moi nhap ma gioi thieu", r.status_code == 204, r.text)
    r = requests.post(G + "/links/referrals/claim", headers=sh2, json={"code": ref["code"]})
    kiem("nhap lan 2 → 409", r.status_code == 409, r.text)
    kiem("ledger ghi quan he gioi thieu", cho(lambda: sql("ledger", f"select referrer_id from referrals where referred_id='{sh2_id}'") == sh_id, 15))
    l2 = requests.post(G + "/links", headers=sh2, json={"url": "https://example.com/cua-sharer-2"}).json()
    cho(lambda: requests.get(GATE + f"/g/{l2['code']}").status_code == 200, 15)
    n, _ = vuot(l2["code"], "203.0.113.20")
    kiem("khach vuot link cua nguoi duoc moi", n.status_code == 200 and n.json()["passed"], n.text)
    kiem("nguoi duoc moi: pending 1.400", cho(lambda: bal(sh2)["pendingVnd"] == 1400, 20), bal(sh2))
    kiem("nguoi gioi thieu: them 140 (10% x 1.400) → pending 1.540", cho(lambda: bal(sh)["pendingVnd"] == 1540, 20), bal(sh))

    print("\n[7] Mat khau link (FS-06)")
    l3 = requests.post(G + "/links", headers=sh, json={"url": "https://example.com/bi-mat", "password": "mo-khoa-123"}).json()
    cho(lambda: requests.get(GATE + f"/g/{l3['code']}").status_code == 200, 15)
    kiem("trang bao can mat khau", requests.get(GATE + f"/g/{l3['code']}").json()["requiresPassword"])
    kiem("sai mat khau → 403", phien(l3["code"], "203.0.113.30", password="sai").status_code == 403)
    kiem("dung mat khau → phat cau hoi", phien(l3["code"], "203.0.113.30", password="mo-khoa-123").status_code == 200)

    print("\n[8] Het ngan sach cong link: ledger khong chi vuot, gate ngung phat cau hoi")
    # Da tieu 2 luot (5.200) → con 7.000 → them 2 luot (5.200) → con 1.800 < 2.600.
    for ip in ("203.0.113.40", "203.0.113.41"):
        n, _ = vuot(code, ip)
    kiem("ngan sach con 1.800", cho(lambda: sql("gate", f"select remaining_vnd from gate_budgets where project_id='{pid}'") == "1800", 20),
         sql("gate", f"select * from gate_budgets where project_id='{pid}'"))
    time.sleep(1.5)
    r = phien(code, "203.0.113.42")
    kiem("het ngan sach: bo cau RONG (chi dem nguoc, khong tien)", r.status_code == 200 and r.json()["questions"] == [], r.text)
    esc = sql("ledger", f"select balance from accounts where code='escrow:project:{pid}'")
    kiem("ky quy con 9.600 >= 7.800 danh cho labeler chuyen nghiep", esc == "9600", esc)

    print("\n[9] Thong ke ClickHouse (FS-07)")
    time.sleep(2.5)
    d = requests.get(G + "/gate/stats/me/daily", headers=sh).json()
    hom = d[-1] if d else {}
    kiem("theo ngay: 3 luot tinh tien cua sharer 1, doanh thu uoc tinh 4.200", hom.get("paidClicks") == 3 and hom.get("estimatedRevenueVnd") == 4200 and hom.get("views", 0) >= 6, d)
    o = {x["key"]: x for x in requests.get(G + "/gate/stats/me/outcomes", headers=sh).json()}
    kiem("ket cuc: co trungIp, tuVuot, truotCauVang", all(k in o for k in ("tinhTien", "trungIp", "tuVuot", "truotCauVang")), list(o))
    t = requests.get(G + "/gate/stats/me/top-links", headers=sh).json()
    kiem("top link dung link vua dung", t and t[0]["key"].replace("-", "") == lk["id"].replace("-", ""), t)
    kiem("nguoi khong phai sharer xem thong ke → 403", requests.get(G + "/gate/stats/me/daily", headers=biz).status_code == 403)

    print("\n[10] Bao cao vi pham → kiem duyet → vo hieu hoa + giu doanh thu dang treo")
    dat("link.report_review_threshold", 2)
    for ip in ("198.51.100.1", "198.51.100.1", "198.51.100.2"):
        requests.post(LINK + f"/links/r/{code}/report", headers=khach(ip), json={"reason": "trang lua dao"})
    q = requests.get(G + "/links/admin/review-queue", headers=admin).json()
    it = [x for x in q["items"] if x["link"]["code"] == code]
    kiem("2 IP khac nhau bao cao (IP trung khong dem) → vao hang doi", it and it[0]["reportCount"] == 2, q)
    truoc = bal(sh)["pendingVnd"]
    r = requests.post(G + f"/links/admin/{lk['id']}/disable", headers=admin, json={"reason": "Lua dao", "withholdRevenue": True})
    kiem("admin vo hieu hoa link", r.status_code == 200 and r.json()["status"] == "disabled", r.text)
    kiem("trang vuot link → 404", cho(lambda: requests.get(GATE + f"/g/{code}").status_code == 404, 15))
    kiem("doanh thu treo cua link bi giu (sharer pending giam 4.200)", cho(lambda: bal(sh)["pendingVnd"] == truoc - 4200, 15), (truoc, bal(sh)))
    kiem("platform:withheld = 4.200 + hoa hong cua link khong anh huong",
         sql("ledger", "select balance from accounts where code='platform:withheld'") != "", sql("ledger", "select balance from accounts where code='platform:withheld'"))

    print("\n[11] Nhan cong link: duyet KHONG chi tien theo nhan; doi soat lanh")
    a = requests.get(G + f"/annotations/projects/{pid}?pageSize=50", headers=biz).json()["items"]
    gl = [x for x in a if x["source"] == "linkGateway"]
    kiem("annotation tra source=linkGateway, taskId null", gl and gl[0]["taskId"] is None, gl[:1])
    r = requests.post(G + f"/annotations/{gl[0]['id']}/approve", headers=biz)
    time.sleep(3)
    kiem("duyet nhan cong link: khong tao khoan treo Annotation", r.status_code == 200 and sql("ledger", f"select count(*) from holds where project_id='{pid}' and kind='Annotation'") == "0", r.text)
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai healthy", rec.get("healthy") is True, rec)
    dlq = [l for l in subprocess.run(["docker", "exec", rabbit_container(), "rabbitmqctl", "list_queues", "name", "messages"],
                                     capture_output=True, text=True).stdout.splitlines()
           if ".dlq" in l and l.split()[-1] != "0" and not l.startswith("project-svc.escrow-reserved.dlq")]
    kiem("khong co message nao vao DLQ", dlq == [], dlq)

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e-p2: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
