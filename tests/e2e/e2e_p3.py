# E2E P3 — quality-svc: dong thuan, redundancy thich ung, cau vang kiem tra, uy tin.
import os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401

PW = "Matkhau@123"
KQ = {"pass": 0, "fail": 0}


def kiem(ten, dk, ct=""):
    KQ["pass" if dk else "fail"] += 1
    print(("  PASS " if dk else "  FAIL ") + ten + ("" if dk else " | " + str(ct)[:500]))


def login(e):
    r = requests.post(G + "/auth/login", json={"email": e, "password": PW}); r.raise_for_status()
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
lab = {"lab1": login("labeler1@crowd.local"), "lab2": login("labeler2@crowd.local"), "lab3": login("labeler3@crowd.local")}
ID = {"lab1": "1f5e19a7-9f58-589b-8c74-45334ff35d05", "lab2": "0398f264-d694-5176-9614-844201175b74",
      "lab3": "14299c5f-f7c3-5075-b65a-cd4fbc2aa194"}

# 8 cau: c1 la cau TRANH CHAP co y, c8 la CAU VANG kiem tra.
CAU = {
    "c1": ("Hang dep nhung giao hoi cham mot chut.", "tich_cuc"),
    "c2": ("San pham tuyet voi, rat hai long.", "tich_cuc"),
    "c3": ("Chat luong qua te, khong nen mua.", "tieu_cuc"),
    "c4": ("Dong goi can than, giao nhanh.", "tich_cuc"),
    "c5": ("Hang loi, shop khong tra loi tin nhan.", "tieu_cuc"),
    "c6": ("Gia re ma dung tot, se ung ho tiep.", "tich_cuc"),
    "c7": ("Mau sac khac hinh, that vong.", "tieu_cuc"),
    "c8": ("Rat tot, dang dong tien bat gao.", "tich_cuc"),
}
TEN_THEO_TEXT = {v[0]: k for k, v in CAU.items()}


def tra_loi(labeler, ten):
    dung = CAU[ten][1]
    sai = "tieu_cuc" if dung == "tich_cuc" else "tich_cuc"
    if ten == "c1":
        return {"lab1": "tich_cuc", "lab2": "tieu_cuc", "lab3": "tich_cuc"}[labeler]
    if ten == "c8" and labeler == "lab2":
        return sai                       # lab2 truot cau vang
    return dung


print("\n[1] Tao du an van ban: redundancy 2, tran 3, 50% cau vang kiem tra")
r = requests.post(G + "/projects", headers=biz, json={"name": "[e2e-p3] Cam xuc review", "description": "p3", "modality": "text", "visibility": "public"})
pid = r.json()["id"]
requests.put(G + f"/projects/{pid}/label-schema", headers=biz, json={"modality": "text", "tools": [
    {"name": "cam_xuc", "kind": "classification", "classes": ["tich_cuc", "tieu_cuc"]}]}).raise_for_status()
requests.put(G + f"/projects/{pid}/guideline", headers=biz, json={"markdown": "p3", "examples": []}).raise_for_status()
r = requests.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 1000, "redundancy": 2, "maxRedundancy": 1, "budgetVnd": 100000, "deadline": "2027-12-31T00:00:00Z"})
kiem("tran < redundancy → 400 tran_redundancy_khong_hop_le", r.status_code == 400 and "tran_redundancy_khong_hop_le" in r.text, r.text)
r = requests.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 1000, "redundancy": 2, "maxRedundancy": 3, "budgetVnd": 30000, "deadline": "2027-12-31T00:00:00Z"})
kiem("dat gia voi tran 3", r.status_code == 200 and r.json()["maxRedundancy"] == 3, r.text)
r = requests.put(G + f"/projects/{pid}/quality-control", headers=biz, json={"goldCheckPercent": 60})
kiem("ti le cau vang > 50 → 400", r.status_code == 400, r.text)
r = requests.put(G + f"/projects/{pid}/quality-control", headers=biz, json={"goldCheckPercent": 50})
kiem("dat ti le cau vang 50%", r.status_code == 200 and r.json()["goldCheckPercent"] == 50, r.text)

r = requests.post(G + f"/projects/{pid}/datasets/manifest", headers=biz, json={"name": "lo", "rows": [{"text": v[0], "name": k} for k, v in CAU.items()]})
cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=biz).json() if d["status"] == "ready"], 30)
mau = {m["originalName"]: m for m in requests.get(G + f"/projects/{pid}/samples?pageSize=50", headers=biz).json()["items"]}
r = requests.post(G + f"/projects/{pid}/gold-items", headers=biz, json={"items": [
    {"sampleId": mau["c8"]["id"], "purpose": "qualityCheck", "expectedPayload": {"cam_xuc": {"labelIds": ["tich_cuc"]}}}]})
kiem("them cau vang kiem tra c8", r.status_code == 200, r.text)

rd = requests.get(G + f"/projects/{pid}/readiness", headers=biz).json()
# 8 mau x tran 3 x (1000 + 300) — ky quy tinh theo TRAN.
kiem("ky quy toi thieu tinh theo tran: 8 x 3 x 1300 = 31.200", rd["estimatedCostVnd"] == 31200, rd)
r = requests.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 1000, "redundancy": 2, "maxRedundancy": 3, "budgetVnd": 40000, "deadline": "2027-12-31T00:00:00Z"})
requests.post(G + f"/projects/{pid}/publish", headers=biz).raise_for_status()
st = cho(lambda: requests.get(G + f"/projects/{pid}", headers=biz).json()["status"] == "pendingApproval", 20)
kiem("ky quy xong", st, requests.get(G + f"/projects/{pid}", headers=biz).text)
requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()
for k in lab:
    requests.post(G + f"/projects/{pid}/join", headers=lab[k]).raise_for_status()

print("\n[2] lab1, lab2 lam het task; c1 co y chon khac nhau")
cau_vang_gap = {}
khoa_task_thuong = None


def lam_het(k):
    global khoa_task_thuong
    so = 0
    for _ in range(40):
        r = requests.post(G + f"/tasks/projects/{pid}/next", headers=lab[k])
        if r.status_code == 403:
            time.sleep(1); continue
        if r.status_code == 204:
            return so
        t = r.json()
        ten = TEN_THEO_TEXT[t["content"]["text"]]
        if ten == "c8":
            cau_vang_gap[k] = t
        elif khoa_task_thuong is None:
            khoa_task_thuong = sorted(t.keys())
        s = requests.post(G + f"/tasks/assignments/{t['assignmentId']}/submit", headers=lab[k],
                          json={"payload": {"cam_xuc": {"labelIds": [tra_loi(k, ten)]}}})
        s.raise_for_status()
        if ten != "c8":
            so += 1
    return so


n1 = lam_het("lab1"); n2 = lam_het("lab2")
kiem("lab1, lab2 moi nguoi lam 7 task that", n1 == 7 and n2 == 7, (n1, n2))
kiem("ca hai deu gap cau vang trong luong task", set(cau_vang_gap) == {"lab1", "lab2"}, list(cau_vang_gap))
if cau_vang_gap:
    kiem("cau vang co CUNG hinh dang response voi task that", sorted(next(iter(cau_vang_gap.values())).keys()) == khoa_task_thuong)

time.sleep(3)
n_ann = len(requests.get(G + f"/annotations/projects/{pid}?pageSize=100", headers=biz).json()["items"])
kiem("cau vang KHONG tao nhan ben annotation-svc (14 nhan, khong phai 16)", n_ann == 14, n_ann)
kiem("gold.answered da toi quality: lab1 dung, lab2 sai",
     sql("quality", f"select string_agg(correct::text, ',' order by correct) from gold_answers where project_id='{pid}'") == "false,true",
     sql("quality", f"select labeler_id, correct from gold_answers where project_id='{pid}'"))

print("\n[3] Tranh chap c1 → quality xin them nguoi → task-svc mo lai task voi redundancy 3")
task_c1 = sql("task", f"select id from tasks where sample_id='{mau['c1']['id']}'")
target = cho(lambda: sql("task", f"select redundancy_target from tasks where id='{task_c1}'") == "3", 20)
kiem("task c1: redundancy 2 → 3, mo lai", target and sql("task", f"select state from tasks where id='{task_c1}'") == "Open",
     sql("task", f"select redundancy_target, state from tasks where id='{task_c1}'"))
kiem("cac task dong thuan khong bi tang",
     sql("task", f"select count(*) from tasks where project_id='{pid}' and redundancy_target=3") == "1")
n3 = lam_het("lab3")
kiem("lab3 chi nhan duoc task c1 (cac task khac da du nguoi)", n3 == 1, n3)

print("\n[4] Dong thuan → annotation-svc danh dau khop / lech")


def nhan_c1():
    ds = requests.get(G + f"/annotations/projects/{pid}?pageSize=100", headers=biz).json()["items"]
    ds = [a for a in ds if a["sampleId"] == mau["c1"]["id"]]
    return ds if len(ds) == 3 and all(a["consensusAgrees"] is not None for a in ds) else None


c1 = cho(nhan_c1, 20)
kiem("3 nhan c1 co co dong thuan", c1 is not None)
if c1:
    theo = {a["labelerId"]: a["consensusAgrees"] for a in c1}
    kiem("lab1, lab3 khop; lab2 lech", theo == {ID["lab1"]: True, ID["lab3"]: True, ID["lab2"]: False}, theo)
r = requests.get(G + f"/annotations/projects/{pid}?consensusAgrees=false", headers=biz).json()
kiem("loc consensusAgrees=false → 1 nhan", r["total"] == 1, r["total"])

print("\n[5] Duyet: tung nhan lech + hang loat nhan khop; ledger chi du 3 luot cho task c1 (tran 3)")
lech = r["items"][0]["id"]
requests.post(G + f"/annotations/{lech}/approve", headers=biz).raise_for_status()
r = requests.post(G + f"/annotations/projects/{pid}/approve-agreed", headers=biz)
kiem("duyet hang loat 14 nhan khop (15 nhan - 1 nhan lech)", r.status_code == 200 and r.json()["approvedCount"] == 14, r.text)
het = cho(lambda: sql("ledger", f"select count(*) from holds where project_id='{pid}'") == "15", 20)
kiem("ledger chi tra 15 luot (ke ca luot thu 3 cua task c1)", het, sql("ledger", f"select count(*) from holds where project_id='{pid}'"))
dlq = [l for l in subprocess.run(["docker", "exec", rabbit_container(), "rabbitmqctl", "list_queues", "name", "messages"],
                                 capture_output=True, text=True).stdout.splitlines()
       if ".dlq" in l and not l.startswith("project-svc.escrow-reserved.dlq") and l.split()[-1] != "0"]
kiem("khong co message moi nao vao DLQ (tru 4 message cu tu 28/09)", dlq == [], dlq)

print("\n[6] quality: tong quan, labeler, Dawid-Skene, uy tin → task-svc")
s = requests.get(G + f"/quality/projects/{pid}/summary", headers=biz)
kiem("summary: 7 task dong thuan, 1 lan xin them nguoi", s.status_code == 200 and s.json()["agreed"] == 7 and s.json()["redundancyIncreases"] == 1, s.text)
so_vang = len(cau_vang_gap)   # lab3 co the gap cau vang khi con task c1; chi lab2 tra loi sai
kiem("summary: so cau vang = so lan gap (%s), dung %s%%" % (so_vang, round(100 * (so_vang - 1) / so_vang)),
     s.json().get("goldAnswers") == so_vang and s.json().get("goldAccuracyPercent") == round(100 * (so_vang - 1) / so_vang), s.text)
r = requests.get(G + f"/quality/projects/{pid}/summary", headers=lab["lab1"])
kiem("labeler xem summary du an → 404", r.status_code == 404, r.status_code)
r = requests.post(G + "/quality/admin/dawid-skene/run", headers=admin)
kiem("admin chay Dawid-Skene", r.status_code == 200 and r.json()["computed"] >= 1, r.text)
lb = {x["labelerId"]: x for x in requests.get(G + f"/quality/projects/{pid}/labelers", headers=biz).json()}
kiem("labeler: lab1 khop 100%, lab2 thap hon", lb[ID["lab1"]]["agreementPercent"] == 100 and lb[ID["lab2"]]["agreementPercent"] < 100, lb)
kiem("Dawid-Skene co do tin cay cho lab1, lab2 (>= 5 nhan)", "cam_xuc" in lb[ID["lab1"]]["dawidSkeneSkill"] and "cam_xuc" in lb[ID["lab2"]]["dawidSkeneSkill"], lb)
kiem("uy tin lab1 > lab2", lb[ID["lab1"]]["reputation"] > lb[ID["lab2"]]["reputation"], lb)
me = requests.get(G + "/quality/me", headers=lab["lab1"]).json()
kiem("/quality/me khong lo so cau vang", "goldAnswers" not in me and me["reputation"] == lb[ID["lab1"]]["reputation"], me)
rep = cho(lambda: sql("task", f"select reputation from labeler_cache where user_id='{ID['lab1']}'") == str(me["reputation"]), 15)
kiem("reputation.changed da toi task-svc (labeler_cache)", rep, sql("task", f"select reputation from labeler_cache where user_id='{ID['lab1']}'"))

print("\n[7] Ket qua du an mang trang thai dong thuan")
kq = requests.get(G + f"/annotations/projects/{pid}/results", headers=biz).json()
st = {x["sampleId"]: x["consensusStatus"] for x in kq["samples"]}
kiem("moi mau co consensusStatus = agreed", set(st.values()) == {"agreed"} and len(st) == 7, st)

print(f"\nTONG: {KQ['pass']} PASS, {KQ['fail']} FAIL")
sys.exit(1 if KQ["fail"] else 0)
