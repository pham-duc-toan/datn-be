# E2E DOC CHU DU LIEU QUA gRPC THAY BAN SAO (kien truc 3.24) — tu tao tai khoan + du an moi moi lan chay.
#
# Phu:
#   [1] mo bai hoi identity qua gRPC (khong con labeler_cache): doi uy tin trong identity_db la co hieu luc NGAY
#       o lan mo bai sau (truoc day phai cho reputation.changed toi project-svc); identity tat → 503 (chi DEMO)
#   [2] ket qua giao doanh nghiep: quality-svc gop qua gRPC, project-svc giu bang dem sample_results — lan dau
#       tinh moi, lan sau dung lai; quality tat: mau da co trong bang dem van xem duoc, mau phai tinh lai → 503,
#       quality chay lai → 200 (chi DEMO)
#   [3] cap do / bang xep hang: so cau duoc chap nhan doc qua gRPC tu project.attempts (identity khong con bo dem),
#       hang cho level_dirty duoc xu ly het
import os
import subprocess
import sys
import time

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import TIEN_TO_CONTAINER  # noqa: E402
from tro_giup import (  # noqa: E402
    G, GOC, KQ, admin, bat_dau, cho, cho_cham, dang_ky, dat, dat_va_cho, ket_thuc_som, kiem, lam_het, publish_va_duyet,
    sql, tao_du_an, tham_gia,
)
from e2e_cong_cu_helpers import nap  # noqa: E402

DEMO = os.environ.get("E2E_PROFILE", "dev").lower() == "demo"


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def dat_uy_tin(u, uy_tin):
    """Ghi uy tin vao identity_db (nhu sau reputation.changed). project-svc doc qua gRPC luc mo bai."""
    sql("identity", "insert into labeler_stats(user_id, reputation, reputation_at, level, updated_at) "
                    f"values ('{u['id']}', {uy_tin}, now(), 1, now()) "
                    f"on conflict (user_id) do update set reputation = {uy_tin}, reputation_at = now()")


def ket_qua(chu, pid):
    return requests.get(G + f"/annotations/projects/{pid}/results", headers=chu["h"])


def dem(r):
    """Header X-Results-Cache: "hits=2; misses=0" → (2, 0)."""
    phan = dict(x.strip().split("=") for x in r.headers.get("X-Results-Cache", "hits=-1; misses=-1").split(";"))
    return int(phan["hits"]), int(phan["misses"])


def tam_dung(ten):
    subprocess.run(["docker", "stop", f"{TIEN_TO_CONTAINER}{ten}"], check=True, capture_output=True)


def chay_lai(ten):
    subprocess.run(["docker", "start", f"{TIEN_TO_CONTAINER}{ten}"], check=True, capture_output=True)


try:
    dat_va_cho("ledger.reward_hold", 0, ["ledger"])
    biz = dang_ky(["business"], "grpc-biz")
    nap(biz, 2_000_000)

    # =================================================================================
    print("\n[1] Mo bai: dieu kien uy tin hoi identity qua gRPC (khong ban sao)")
    P, _ = tao_du_an(biz, "GRPC-UYTIN", {"u1": "Mot", "u2": "Hai"}, thuong=10000, suat=3,
                     dieu_kien={"minLevel": None, "minReputation": 50, "requireEntranceTest": False,
                                "entranceQuestionCount": 1, "entrancePassPercent": 80})
    publish_va_duyet(biz, P)
    lab = dang_ky(["labeler"], "grpc-uytin")
    dat_uy_tin(lab, 80)
    r = requests.post(G + f"/projects/{P}/join", headers=lab["h"])
    kiem("uy tin 80 >= 50 → vao du an", r.status_code in (200, 201), r.text)

    dat_uy_tin(lab, 10)
    r = requests.post(G + f"/tasks/projects/{P}/attempt", headers=lab["h"])
    kiem("vua tut uy tin (ghi thang identity_db) → mo bai bi chan NGAY: 403 chua_du_uy_tin",
         r.status_code == 403 and code(r) == "chua_du_uy_tin", r.text)
    kiem("project_db khong con bang labeler_cache",
         sql("project", "select count(*) from information_schema.tables where table_name='labeler_cache'") == "0")

    dat_uy_tin(lab, 80)
    b = bat_dau(lab, P)
    kiem("uy tin len lai 80 → mo duoc bai", isinstance(b, dict) and "attemptId" in b, getattr(b, "text", b))

    if DEMO:
        lab2 = dang_ky(["labeler"], "grpc-uytin2")
        dat_uy_tin(lab2, 80)
        requests.post(G + f"/projects/{P}/join", headers=lab2["h"]).raise_for_status()
        tam_dung("identity")
        try:
            r = requests.post(G + f"/tasks/projects/{P}/attempt", headers=lab2["h"])
            kiem("identity tat → mo bai 503 dich_vu_khong_san_sang (khong doan dieu kien)",
                 r.status_code == 503 and code(r) == "dich_vu_khong_san_sang", f"{r.status_code} {r.text[:200]}")
            r = requests.get(G + f"/tasks/attempts/{b['attemptId']}", headers=lab["h"]) if isinstance(b, dict) else None
            kiem("identity tat → bai dang lam van mo duoc", r is not None and r.status_code == 200, r.text[:200] if r is not None else b)
        finally:
            chay_lai("identity")
        kiem("identity chay lai → mo bai duoc",
             cho(lambda: isinstance(bat_dau(lab2, P, 2), dict), 90))

    # =================================================================================
    print("\n[2] Ket qua giao doanh nghiep: quality gop qua gRPC, project giu bang dem")
    Q, mau_q = tao_du_an(biz, "GRPC-GOP", {"q1": "Mot", "q2": "Hai"}, thuong=10000, suat=3)
    publish_va_duyet(biz, Q)
    ba = [dang_ky(["labeler"], f"grpc-gop{i}") for i in range(3)]
    for u in ba:
        tham_gia(u, Q)
        lam_het(u, Q, lambda _: "a")
    # Du suat (he so nguoi lam = 1) thi da tu dong thu bai; con nhan bai thi ket thuc som.
    if requests.get(G + f"/projects/{Q}", headers=biz["h"]).json()["status"] == "running":
        ket_thuc_som(biz, Q)
    kiem("ba bai giong nhau → dat", cho_cham(ba[0], Q, 90) and cho_cham(ba[0], Q)["passed"] is True)
    cho(lambda: ket_qua(biz, Q).status_code == 200 and ket_qua(biz, Q).json()["sampleCount"] == 2, 60)
    k1 = ket_qua(biz, Q).json()
    kiem("ket qua: 2 mau, dong thuan 'a' (gop o quality, dung hinh dang cu: method / votes / kind)",
         k1["sampleCount"] == 2 and all(m["consensusStatus"] == "agreed" for m in k1["samples"])
         and all(m["tools"]["nhan"]["method"] == "majority" and m["tools"]["nhan"]["votes"] == {"a": 3}
                 and m["tools"]["nhan"]["kind"] == "classification" for m in k1["samples"]), k1)
    r2 = ket_qua(biz, Q)
    kiem("lan xem thu hai dung lai bang dem (khong goi quality)", dem(r2) == (2, 0), r2.headers.get("X-Results-Cache"))

    if DEMO:
        tam_dung("quality")
        try:
            r = ket_qua(biz, Q)
            kiem("quality tat → mau da co trong bang dem van xem duoc",
                 r.status_code == 200 and dem(r) == (2, 0), f"{r.status_code} {r.headers.get('X-Results-Cache')} {r.text[:200]}")
            # Doi nguong (thanh phan dau van tay) → moi mau phai tinh lai → can quality.
            GOC.setdefault("quality.consensus_min_f1", requests.get(G + "/admin/settings/quality.consensus_min_f1", headers=admin).json()["value"])
            dat("quality.consensus_min_f1", 0.75)
            kiem("quality tat + mau phai tinh lai → 503 dich_vu_khong_san_sang",
                 cho(lambda: (lambda x: x.status_code == 503 and code(x) == "dich_vu_khong_san_sang")(ket_qua(biz, Q)), 30),
                 ket_qua(biz, Q).text[:200])
        finally:
            chay_lai("quality")
        kiem("quality chay lai → tinh lai ca 2 mau",
             cho(lambda: (lambda x: x.status_code == 200 and x.json()["sampleCount"] == 2)(ket_qua(biz, Q)), 90),
             ket_qua(biz, Q).text[:200])

    # =================================================================================
    print("\n[3] Cap do / xep hang: so cau duoc chap nhan doc qua gRPC tu project.attempts")
    kiem("identity khong con bo dem approved_count / labeler_daily_stats",
         sql("identity", "select count(*) from information_schema.columns where table_name='labeler_stats' and column_name='approved_count'") == "0"
         and sql("identity", "select count(*) from information_schema.tables where table_name='labeler_daily_stats'") == "0")
    u = ba[0]
    tong = sql("project", "select coalesce(sum(answered_count), 0) from attempts "
                          f"where labeler_id = '{u['id']}' and (passed = true or appeal_state = 'Accepted')")
    lv = cho(lambda: (lambda j: j if j.get("approvedCount") == int(tong) else None)(
        requests.get(G + "/me/level", headers=u["h"]).json()), 40)
    kiem(f"/me/level approvedCount = tong answered_count bai dat tren project ({tong})", lv is not None,
         requests.get(G + "/me/level", headers=u["h"]).json())
    kiem("hang cho level_dirty duoc xu ly het",
         cho(lambda: sql("identity", f"select count(*) from level_dirty where user_id = '{u['id']}'") == "0", 40))
    bxh = requests.get(G + "/users/leaderboard", headers=u["h"], params={"period": "all", "limit": 100})
    kiem("bang xep hang (qua gRPC + bo nho dem) tra 200, sap giam dan",
         bxh.status_code == 200 and [x["approvedCount"] for x in bxh.json()] == sorted([x["approvedCount"] for x in bxh.json()], reverse=True),
         bxh.text[:300])

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e-grpc: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
