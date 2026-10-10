"""NC-B-06 / VD-M-08 — chi tien luot vuot link: tung luot (perClick) vs gop lo (batched).

GIA THUYET
  H1: perClick bao hoa som — moi luot mot transaction duoi KHOA SO CAI toan cuc (+ chuoi bam
      but toan), nen thong luong bi chan o 1 / (thoi gian mot transaction) bat ke tai.
  H2: batched day duoc diem bao hoa len nhieu lan: duong nong chi con INSERT mot dong, but
      toan viet mot lan cho ca lo. Doi lai, do tre tien vao vi tang them ~ chu ky gop.

MOI TRUONG CO LAP (khong dung du lieu dev):
  vhost RabbitMQ "bench", database "ledger_bench", MOT instance ledger-svc rieng (cong 8195).
  Chuoi khoi tao di DUNG duong event that: deposit.confirmed → project.publish_requested →
  project.published — ledger tu ghi so, khong chen tay vao bang nao (tru setting).

DO
  - do tre moi luot: luc tien vao vi (holds.held_at / gate_clicks.settled_at) − luc gate xac nhan;
  - thong luong: so luot chi xong / giay trong khoang tai;
  - do sau hang doi ledger-svc.click-validated (RabbitMQ management API) moi 0.5 giay;
  - so phien Postgres dang cho KHOA (pg_stat_activity.wait_event_type = 'Lock').

Chay:  ../.venv/Scripts/python ledger_cpm.py --mode perClick --mode batched
"""

import argparse
import asyncio
import json
import os
import random
import statistics
import subprocess
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import aio_pika
import httpx
import psycopg

GOC = Path(__file__).resolve().parents[2]
KET_QUA = Path(__file__).resolve().parent / "ket-qua"
VHOST = "bench"
DB = "ledger_bench"
CONG = 8195
PG = f"host=localhost port=5405 dbname={DB} user=ledger_user password=dev_ledger_pw"
RABBIT = f"amqp://datn:dev_rabbit_pw@localhost:5672/{VHOST}"
MGMT = "http://localhost:15672/api"
AUTH = ("datn", "dev_rabbit_pw")


def uuid7() -> uuid.UUID:
    ms = int(time.time() * 1000)
    r = int.from_bytes(os.urandom(10), "big")
    v = (ms & ((1 << 48) - 1)) << 80 | 0x7 << 76 | ((r >> 62) & 0xFFF) << 64 | 0b10 << 62 | (r & ((1 << 62) - 1))
    return uuid.UUID(int=v)


def bay_gio() -> str:
    return datetime.now(timezone.utc).isoformat()


def phong_bi(event_type: str, payload: dict) -> bytes:
    return json.dumps({
        "eventId": str(uuid7()), "eventType": event_type, "version": 1, "occurredAt": bay_gio(),
        "producer": "bench", "correlationId": str(uuid7()), "causationId": None, "actor": None, "payload": payload,
    }).encode()


def sh(cmd: list[str]) -> str:
    return subprocess.run(cmd, capture_output=True, text=True).stdout.strip()


def chuan_bi_moi_truong() -> None:
    sh(["docker", "exec", "datn-rabbitmq", "rabbitmqctl", "add_vhost", VHOST])
    sh(["docker", "exec", "datn-rabbitmq", "rabbitmqctl", "set_permissions", "-p", VHOST, "datn", ".*", ".*", ".*"])
    sh(["docker", "exec", "datn-db-ledger", "psql", "-U", "ledger_user", "-d", "ledger_db", "-c", f"DROP DATABASE IF EXISTS {DB} WITH (FORCE)"])
    sh(["docker", "exec", "datn-db-ledger", "psql", "-U", "ledger_user", "-d", "ledger_db", "-c", f"CREATE DATABASE {DB}"])
    # Xoa queue cu cua vhost bench (tin con sot tu lan chay truoc).
    for q in httpx.get(f"{MGMT}/queues/{VHOST}", auth=AUTH).json():
        httpx.delete(f"{MGMT}/queues/{VHOST}/{q['name']}", auth=AUTH)


def bat_ledger(log: Path, cong: int = CONG) -> subprocess.Popen:
    env = dict(os.environ)
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ConnectionStrings__LedgerDb": f"Host=localhost;Port=5405;Database={DB};Username=ledger_user;Password=dev_ledger_pw;Maximum Pool Size=10",
        "RabbitMq__VirtualHost": VHOST,
        "Seed__Enabled": "false",
        "Logging__LogLevel__Default": "Warning",
    })
    return subprocess.Popen(
        ["dotnet", "run", "--no-build", "--project", str(GOC / "services/ledger/Crowd.Ledger.Api"), "--urls", f"http://localhost:{cong}"],
        stdout=open(log, "w"), stderr=subprocess.STDOUT, env=env)


def dat_setting(conn, key: str, value, version: int) -> None:
    conn.execute(
        "INSERT INTO settings_replica (key, value, version, updated_at) VALUES (%s, %s::jsonb, %s, now()) "
        "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, version = EXCLUDED.version, updated_at = now()",
        (key, json.dumps(value), version))


async def cho(f, giay: float, buoc: float = 0.5):
    het = time.time() + giay
    while time.time() < het:
        v = f()
        if v:
            return v
        await asyncio.sleep(buoc)
    return None


def mot(conn, sql: str, *ts):
    r = conn.execute(sql, ts).fetchone()
    return None if r is None else r[0]


async def chay_che_do(mode: str, toc_do: list[int], giay_moi_muc: int, chu_ky_lo: int, so_instance: int = 1) -> dict:
    chuan_bi_moi_truong()
    procs = [bat_ledger(KET_QUA / f"ledger-{mode}-{i}.log", CONG + i) for i in range(1)]
    try:
        ok = await cho(lambda: _co_bang(), 90, 1)
        if not ok:
            raise RuntimeError("ledger bench khong khoi dong (xem log)")
        conn = psycopg.connect(PG, autocommit=True)
        # Instance thu 2.. bat SAU khi instance dau da migrate xong (tranh hai ben cung migrate).
        for i in range(1, so_instance):
            procs.append(bat_ledger(KET_QUA / f"ledger-{mode}-{i}.log", CONG + i))
        if so_instance > 1:
            await asyncio.sleep(25)
        dat_setting(conn, "ledger.gate_payout_mode", mode, 100)
        dat_setting(conn, "ledger.gate_batch_interval", chu_ky_lo, 100)
        await asyncio.sleep(7)   # settings.reload_interval = 5s

        ket_noi = await aio_pika.connect_robust(RABBIT)
        kenh = await ket_noi.channel(publisher_confirms=False)
        ex = await kenh.declare_exchange("datn.events", aio_pika.ExchangeType.TOPIC, durable=True)

        async def phat(event_type: str, payload: dict) -> None:
            await ex.publish(aio_pika.Message(phong_bi(event_type, payload), content_type="application/json",
                                              delivery_mode=aio_pika.DeliveryMode.PERSISTENT), routing_key=event_type)

        # ---- Khoi tao qua DUNG duong event: nap tien → ky quy → publish ----
        biz, du_an = uuid7(), uuid7()
        ngan_sach = 10 ** 13
        await cho(lambda: mot(conn, "SELECT count(*) FROM pg_tables WHERE tablename='processed_events'") == 1, 30)
        await asyncio.sleep(3)   # consumer khai bao xong queue
        await phat("deposit.confirmed", {"intentId": str(uuid7()), "businessId": str(biz), "amountVnd": ngan_sach,
                                         "provider": "bench", "providerTxnId": "bench-" + uuid7().hex})
        await cho(lambda: mot(conn, "SELECT balance FROM accounts WHERE code=%s", f"business:{biz}:available") == ngan_sach, 30)
        await phat("project.publish_requested", {"projectId": str(du_an), "ownerId": str(biz), "escrowAmountVnd": ngan_sach})
        await cho(lambda: mot(conn, "SELECT count(*) FROM project_escrows WHERE project_id=%s", du_an) == 1, 30)
        await phat("project.published", {
            "projectId": str(du_an), "ownerId": str(biz), "modality": "text",
            "labelSchema": {"modality": "text", "tools": [{"name": "loai", "kind": "classification", "classes": ["a", "b"]}]},
            "unitPriceVnd": 1000, "platformFeeVnd": 300, "redundancy": 1, "maxRedundancy": 1, "goldCheckPercent": 0,
            "deadline": (datetime.now(timezone.utc) + timedelta(days=30)).isoformat(), "allowProfessional": True,
            "allowLinkGateway": True, "allowCollaborative": False, "isPrivate": False, "minLevel": None, "minReputation": None,
            "requireEntranceTest": False, "sampleCount": 1})
        await cho(lambda: mot(conn, "SELECT allow_link_gateway FROM project_escrows WHERE project_id=%s", du_an) is True, 30)

        sharer = [uuid7() for _ in range(2000)]
        link = {s: uuid7() for s in sharer}
        muc = []
        for r in toc_do:
            muc.append(await chay_muc(conn, phat, du_an, sharer, link, r, giay_moi_muc, mode))
            print(f"  {mode} x{so_instance} {r}/s: {muc[-1]['tom_tat']}", flush=True)
        await ket_noi.close()
        conn.close()
        return {"mode": mode, "chu_ky_lo": chu_ky_lo, "so_instance": so_instance, "muc": muc}
    finally:
        for proc in procs:
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True)


def _co_bang() -> bool:
    try:
        with psycopg.connect(PG, autocommit=True, connect_timeout=2) as c:
            return mot(c, "SELECT count(*) FROM pg_tables WHERE tablename IN ('gate_clicks','holds','settings_replica')") == 3
    except psycopg.Error:
        return False


async def chay_muc(conn, phat, du_an, sharer, link, toc_do: int, giay: int, mode: str) -> dict:
    """Tai mo (open-loop): phat dung toc_do luot/giay trong `giay` giay, do toi khi chi het."""
    ids: list[str] = []
    mau_hang_doi: list[tuple[float, int]] = []
    mau_khoa: list[int] = []
    dung = asyncio.Event()

    async def lay_mau() -> None:
        async with httpx.AsyncClient(auth=AUTH, timeout=2) as c:
            while not dung.is_set():
                try:
                    q = (await c.get(f"{MGMT}/queues/{VHOST}/ledger-svc.click-validated")).json()
                    mau_hang_doi.append((time.time(), q.get("messages", 0)))
                except Exception:  # noqa: BLE001
                    pass
                mau_khoa.append(mot(conn, "SELECT count(*) FROM pg_stat_activity WHERE datname=%s AND wait_event_type='Lock'", DB))
                await asyncio.sleep(0.5)

    task_mau = asyncio.create_task(lay_mau())
    t0 = time.time()
    tong = toc_do * giay
    for i in range(tong):
        muc_tieu = t0 + i / toc_do
        cham = muc_tieu - time.time()
        if cham > 0:
            await asyncio.sleep(cham)
        s = random.choice(sharer)
        cid = str(uuid7())
        ids.append(cid)
        await phat("click.validated", {
            "clickId": cid, "linkId": str(link[s]), "sharerId": str(s), "projectId": str(du_an), "labelCount": 2,
            "sharerAmountVnd": 1400, "platformAmountVnd": 1200, "validatedAt": bay_gio()})
    t_phat = time.time() - t0

    bang = "holds" if mode == "perClick" else "gate_clicks"
    cot_id = "annotation_id" if mode == "perClick" else "click_id"
    cot_luc = "held_at" if mode == "perClick" else "settled_at"
    dieu_kien = "kind='GateClick'" if mode == "perClick" else "state='Paid'"
    def da_chi() -> int:
        return mot(conn, f"SELECT count(*) FROM {bang} WHERE {dieu_kien} AND {cot_id} = ANY(%s::uuid[])", ids)

    # Doi chi het (tran 240 giay — qua tran la "khong tieu thu kip", ghi nhan backlog).
    xong = await cho(lambda: da_chi() >= tong, 240, 1)
    t_xong = time.time() - t0
    dung.set()
    await task_mau

    rows = conn.execute(
        f"SELECT EXTRACT(EPOCH FROM ({cot_luc} - validated_at)) FROM {bang} b "
        f"JOIN (SELECT unnest(%s::uuid[]) AS id) x ON x.id = b.{cot_id}" if mode == "batched" else
        "SELECT 0", (ids,) if mode == "batched" else ()).fetchall()
    if mode == "perClick":
        # holds khong luu validated_at: lay tu thoi diem phat (eventId = uuid7 theo thoi gian ≈ validatedAt).
        rows = conn.execute(
            "SELECT EXTRACT(EPOCH FROM (h.held_at - to_timestamp(((('x' || substr(replace(h.annotation_id::text,'-',''),1,12))::bit(48)::bigint) / 1000.0)))) "
            "FROM holds h JOIN (SELECT unnest(%s::uuid[]) AS id) x ON x.id = h.annotation_id", (ids,)).fetchall()
    tre = sorted(float(r[0]) for r in rows if r[0] is not None)

    def pct(p: float) -> float | None:
        return None if not tre else tre[min(len(tre) - 1, int(p / 100 * len(tre)))]

    so_chi = len(tre)
    kq = {
        "toc_do_phat": toc_do, "so_luot": tong, "giay_phat": round(t_phat, 2), "da_chi": so_chi,
        "xong": bool(xong), "giay_toi_khi_chi_het": round(t_xong, 1) if xong else None,
        "thong_luong": round(so_chi / t_xong, 1),
        "tre_p50": pct(50), "tre_p95": pct(95), "tre_p99": pct(99), "tre_max": tre[-1] if tre else None,
        "hang_doi_max": max((m for _, m in mau_hang_doi), default=0),
        "cho_khoa_tb": round(statistics.mean(mau_khoa), 2) if mau_khoa else 0,
        "cho_khoa_max": max(mau_khoa, default=0),
        "hang_doi": [(round(t - t0, 1), m) for t, m in mau_hang_doi],
    }
    kq["tom_tat"] = (f"chi {so_chi}/{tong}, thong luong {kq['thong_luong']}/s, tre p50 {fmt(kq['tre_p50'])} "
                     f"p99 {fmt(kq['tre_p99'])}, hang doi max {kq['hang_doi_max']}, cho khoa tb {kq['cho_khoa_tb']}")
    return kq


def fmt(x):
    return "-" if x is None else f"{x:.2f}s"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", action="append", default=[])
    ap.add_argument("--toc-do", default="50,100,200,400,800")
    ap.add_argument("--giay", type=int, default=20)
    ap.add_argument("--chu-ky-lo", type=int, default=5)
    ap.add_argument("--so-instance", type=int, default=1)
    a = ap.parse_args()
    KET_QUA.mkdir(exist_ok=True)
    toc = [int(x) for x in a.toc_do.split(",")]
    tat_ca = []
    for m in a.mode or ["perClick", "batched"]:
        tat_ca.append(asyncio.run(chay_che_do(m, toc, a.giay, a.chu_ky_lo, a.so_instance)))
    dich = KET_QUA / "ledger-cpm.json"
    cu = json.loads(dich.read_text(encoding="utf-8")) if dich.exists() else {}
    for x in tat_ca:
        cu[f"{x['mode']}-x{x['so_instance']}"] = x
    dich.write_text(json.dumps(cu, indent=1), encoding="utf-8")
    print("xong")


if __name__ == "__main__":
    sys.exit(main())
