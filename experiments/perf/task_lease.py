"""NC-B-06 — phan phoi task duoi tai: N labeler cung "lay task → nop" lien tuc.

GIA THUYET
  H3: Lay task bang UPDATE ... FOR UPDATE SKIP LOCKED (docs 3.5) KHONG BAO GIO cap trung
      (moi (task, labeler) toi da mot luot, so nop moi task <= redundancy) o moi muc dong thoi.
  H4: Thong luong tang theo so labeler toi khi cham tran NHOM KET NOI Postgres cua task-svc
      (Maximum Pool Size = 20 o dev); sau do do tre tang tuyen tinh (request xep hang cho ket noi)
      chu thong luong khong tang.
      → SAI (xem docs/thi-nghiem/nc-b-06-hieu-nang.md): pool khong bao gio can (4–10 / 20 ket
        noi ranh); tran nam o chi phi cau lay task va o CPU may do.

Sinh tai bang NHIEU TIEN TRINH (--tien-trinh, mac dinh 8): mot tien trinh httpx bao hoa mot
nhan CPU o ~20 luot/giay — do do la tran cua client. Moi muc ghi cpu_client_max de kiem.

Goi THANG task-svc (cong 8103, khong qua gateway) de do chinh service. Can ca he thong dev
dang chay (identity, project, task, annotation, ledger, payment, gateway).

Chay:  ../.venv/Scripts/python task_lease.py --muc 10,50,100,200,400 --giay 30 --tien-trinh 8 --ra task-lease.json
"""

import argparse
import asyncio
import json
import random
import statistics
import subprocess
import time
import uuid
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import httpx

G = "http://localhost:8080"
TASK = "http://localhost:8103"
PW = "Matkhau@123"
KET_QUA = Path(__file__).resolve().parent / "ket-qua"


def sql(q: str) -> str:
    return subprocess.run(["docker", "exec", "datn-db-task", "psql", "-U", "task_user", "-d", "task_db", "-tAc", q],
                          capture_output=True, text=True).stdout.strip()


async def dang_nhap(c: httpx.AsyncClient, email: str) -> str:
    r = await c.post(G + "/auth/login", json={"email": email, "password": PW})
    r.raise_for_status()
    return r.json()["accessToken"]


async def chuan_bi(c: httpx.AsyncClient, so_labeler: int, so_mau: int) -> tuple[str, list[str]]:
    biz = {"Authorization": "Bearer " + await dang_nhap(c, "doanhnghiep1@crowd.local")}
    admin = {"Authorization": "Bearer " + await dang_nhap(c, "admin@crowd.local")}

    # Nap du tien ky quy: so mau x 3 x (100 + 30).
    can = so_mau * 3 * 130 + 100000
    r = await c.post(G + "/payments/deposits", headers=dict(biz, **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": can})
    d = r.json()
    await c.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay", params={"amount": can})

    r = await c.post(G + "/projects", headers=biz, json={"name": "[bench] Lay task", "description": "b06", "modality": "text", "visibility": "public"})
    pid = r.json()["id"]
    await c.put(G + f"/projects/{pid}/label-schema", headers=biz, json={"modality": "text", "tools": [
        {"name": "loai", "kind": "classification", "classes": ["a", "b"]}]})
    await c.put(G + f"/projects/{pid}/guideline", headers=biz, json={"markdown": "bench", "examples": []})
    await c.put(G + f"/projects/{pid}/pricing", headers=biz, json={"unitPriceVnd": 100, "redundancy": 3, "maxRedundancy": 3,
                                                                    "budgetVnd": so_mau * 3 * 130, "deadline": "2027-12-31T00:00:00Z"})
    await c.put(G + f"/projects/{pid}/quality-control", headers=biz, json={"goldCheckPercent": 0})
    for lo in range(so_mau // 1000):
        rows = [{"text": f"mau {lo}-{i} {uuid.uuid4().hex[:8]}", "name": f"{lo}-{i}"} for i in range(1000)]
        (await c.post(G + f"/projects/{pid}/datasets/manifest", headers=biz, json={"name": f"lo{lo}", "rows": rows})).raise_for_status()
    for _ in range(300):
        ds = (await c.get(G + f"/projects/{pid}/datasets", headers=biz)).json()
        if len(ds) == so_mau // 1000 and all(x["status"] == "ready" for x in ds):
            break
        await asyncio.sleep(2)
    (await c.post(G + f"/projects/{pid}/publish", headers=biz)).raise_for_status()
    for _ in range(60):
        st = (await c.get(G + f"/projects/{pid}", headers=biz)).json()["status"]
        if st == "pendingApproval":
            await c.post(G + f"/projects/{pid}/approve", headers=admin)
        if st == "running":
            break
        await asyncio.sleep(1)

    # Labeler: dang ky song song (PBKDF2 ton CPU — gioi han 16 request cung luc).
    sem = asyncio.Semaphore(16)
    tag = uuid.uuid4().hex[:6]

    async def mot(i: int) -> str:
        async with sem:
            email = f"bench-{tag}-{i}@crowd.local"
            (await c.post(G + "/auth/register", json={"email": email, "password": PW, "displayName": f"b{i}", "roles": ["labeler"]})).raise_for_status()
            tok = await dang_nhap(c, email)
            (await c.post(G + f"/projects/{pid}/join", headers={"Authorization": "Bearer " + tok})).raise_for_status()
            return tok

    tokens = await asyncio.gather(*[mot(i) for i in range(so_labeler)])
    n_task = so_mau
    for _ in range(120):
        if sql(f"select count(*) from tasks where project_id='{pid}'") == str(n_task) and \
                int(sql(f"select count(*) from project_members_cache where project_id='{pid}'") or 0) >= so_labeler:
            break
        await asyncio.sleep(2)
    return pid, list(tokens)


async def _phan_tai(pid: str, tokens: list[str], t_dung: float) -> dict:
    """Mot tien trinh con: chay cac labeler duoc chia, tra so do THO."""
    lay: list[float] = []
    nop: list[float] = []
    loi: dict[str, int] = {}
    het_task = 0
    limits = httpx.Limits(max_connections=len(tokens) + 10, max_keepalive_connections=len(tokens) + 10)

    async with httpx.AsyncClient(timeout=60, limits=limits) as c:
        async def labeler(tok: str) -> None:
            nonlocal het_task
            h = {"Authorization": "Bearer " + tok}
            while time.time() < t_dung:
                t0 = time.perf_counter()
                r = await c.post(f"{TASK}/tasks/projects/{pid}/next", headers=h)
                lay.append(time.perf_counter() - t0)
                if r.status_code == 204:
                    het_task += 1
                    await asyncio.sleep(0.2)
                    continue
                if r.status_code != 200:
                    loi[f"next {r.status_code}"] = loi.get(f"next {r.status_code}", 0) + 1
                    await asyncio.sleep(0.1)
                    continue
                aid = r.json()["assignmentId"]
                t0 = time.perf_counter()
                s = await c.post(f"{TASK}/tasks/assignments/{aid}/submit", headers=h,
                                 json={"payload": {"loai": {"labelIds": [random.choice(["a", "b"])]}}})
                nop.append(time.perf_counter() - t0)
                if s.status_code != 200:
                    loi[f"submit {s.status_code}"] = loi.get(f"submit {s.status_code}", 0) + 1

        await asyncio.gather(*[labeler(t) for t in tokens])

    return {"lay": lay, "nop": nop, "loi": loi, "het_task": het_task}


def _tien_trinh_con(pid: str, tokens: list[str], t_dung: float) -> dict:
    t_cpu = time.process_time()
    kq = asyncio.run(_phan_tai(pid, tokens, t_dung))
    kq["cpu_giay"] = time.process_time() - t_cpu
    return kq


async def chay_muc(pid: str, tokens: list[str], so: int, giay: int, so_tien_trinh: int) -> dict:
    """
    Chia `so` labeler cho nhieu TIEN TRINH, moi tien trinh mot event loop + client rieng.

    Ban dau chay ca trong MOT tien trinh: chan doan cho thay tien trinh do dung 100% mot
    nhan CPU, con task-svc chi ~27% va Postgres gan nhu ranh — tran ~20 luot/giay do la
    tran cua CHINH bo sinh tai. Nay ghi lai CPU cua tung tien trinh con (cpu_client_max)
    de chung minh client khong con bao hoa.
    """
    n = max(1, min(so_tien_trinh, so))
    phan = [tokens[:so][i::n] for i in range(n)]
    t_dung = time.time() + 3 + giay  # 3 giay cho tien trinh con khoi dong (spawn tren Windows)
    loop = asyncio.get_running_loop()
    with ProcessPoolExecutor(max_workers=n) as ex:
        kqs = await asyncio.gather(*[loop.run_in_executor(ex, _tien_trinh_con, pid, ph, t_dung) for ph in phan])

    lay = [x for k in kqs for x in k["lay"]]
    nop = [x for k in kqs for x in k["nop"]]
    loi: dict[str, int] = {}
    for k in kqs:
        for ten, v in k["loi"].items():
            loi[ten] = loi.get(ten, 0) + v

    def pct(xs: list[float], p: float) -> float | None:
        if not xs:
            return None
        xs = sorted(xs)
        return xs[min(len(xs) - 1, int(p / 100 * len(xs)))] * 1000

    return {
        "dong_thoi": so, "giay": giay, "nop": len(nop), "thong_luong": round(len(nop) / giay, 1),
        "lay_p50_ms": pct(lay, 50), "lay_p95_ms": pct(lay, 95), "lay_p99_ms": pct(lay, 99),
        "nop_p50_ms": pct(nop, 50), "nop_p99_ms": pct(nop, 99), "het_task": sum(k["het_task"] for k in kqs), "loi": loi,
        "tien_trinh": n, "cpu_client_max": round(max(k["cpu_giay"] for k in kqs) / giay, 2),
    }


def kiem_dung(pid: str) -> dict:
    """H3: khong (task, labeler) nao co hai luot; khong task nao nop vuot redundancy."""
    trung = sql(f"select count(*) from (select a.task_id, a.labeler_id from assignments a join tasks t on t.id=a.task_id "
                f"where t.project_id='{pid}' group by 1,2 having count(*) > 1) x")
    vuot = sql(f"select count(*) from tasks where project_id='{pid}' and submitted_count > redundancy_target")
    tong = sql(f"select count(*) from assignments a join tasks t on t.id=a.task_id where t.project_id='{pid}' and a.state='Submitted'")
    return {"cap_trung": int(trung or 0), "vuot_redundancy": int(vuot or 0), "tong_luot_nop": int(tong or 0)}


async def main_async(muc: list[int], giay: int, so_mau: int, so_tien_trinh: int, ten: str) -> None:
    KET_QUA.mkdir(exist_ok=True)
    async with httpx.AsyncClient(timeout=120) as c:
        pid, tokens = await chuan_bi(c, max(muc), so_mau)
    print("du an", pid, "labeler", len(tokens), flush=True)
    kq = []
    for so in muc:
        r = await chay_muc(pid, tokens, so, giay, so_tien_trinh)
        print(f"  {so} labeler: {r['thong_luong']} nop/s, lay p50 {r['lay_p50_ms']:.0f}ms p99 {r['lay_p99_ms']:.0f}ms, "
              f"nop p50 {r['nop_p50_ms']:.0f}ms, cpu client max {r['cpu_client_max']}, loi {r['loi']}", flush=True)
        kq.append(r)
        await asyncio.sleep(5)
    dung = kiem_dung(pid)
    print("kiem tra dung:", dung)
    (KET_QUA / ten).write_text(json.dumps({"du_an": pid, "muc": kq, "kiem_dung": dung}, indent=1), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--muc", default="10,50,100,200,400")
    ap.add_argument("--giay", type=int, default=30)
    ap.add_argument("--so-mau", type=int, default=20000)
    ap.add_argument("--tien-trinh", type=int, default=8, help="so tien trinh sinh tai (1 = cach do cu)")
    ap.add_argument("--ra", default="task-lease.json", help="ten file ket qua trong ket-qua/")
    a = ap.parse_args()
    asyncio.run(main_async([int(x) for x in a.muc.split(",")], a.giay, a.so_mau, a.tien_trinh, a.ra))


if __name__ == "__main__":
    main()
