# Chay lan luot moi bo E2E, tom tat PASS/FAIL tung bo. e2e_p2 chay hai lan: perClick va batched.
import os, re, subprocess, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, GOC_REPO, RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER, URL_ANNOTATION, URL_GATE, URL_LINK, URL_TASK, rabbit_container, sql_container  # noqa: E402,F401
from tai_khoan_e2e import mo_rong_gioi_han, tra_gioi_han  # noqa: E402

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


# Gateway gioi han tan suat; E2E dang ky / dang nhap hang tram lan tu MOT IP → nang han muc, xong tra lai.
HAN_MUC_CU = mo_rong_gioi_han(admin())

ok = True
# Du an lam trung tam (kien truc 3.18): luong chinh lam bai → cham → thuong → khieu nai → chot.
ok &= chay("du_an_trung_tam", "e2e_du_an_trung_tam.py")
ok &= chay("suat_thuong", "e2e_suat_thuong.py")
# Cau tra loi mo cham theo nghia (3.19): ml-svc nhung cau; dung ml-svc → so chu sau han.
ok &= chay("cau_tra_loi_mo", "e2e_cau_tra_loi_mo.py")
ok &= chay("tong_the", "e2e_tong_the.py")
ok &= chay("settings", "e2e_settings.py")
ok &= chay("p2_cong_link_perClick", "e2e_p2.py")
ok &= chay("modality", "e2e_modality.py")
ok &= chay("ep_race_quality", "ep_race_quality.py")
ok &= chay("ep_race_uy_tin", "ep_race_uy_tin.py")
ok &= chay("dong_cho_chi", "e2e_dong_cho_chi.py")
ok &= chay("kyb_thong_bao", "e2e_kyb_thong_bao.py")
ok &= chay("hoan_thien", "e2e_hoan_thien.py")
ok &= chay("bao_ve_du_lieu", "e2e_bao_ve_du_lieu.py")
ok &= chay("luu_tru", "e2e_luu_tru.py")
ok &= chay("cong_cu", "e2e_cong_cu.py")
ok &= chay("thanh_vien", "e2e_thanh_vien.py")
# Dot hoan thien 10/2026
ok &= chay("bao_mat_tai_khoan", "e2e_bao_mat_tai_khoan.py")
ok &= chay("cong_thanh_toan", "e2e_cong_thanh_toan.py")
ok &= chay("hoa_don", "e2e_hoa_don.py")
ok &= chay("bao_ve_du_an", "e2e_bao_ve_du_an.py")
ok &= chay("nhap_nguon_ngoai", "e2e_nhap_nguon_ngoai.py")
ok &= chay("xuat_mo_rong", "e2e_xuat_mo_rong.py")
ok &= chay("chi_so_chat_luong", "e2e_chi_so_chat_luong.py")
ok &= chay("watermark_av", "e2e_watermark_av.py")
ok &= chay("hang_doi_duyet", "e2e_hang_doi_duyet.py")
ok &= chay("thong_bao_realtime", "e2e_thong_bao_realtime.py")

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
q = requests.get(f"{RABBIT_MGMT}/api/queues/%2F", auth=(RABBIT_USER, RABBIT_PASSWORD)).json()
dlq = [(x["name"], x.get("messages", 0)) for x in q if x["name"].endswith(".dlq") and x.get("messages", 0) > 0]
print(f"  DLQ co tin: {dlq if dlq else 'khong (0 / ' + str(sum(1 for x in q if x['name'].endswith('.dlq'))) + ' hang DLQ)'}")
ok &= not dlq
ton = {}
for db in ("identity", "project", "task", "annotation", "ledger", "payment", "link", "gate", "quality", "admin", "notification"):
    r = subprocess.run(["docker", "exec", sql_container(db), "psql", "-U", f"{db}_user", "-d", f"{db}_db", "-tAc",
                        "select count(*) from outbox where published_at is null"], capture_output=True, text=True)
    ton[db] = (r.stdout.strip() or r.stderr.strip()[:60])
print("  outbox chua gui:", ton)
ok &= all(v == "0" for v in ton.values())
r = subprocess.run(["docker", "exec", sql_container("notification"), "psql", "-U", "notification_user", "-d", "notification_db",
                    "-tAc", "select count(*) from notifications where email_state = 'Failed'"], capture_output=True, text=True)
print("  email thong bao gui hong (Failed):", r.stdout.strip() or r.stderr.strip()[:60])
ok &= r.stdout.strip() == "0"
rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin()).json()
print("  doi soat so cai:", "LANH" if rec.get("healthy") else rec)
ok &= rec.get("healthy") is True
tra_gioi_han(admin(), HAN_MUC_CU)
print("TAT CA DAT" if ok else "CO LOI")
