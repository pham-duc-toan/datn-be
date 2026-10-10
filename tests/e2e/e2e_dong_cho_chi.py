# E2E kiem chung NC-B-01 (TLA+): annotation.approved toi ledger SAU project.completed.
#
# Kich ban TLC tim ra (trace 15 buoc): duyet nhan → dong so → chot hoan thanh → ledger xu ly
# project.completed TRUOC annotation.approved → hoan het ky quy → approved gap ky_quy_da_dong → DLQ,
# labeler da duoc duyet ma khong bao gio duoc tra.
#
# Tai hien tren he thong that: DUNG container ledger, duyet + dong du an (hai event nam cho trong
# hai queue), BAT lai ledger — thu tu xu ly giua hai queue la tuy y. Lap nhieu vong. Ban sua (ky quy
# DANG DONG cho chi du) phai cho ket qua dung o MOI vong.
#
# Chi chay voi stack Docker (E2E_PROFILE=demo): can dung / bat container ledger.
import os, subprocess, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import TIEN_TO_CONTAINER  # noqa: E402
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
    L.dat_va_cho("ledger.hold_duration", 0, ("ledger",))
    for vong in range(1, SO_VONG + 1):
        lab = L.dang_ky(["labeler"], f"dcc{vong}")
        P, _ = L.tao_du_an(biz, f"Dong cho chi {vong}", {"m1": f"Mau dong cho chi {vong}"}, 1, 1, 10000)
        L.publish_va_duyet(biz, P)
        requests.post(L.G + f"/projects/{P}/join", headers=lab["h"]).raise_for_status()
        t = L.nhan_task(lab, P, 20)
        L.nop(lab, t["assignmentId"], "a").raise_for_status()
        a = L.cho(lambda: [x for x in L.ds_nhan(biz, P) if x["status"] == "pendingReview"], 20)[0]
        truoc = L.vi(biz)["businessAvailableVnd"]

        docker("stop", LEDGER)   # tu day annotation.approved va project.completed nam cho trong hai queue
        try:
            requests.post(L.G + f"/annotations/{a['id']}/approve", headers=biz["h"]).raise_for_status()
            requests.post(L.G + f"/projects/{P}/pause", headers=biz["h"]).raise_for_status()
            r = L.cho(lambda: (lambda x: x.json() if x.status_code == 200 else None)(
                requests.post(L.G + f"/projects/{P}/complete", headers=biz["h"])), 30)
            L.kiem(f"vong {vong}: hoan thanh duoc khi ledger dang tat (event cho trong queue)", r is not None and r["status"] == "completed", r)
        finally:
            docker("start", LEDGER)

        tra = L.cho(lambda: L.sql("ledger", f"select count(*) from holds where annotation_id='{a['id']}'") == "1", 90)
        L.kiem(f"vong {vong}: nhan da duyet DUOC TRA du ledger xu ly hai event theo thu tu nao", tra,
               L.sql("ledger", f"select state, expected_paid_annotations from project_escrows where project_id='{P}'"))
        dong = L.cho(lambda: L.sql("ledger", f"select state from project_escrows where project_id='{P}'") == "Closed", 60)
        L.kiem(f"vong {vong}: ky quy dong sau khi chi du", dong, L.sql("ledger", f"select state from project_escrows where project_id='{P}'"))
        # Ngan sach tao_du_an = 1 mau x tran 1 x 10.000 x 2 = 20.000; da chi 13.000 (10.000 + phi 30%).
        L.kiem(f"vong {vong}: phan ky quy con lai (7.000) ve vi doanh nghiep",
               L.cho(lambda: L.vi(biz)["businessAvailableVnd"] == truoc + 20000 - 13000, 30), (truoc, L.vi(biz)))
finally:
    for key, v in L.GOC.items():
        requests.put(L.G + f"/admin/settings/{key}", headers=L.admin, json={"value": v, "reason": "e2e dong cho chi: tra lai"})

dlq = requests.get(f"{L.RABBIT_MGMT}/api/queues/%2F", auth=("datn", "dev_rabbit_pw")).json()
co = [(q["name"], q["messages"]) for q in dlq if q["name"].endswith(".dlq") and q.get("messages", 0)]
L.kiem("khong event nao vao DLQ", not co, co)
print(f"\nKET QUA: {L.KQ['pass']} pass, {L.KQ['fail']} fail")
