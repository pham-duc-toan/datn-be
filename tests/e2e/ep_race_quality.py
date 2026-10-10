# Ep race: 2 labeler nop CUNG LUC nhan cuoi cua cung mot task (redundancy 2) — 40 lan.
# Truoc ban sua co task khong bao gio co dong thuan: (1) hai nhan cuoi dem thieu (sua: khoa advisory theo
# task), (2) deadlock khi cap nhat uy tin hai labeler theo thu tu khac nhau (sua: cap nhat theo labeler_id tang dan).
import sys, threading, time, uuid
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import requests
import tro_giup as L  # noqa: E402

N = 40
biz = L.dang_ky(["business"], "racebiz")
k = uuid.uuid4().hex
d = requests.post(L.G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 1000000}).json()
requests.post(L.G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount=1000000")
L.cho(lambda: L.vi(biz)["businessAvailableVnd"] == 1000000, 20)
a, b = L.dang_ky(["labeler"], "raceA"), L.dang_ky(["labeler"], "raceB")
L.dat_va_cho("task.lease_candidate_window", 1, ("task",))
try:
    P, _ = L.tao_du_an(biz, "Race", {f"r{i:02d}": f"Mau race {i}" for i in range(N)}, 2, 2, 1000)
    L.publish_va_duyet(biz, P)
    for u in (a, b):
        requests.post(L.G + f"/projects/{P}/join", headers=u["h"]).raise_for_status()
    cung_task = 0
    for _ in range(N):
        ta, tb = L.nhan_task(a, P, 20), L.nhan_task(b, P, 20)
        if ta is None or tb is None:
            break
        cung_task += ta["taskId"] == tb["taskId"]
        rao = threading.Barrier(2)

        def nop(u, t):
            rao.wait()
            L.nop(u, t["assignmentId"], "a").raise_for_status()

        th = [threading.Thread(target=nop, args=(a, ta)), threading.Thread(target=nop, args=(b, tb))]
        [x.start() for x in th]
        [x.join() for x in th]
    print("so lan hai nguoi cung task:", cung_task, "/", N)
    ok = L.cho(lambda: L.sql("quality", f"select count(*) from consensus_rounds where project_id='{P}'") == str(N), 60)
    print("task co ket qua dong thuan:", L.sql("quality", f"select count(*) from consensus_rounds where project_id='{P}'"), "/", N)
    print(("  PASS " if ok else "  FAIL ") + f"ep race: {N} task, hai nguoi nop dong thoi nhan cuoi → task nao cung co dong thuan")
finally:
    L.dat("task.lease_candidate_window", 32)
