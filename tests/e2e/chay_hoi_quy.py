# Chay lan luot moi bo E2E, tom tat PASS/FAIL tung bo. e2e_p2 chay hai lan: perClick va batched.
import os, re, subprocess, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401

S = os.path.dirname(os.path.abspath(__file__))
KET_QUA = os.path.join(S, "ket-qua")
os.makedirs(KET_QUA, exist_ok=True)
PY = sys.executable


def admin():
    t = requests.post(G + "/auth/login", json={"email": "admin@crowd.local", "password": "Matkhau@123"}).json()["accessToken"]
    return {"Authorization": "Bearer " + t}


def dat(key, value):
    r = requests.put(G + f"/admin/settings/{key}", headers=admin(), json={"value": value, "reason": "hoi quy"})
    assert r.status_code == 200, r.text


def chay(ten, file):
    t0 = time.time()
    p = subprocess.run([PY, file], cwd=S, capture_output=True, text=True, encoding="utf-8", errors="replace",
                       env=dict(os.environ, PYTHONIOENCODING="utf-8"))
    out = p.stdout + p.stderr
    open(os.path.join(KET_QUA, f"{ten}.txt"), "w", encoding="utf-8").write(out)
    so_pass = len(re.findall(r"^\s*PASS", out, re.M))
    loi = [l.strip() for l in out.splitlines() if re.match(r"^\s*FAIL", l)]
    skip = [l.strip() for l in out.splitlines() if "SKIP" in l]
    tb = "Traceback" in out
    print(f"{ten:28s} pass={so_pass:3d} fail={len(loi):2d} {'TRACEBACK ' if tb else ''}({time.time() - t0:.0f}s)", flush=True)
    for l in loi + skip:
        print("    ", l[:260], flush=True)
    return len(loi) == 0 and not tb


ok = True
ok &= chay("tong_the", "e2e_tong_the.py")
ok &= chay("settings", "e2e_settings.py")
ok &= chay("p3_chat_luong", "e2e_p3.py")
ok &= chay("p2_cong_link_perClick", "e2e_p2.py")
ok &= chay("modality", "e2e_modality.py")
ok &= chay("ep_race_quality", "ep_race_quality.py")
ok &= chay("dong_cho_chi", "e2e_dong_cho_chi.py")

dat("ledger.gate_batch_interval", 2)
dat("ledger.gate_payout_mode", "batched")
time.sleep(3)
try:
    ok &= chay("p2_cong_link_batched", "e2e_p2.py")
finally:
    dat("ledger.gate_payout_mode", "perClick")
    dat("ledger.gate_batch_interval", 60)
print("\n== Kiem tra tong sau hoi quy")
time.sleep(10)
q = requests.get(f"{RABBIT_MGMT}/api/queues/%2F", auth=("datn", "dev_rabbit_pw")).json()
dlq = [(x["name"], x.get("messages", 0)) for x in q if x["name"].endswith(".dlq") and x.get("messages", 0) > 0]
print(f"  DLQ co tin: {dlq if dlq else 'khong (0 / ' + str(sum(1 for x in q if x['name'].endswith('.dlq'))) + ' hang DLQ)'}")
ok &= not dlq
ton = {}
for db in ("identity", "project", "task", "annotation", "ledger", "payment", "link", "gate", "quality", "admin"):
    r = subprocess.run(["docker", "exec", sql_container(db), "psql", "-U", f"{db}_user", "-d", f"{db}_db", "-tAc",
                        "select count(*) from outbox where published_at is null"], capture_output=True, text=True)
    ton[db] = (r.stdout.strip() or r.stderr.strip()[:60])
print("  outbox chua gui:", ton)
ok &= all(v == "0" for v in ton.values())
rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin()).json()
print("  doi soat so cai:", "LANH" if rec.get("healthy") else rec)
ok &= rec.get("healthy") is True
print("TAT CA DAT" if ok else "CO LOI")
