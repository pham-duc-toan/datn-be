# E2E SUAT THUONG (10/2026): so suat n = so nguoi DUOC THUONG — n bai DAT nop som nhat; tran nguoi lam = n x he so.
#
# Phu: tran nguoi lam = 3 suat x 2 = 6 (chua dong thu bai khi 5 bai nop), ket thuc som → cham da so tren CA 5 bai,
# 3 bai dat nop som nhat duoc thuong (tien vao vi), bai dat thu 4 "het suat thuong" (khong tien), bai truot khong
# tien; du an chot sau han khieu nai, ky quy het (ca 3 suat da tra).
import os
import sys
import time
import uuid

os.environ["E2E_HE_SO_NGUOI_LAM"] = "giu"  # tro_giup khong ep he so = 1

import requests  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import G, GOC, KQ, TAG, admin, cho, dang_ky, dat, dat_va_cho, kiem, tham_gia, vi  # noqa: E402,F401


def nap(biz, so):
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": "e2e-" + uuid.uuid4().hex}), json={"amountVnd": so})
    r.raise_for_status()
    requests.post(G + f"/payments/sandbox/checkout/{r.json()['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(biz)["businessAvailableVnd"] >= so, 30), vi(biz)


def bat_dau(u, pid, giay=20):
    het = time.time() + giay
    while True:
        r = requests.post(G + f"/tasks/projects/{pid}/attempt", headers=u["h"])
        if r.status_code in (200, 201) or time.time() > het:
            return r
        time.sleep(0.5)


def bai_cua_toi(u, pid):
    ds = requests.get(G + f"/tasks/me/attempts?projectId={pid}", headers=u["h"]).json()
    ds = ds if isinstance(ds, list) else ds.get("items", [])
    return next((b for b in ds if b["projectId"] == pid), None)


try:
    dat_va_cho("project.participant_multiplier", 2, ["task"])
    dat_va_cho("task.appeal_window", 10, ["task"])
    dat_va_cho("task.reaper_interval", 2, ["task"])
    dat_va_cho("ledger.reward_hold", 0, ["ledger"])

    biz = dang_ky(["business"], "suat-biz")
    lab = {k: dang_ky(["labeler"], "suat-" + k) for k in ("l1", "l2", "l3", "l4", "l5")}
    nap(biz, 2_000_000)

    print("\n[1] Du an 3 suat thuong, he so 2 → toi da 6 nguoi lam")
    MAU = {"s1": "Tuyet voi", "s2": "Qua te", "s3": "Rat hai long"}
    DUNG = {"Tuyet voi": "a", "Qua te": "b", "Rat hai long": "a"}
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-suat] {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
    P = r.json()["id"]
    requests.put(G + f"/projects/{P}/label-schema", headers=biz["h"], json={"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]}]}).raise_for_status()
    requests.put(G + f"/projects/{P}/guideline", headers=biz["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    requests.put(G + f"/projects/{P}/pricing", headers=biz["h"], json={"rewardVnd": 30000, "slots": 3, "passThresholdPercent": 60,
                                                                      "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    requests.post(G + f"/projects/{P}/datasets/manifest", headers=biz["h"],
                  json={"name": "lo", "rows": [{"text": v, "name": k} for k, v in MAU.items()]}).raise_for_status()
    assert cho(lambda: [d for d in requests.get(G + f"/projects/{P}/datasets", headers=biz["h"]).json() if d["status"] == "ready"], 60)
    rdy = requests.get(G + f"/projects/{P}/readiness", headers=biz["h"]).json()
    kiem("readiness bao he so tran nguoi lam = 2", rdy["limits"]["participantMultiplier"] == 2, rdy["limits"])
    requests.post(G + f"/projects/{P}/publish", headers=biz["h"]).raise_for_status()
    assert cho(lambda: requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "pendingApproval", 40)
    requests.post(G + f"/projects/{P}/approve", headers=admin).raise_for_status()
    assert cho(lambda: requests.get(G + f"/tasks/projects/{P}/slots", headers=biz["h"]).status_code == 200, 40)
    s = requests.get(G + f"/tasks/projects/{P}/slots", headers=biz["h"]).json()
    kiem("slots: 3 suat thuong, 6 cho lam", s["slots"] == 3 and s["maxParticipants"] == 6 and s["available"] == 6, s)

    print("\n[2] 5 nguoi nop (l5 sai het, nop truoc) — chua du tran, du an van mo")
    for k in ("l5", "l1", "l2", "l3", "l4"):
        u = lab[k]
        tham_gia(u, P)
        r = bat_dau(u, P)
        assert r.status_code in (200, 201), (k, r.text)
        bai = r.json()
        for q in bai["questions"]:
            dung = DUNG[q["content"]["text"]]
            lop = ("b" if dung == "a" else "a") if k == "l5" else dung
            requests.put(G + f"/tasks/attempts/{bai['attemptId']}/answers/{q['sampleId']}", headers=u["h"],
                         json={"data": {"nhan": {"labelIds": [lop]}}}).raise_for_status()
        r = requests.post(G + f"/tasks/attempts/{bai['attemptId']}/submit", headers=u["h"])
        kiem(f"{k} nop bai", r.status_code == 200, r.text)
        time.sleep(0.3)  # thu tu nop ro rang
    s = requests.get(G + f"/tasks/projects/{P}/slots", headers=biz["h"]).json()
    kiem("5/6 bai nop → van nhan bai (con 1 cho)", s["phase"] == "open" and s["submitted"] == 5 and s["available"] == 1, s)

    print("\n[3] Ket thuc som → cham: 3 bai dat nop som nhat duoc thuong")
    requests.post(G + f"/projects/{P}/finish", headers=biz["h"]).raise_for_status()
    assert cho(lambda: (bai_cua_toi(lab["l4"], P) or {}).get("passed") is not None, 90), bai_cua_toi(lab["l4"], P)
    kq = {k: bai_cua_toi(u, P) for k, u in lab.items()}
    for k in ("l1", "l2", "l3"):
        kiem(f"{k} dat va duoc thuong", kq[k]["finalPassed"] is True and kq[k]["rewarded"] is True, kq[k])
    kiem("l4 dat nhung het suat thuong", kq["l4"]["finalPassed"] is True and kq["l4"]["rewarded"] is False, kq["l4"])
    kiem("l5 truot", kq["l5"]["finalPassed"] is False and kq["l5"]["rewarded"] is False, kq["l5"])
    kiem("l1, l2, l3 nhan 30.000; l4, l5 khong",
         cho(lambda: all(vi(lab[k]).get("availableVnd", 0) == 30000 for k in ("l1", "l2", "l3")), 30)
         and all(vi(lab[k]).get("availableVnd", 0) == 0 for k in ("l4", "l5")),
         {k: vi(u) for k, u in lab.items()})

    print("\n[4] Het han khieu nai → du an hoan thanh, ky quy het (3 suat da tra)")
    ok = cho(lambda: requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "completed", 90)
    kiem("du an Completed", ok, requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"])
    kiem("ky quy ve 0", cho(lambda: vi(biz)["escrowVnd"] == 0, 30), vi(biz))
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    KQ["fail"] += 1
    KQ["loi"].append("ngoai le — xem traceback")
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
sys.exit(1 if KQ["fail"] else 0)
