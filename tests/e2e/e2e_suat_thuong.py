# E2E SUAT THUONG (kien truc 3.38): KHONG gioi han nguoi lam; n suat thuong trao cho n bai DAT giong da so NHAT
# (cung % thi bai nop som hon). Thu bai chi dong khi het han chot hoac chu du an ket thuc som.
#
# Phu: han chot vuot project.max_duration_days → 400; readiness khong con he so nguoi lam; labeler chi thay so suat
# (khong thay so nguoi lam); 5 bai nop vao du an 3 suat → van nhan bai; ket thuc som → cham: l4 nop CUOI nhung giong
# da so 100% van duoc thuong, l3 nop som hon nhung chi 80% thi khong (luat cu "nop som nhat" se cho l3, bo l4);
# du an chot sau han khieu nai, ky quy het.
import os
import sys
import time
import uuid

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import dat_cau_hoi_cong_cu  # noqa: E402
from tro_giup import G, GOC, HAN_CHOT, KQ, TAG, admin, cho, dang_ky, dat_va_cho, khong_tu_dong, kiem, luu, nap_mau, nop_bai, tham_gia, vi  # noqa: E402,F401


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
    dat_va_cho("task.appeal_window", 10, ["project"])
    dat_va_cho("task.reaper_interval", 2, ["project"])
    dat_va_cho("ledger.reward_hold", 0, ["ledger"])

    biz = dang_ky(["business"], "suat-biz")
    lab = {k: dang_ky(["labeler"], "suat-" + k) for k in ("l1", "l2", "l3", "l4", "l5")}
    nap(biz, 2_000_000)

    print("\n[1] Du an 3 suat thuong, han chot bat buoc trong gioi han")
    MAU = {"s1": "Tuyet voi", "s2": "Qua te", "s3": "Rat hai long", "s4": "Te hai", "s5": "On"}
    DUNG = {"Tuyet voi": "a", "Qua te": "b", "Rat hai long": "a", "Te hai": "b", "On": "a"}
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-suat] {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
    P = r.json()["id"]
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]}]}}).raise_for_status()
    dat_cau_hoi_cong_cu(biz["h"], P)
    xa = time.strftime("%Y-%m-%dT00:00:00Z", time.gmtime(time.time() + 400 * 86400))
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"pricing": {"rewardVnd": 30000, "slots": 3, "passThresholdPercent": 60,
                                                                          "deadline": xa}})
    kiem("han chot qua 30 ngay → 400 han_chot_qua_xa", r.status_code == 400 and r.json().get("code") == "han_chot_qua_xa", r.text)
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"pricing": {"rewardVnd": 30000, "slots": 3, "passThresholdPercent": 60,
                                                                      "deadline": HAN_CHOT}}).raise_for_status()
    nap_mau(P, biz["h"], [{"text": v, "name": k} for k, v in MAU.items()])
    rdy = requests.get(G + f"/projects/{P}/readiness", headers=biz["h"]).json()
    kiem("readiness: co han chot toi da, khong con he so nguoi lam",
         rdy["limits"].get("maxDurationDays", 0) >= 20 and "participantMultiplier" not in rdy["limits"], rdy["limits"])
    requests.post(G + f"/projects/{P}/publish", headers=biz["h"]).raise_for_status()
    khong_tu_dong(P)  # bo nay kiem dung hanh vi moi: nop bao nhieu bai thu bai van mo
    assert cho(lambda: requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "pendingApproval", 40)
    requests.post(G + f"/projects/{P}/approve", headers=admin).raise_for_status()
    assert cho(lambda: requests.get(G + f"/tasks/projects/{P}/slots", headers=biz["h"]).status_code == 200, 40)
    tham_gia(lab["l1"], P)
    s = requests.get(G + f"/tasks/projects/{P}/slots", headers=lab["l1"]["h"]).json()
    kiem("labeler thay 3 suat thuong va han chot, KHONG thay so nguoi lam",
         s["slots"] == 3 and s["deadline"] and not {"submitted", "inProgress", "available", "maxParticipants"} & set(s), s)

    print("\n[2] 5 nguoi nop vao du an 3 suat — khong gioi han nguoi lam, du an van mo")
    # l5 sai het (0%); l2, l3 moi nguoi sai 1 cau khac nhau (80%); l1, l4 dung het (100%). Thu tu nop: l5 l2 l3 l1 l4.
    SAI = {"l5": set(MAU.values()), "l2": {"Tuyet voi"}, "l3": {"Qua te"}, "l1": set(), "l4": set()}
    for k in ("l5", "l2", "l3", "l1", "l4"):
        u = lab[k]
        tham_gia(u, P)
        r = bat_dau(u, P)
        assert r.status_code in (200, 201), (k, r.text)
        bai = r.json()
        for q in bai["questions"]:
            text = q["content"]["text"]
            dung = DUNG[text]
            lop = ("b" if dung == "a" else "a") if text in SAI[k] else dung
            luu(u, bai["attemptId"], q["sampleId"], {"nhan": {"labelIds": [lop]}})
        r = nop_bai(u, bai["attemptId"])
        kiem(f"{k} nop bai", r.status_code == 200 and r.json()["collectionFinished"] is False, r.text)
        time.sleep(0.3)  # thu tu nop ro rang
    td = requests.get(G + f"/tasks/projects/{P}/progress", headers=biz["h"]).json()
    kiem("5 bai nop > 3 suat → van nhan bai (chu du an thay so lieu, du kien dong luc han chot)",
         td["phase"] == "open" and td["submitted"] == 5 and td["estimatedCompletionAt"], td)

    print("\n[3] Ket thuc som → cham: 3 bai dat GIONG DA SO NHAT duoc thuong")
    requests.post(G + f"/projects/{P}/finish", headers=biz["h"]).raise_for_status()
    assert cho(lambda: (bai_cua_toi(lab["l4"], P) or {}).get("passed") is not None, 90), bai_cua_toi(lab["l4"], P)
    kq = {k: bai_cua_toi(u, P) for k, u in lab.items()}
    kiem("l1, l4 giong da so 100%; l2, l3 80%", [kq[k]["agreementPercent"] for k in ("l1", "l4", "l2", "l3")] == [100, 100, 80, 80],
         {k: kq[k]["agreementPercent"] for k in kq})
    kiem("l4 nop CUOI nhung 100% → duoc thuong", kq["l4"]["finalPassed"] is True and kq["l4"]["rewarded"] is True, kq["l4"])
    kiem("l1 100% → duoc thuong", kq["l1"]["rewarded"] is True, kq["l1"])
    kiem("l2, l3 cung 80% → l2 nop som hon duoc thuong", kq["l2"]["rewarded"] is True, kq["l2"])
    kiem("l3 dat (80%) nhung khong lot suat", kq["l3"]["finalPassed"] is True and kq["l3"]["rewarded"] is False, kq["l3"])
    kiem("l5 truot", kq["l5"]["finalPassed"] is False and kq["l5"]["rewarded"] is False, kq["l5"])
    kiem("l1, l2, l4 nhan 30.000; l3, l5 khong",
         cho(lambda: all(vi(lab[k]).get("availableVnd", 0) == 30000 for k in ("l1", "l2", "l4")), 30)
         and all(vi(lab[k]).get("availableVnd", 0) == 0 for k in ("l3", "l5")),
         {k: vi(u) for k, u in lab.items()})
    td = requests.get(G + f"/tasks/projects/{P}/progress", headers=biz["h"]).json()
    kiem("ly do dong thu bai: ket_thuc_som", td["finishReason"] == "ket_thuc_som", td)

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
