# E2E kiem chung NC-B-01 (TLA+) tren luong du an lam trung tam (3.18): project.graded (tra thuong bai dat)
# toi ledger SAU project.completed (hoan ky quy).
#
# Kich ban TLC tim ra o luong cu: ledger xu ly "ket thuc" TRUOC "tra tien" → hoan het ky quy → tra tien gap
# ky_quy_da_dong → DLQ, labeler dat ma khong bao gio duoc tra.
#
# Tai hien tren he thong that: DUNG container ledger, labeler nop bai, doanh nghiep ket thuc som → quality cham
# (project.graded) → het han khieu nai → project.completed (hai event nam cho trong hai queue cua ledger), BAT
# lai ledger — thu tu xu ly giua hai queue la tuy y. Lap nhieu vong. Ky quy DANG DONG (cho tra du so bai dat)
# phai cho ket qua dung o MOI vong.
#
# Chi chay voi stack Docker (E2E_PROFILE=demo): can dung / bat container ledger.
import os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import RABBIT_PASSWORD, RABBIT_USER, TIEN_TO_CONTAINER  # noqa: E402
import tro_giup as L  # noqa: E402

SO_VONG = int(os.environ.get("E2E_SO_VONG", "3"))
LEDGER = f"{TIEN_TO_CONTAINER}ledger"


def docker(*lenh):
    subprocess.run(["docker", *lenh], check=True, capture_output=True)


if os.environ.get("E2E_PROFILE", "dev").lower() != "demo":
    print("  SKIP e2e_dong_cho_chi: chi chay voi stack Docker (E2E_PROFILE=demo) — can dung container ledger")
    sys.exit(0)

biz = L.dang_ky(["business"], "dongchochi")
k = uuid.uuid4().hex
d = requests.post(L.G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 2000000}).json()
requests.post(L.G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount=2000000")
L.cho(lambda: L.vi(biz)["businessAvailableVnd"] == 2000000, 30)

try:
    L.dat_va_cho("ledger.reward_hold", 0, ("ledger",))
    L.dat_va_cho("task.appeal_window", 5, ("task",))
    L.dat_va_cho("task.reaper_interval", 2, ("task",))
    for vong in range(1, SO_VONG + 1):
        lab = L.dang_ky(["labeler"], f"dcc{vong}")
        # 3 suat x (10.000 + phi 3.000) = ky quy 39.000; mot bai dat → tra 13.000, hoan 26.000.
        P, _ = L.tao_du_an(biz, f"Dong cho chi {vong}", {"m1": f"Mau dong cho chi {vong}"}, 1, 1, 10000)
        L.publish_va_duyet(biz, P)
        requests.post(L.G + f"/projects/{P}/join", headers=lab["h"]).raise_for_status()
        b, r = L.lam_bai(lab, P, lambda t: "a")
        r.raise_for_status()
        truoc = L.vi(biz)["businessAvailableVnd"]

        docker("stop", LEDGER)   # tu day project.graded va project.completed nam cho trong hai queue cua ledger
        try:
            L.ket_thuc_som(biz, P)
            xong = L.cho(lambda: requests.get(L.G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "completed", 60)
            L.kiem(f"vong {vong}: cham + chot hoan thanh khi ledger dang tat (event cho trong queue)", xong,
                   requests.get(L.G + f"/projects/{P}", headers=biz["h"]).json()["status"])
        finally:
            docker("start", LEDGER)

        tra = L.cho(lambda: L.sql("ledger", f"select count(*) from holds where annotation_id='{b['attemptId']}' and kind='ProjectReward'") == "1", 90)
        L.kiem(f"vong {vong}: bai dat DUOC TRA du ledger xu ly hai event theo thu tu nao", tra,
               L.sql("ledger", f"select state, expected_paid_attempts from project_escrows where project_id='{P}'"))
        dong = L.cho(lambda: L.sql("ledger", f"select state from project_escrows where project_id='{P}'") == "Closed", 60)
        L.kiem(f"vong {vong}: ky quy dong sau khi tra du", dong, L.sql("ledger", f"select state from project_escrows where project_id='{P}'"))
        L.kiem(f"vong {vong}: hai suat thua (26.000) ve vi doanh nghiep",
               L.cho(lambda: L.vi(biz)["businessAvailableVnd"] == truoc + 39000 - 13000, 30), (truoc, L.vi(biz)))
finally:
    for key, v in L.GOC.items():
        requests.put(L.G + f"/admin/settings/{key}", headers=L.admin, json={"value": v, "reason": "e2e dong cho chi: tra lai"})

dlq = requests.get(f"{L.RABBIT_MGMT}/api/queues/%2F", auth=(RABBIT_USER, RABBIT_PASSWORD)).json()
co = [(q["name"], q["messages"]) for q in dlq if q["name"].endswith(".dlq") and q.get("messages", 0)]
L.kiem("khong event nao vao DLQ", not co, co)
print(f"\nKET QUA: {L.KQ['pass']} pass, {L.KQ['fail']} fail")
