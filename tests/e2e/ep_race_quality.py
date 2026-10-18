# Ep race cham CA DU AN (kien truc 3.18): bai cuoi lam du suat → task-svc phat CUNG LUC attempt.submitted
# va project.collection_finished; quality-svc nhan hai event tren HAI queue, cai nao toi truoc cung duoc.
# Hai transaction cung thu cham: khong khoa thi moi ben chi thay du lieu cua minh, ca hai thay thieu va
# KHONG BEN NAO cham (du an ket o Grading) — hoac ca hai cung cham (tra thuong hai lan). Sua: khoa
# advisory theo du an trong thu_cham, project_collections.graded_at chan cham lai.
#
# Lap N du an, moi du an 3 suat, 3 labeler nop CUNG LUC. Bat bien: moi du an duoc cham DUNG MOT lan
# (graded_at co gia tri, 3 attempt_grades, mot project.graded trong outbox), va ledger tra thuong dung 3 bai.
import sys, threading, time, uuid
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import requests
import tro_giup as L  # noqa: E402

N = int(sys.argv[1]) if len(sys.argv) > 1 else 10
biz = L.dang_ky(["business"], "racebiz")
k = uuid.uuid4().hex
d = requests.post(L.G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 2000000}).json()
requests.post(L.G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount=2000000")
L.cho(lambda: L.vi(biz)["businessAvailableVnd"] == 2000000, 20)
lab = [L.dang_ky(["labeler"], f"race{i}") for i in range(3)]

du_an = []
for i in range(N):
    P, _ = L.tao_du_an(biz, f"Race {i}", {"r1": f"Mau race {i} a", "r2": f"Mau race {i} b"}, thuong=10000, suat=3)
    L.publish_va_duyet(biz, P)
    du_an.append(P)
for P in du_an:
    for u in lab:
        L.tham_gia(u, P)

for P in du_an:
    # Moi nguoi lam xong bai (chua nop), roi ca ba nop cung luc → bai thu ba dong thu bai.
    bai = []
    for u in lab:
        b, _ = L.lam_bai(u, P, lambda t: "a", nop=False)
        bai.append((u, b["attemptId"]))
    rao = threading.Barrier(3)

    def nop(u, aid):
        rao.wait()
        L.nop_bai(u, aid).raise_for_status()

    th = [threading.Thread(target=nop, args=x) for x in bai]
    [x.start() for x in th]
    [x.join() for x in th]

ds = ",".join(f"'{p}'" for p in du_an)
ok = L.cho(lambda: L.sql("quality", f"select count(*) from project_collections where project_id in ({ds}) and graded_at is not null") == str(N), 90)
time.sleep(2)
cham = L.sql("quality", f"select count(*) from project_collections where project_id in ({ds}) and graded_at is not null")
bai_cham = L.sql("quality", f"select count(*) from attempt_grades where project_id in ({ds})")
phat = L.sql("quality", f"select count(*) from outbox where event_type = 'project.graded' and envelope_json->'payload'->>'projectId' in ({ds})")
tra = L.cho(lambda: L.sql("ledger", f"select count(*) from holds where kind = 'ProjectReward' and project_id in ({ds})") == str(3 * N), 60)
print(f"du an da cham: {cham}/{N}, bai cham: {bai_cham}/{3 * N}, project.graded phat: {phat}/{N}")
L.kiem(f"ep race: {N} du an, 3 bai nop dong thoi → du an nao cung duoc cham DUNG MOT lan",
       ok is not None and bai_cham == str(3 * N) and phat == str(N), (cham, bai_cham, phat))
L.kiem("ledger tra thuong dung 3 bai moi du an (khong tra hai lan)", tra is not None,
       L.sql("ledger", f"select count(*) from holds where kind = 'ProjectReward' and project_id in ({ds})"))
print(f"\nKET QUA: {L.KQ['pass']} pass, {L.KQ['fail']} fail")
