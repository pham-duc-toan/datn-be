# E2E DU AN LAM TRUNG TAM (kien truc 3.18) — tu tao tai khoan + du an moi moi lan chay.
#
# Phu: cau hinh gia CA DU AN (thuong / suat / nguong t trong [x, y]), ky quy = suat x (thuong + phi),
# lam bai nhu bai kiem tra (luu nhap, quay lai, bo qua, nop ca bai, bao cau sai dinh dang), het suat,
# ket thuc som, cham mot lan ca du an (da so + cau vang), tra thuong bai dat (vao vi ngay), loai nhan
# bai truot, khieu nai ca bai (admin chap nhan → tra thuong), chot du an sau han khieu nai → hoan ky quy
# phan suat thua, it hon 3 bai → chi xet cau vang, huy du an chua co bai nop → hoan toan bo.
import os
import sys
import time
import uuid

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import (  # noqa: E402
    G, GOC, KQ, TAG, admin, cho, dang_ky, dat, dat_va_cho, kiem, sql, tham_gia, vi,
)

LOP = ("a", "b")


def nap(biz, so):
    k = "e2e-" + uuid.uuid4().hex
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": so})
    r.raise_for_status()
    requests.post(G + f"/payments/sandbox/checkout/{r.json()['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(biz)["businessAvailableVnd"] >= so, 30), vi(biz)


def tao_du_an(chu, ten, mau, thuong, suat, nguong, vang=()):
    """mau: {ten: text}. vang: [(ten, lop_dung)] — cau vang kiem tra (qualityCheck). Tra (pid, {ten: sample_id})."""
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-3.18] {ten} {TAG}", "description": "e2e",
                                                                "modality": "text", "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json={"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": list(LOP)}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    r = requests.put(G + f"/projects/{pid}/pricing", headers=chu["h"], json={
        "rewardVnd": thuong, "slots": suat, "passThresholdPercent": nguong, "deadline": "2027-12-31T00:00:00Z"})
    assert r.status_code == 200, r.text
    requests.post(G + f"/projects/{pid}/datasets/manifest", headers=chu["h"],
                  json={"name": "lo", "rows": [{"text": v, "name": k} for k, v in mau.items()]}).raise_for_status()
    assert cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json() if d["status"] == "ready"], 60)
    ids = {m["originalName"]: m["id"] for m in requests.get(G + f"/projects/{pid}/samples?pageSize=200", headers=chu["h"]).json()["items"]}
    if vang:
        r = requests.post(G + f"/projects/{pid}/gold-items", headers=chu["h"], json={"items": [
            {"sampleId": ids[t], "purpose": "qualityCheck", "expectedPayload": {"nhan": {"labelIds": [d]}}} for t, d in vang]})
        assert r.status_code == 200, r.text
    return pid, ids


def publish_va_duyet(chu, pid):
    r = requests.post(G + f"/projects/{pid}/publish", headers=chu["h"])
    assert r.status_code in (200, 202), r.text
    assert cho(lambda: requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "pendingApproval", 40), \
        requests.get(G + f"/projects/{pid}", headers=chu["h"]).text
    requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()
    assert cho(lambda: requests.get(G + f"/tasks/projects/{pid}/slots", headers=chu["h"]).status_code == 200
               and requests.get(G + f"/tasks/projects/{pid}/slots", headers=chu["h"]).json()["phase"] == "open", 40)


def bat_dau(u, pid, giay=20):
    """POST attempt; 403 ngay sau join (ban sao thanh vien chua toi): thu lai."""
    het = time.time() + giay
    while True:
        r = requests.post(G + f"/tasks/projects/{pid}/attempt", headers=u["h"])
        if r.status_code in (200, 201) or time.time() > het:
            return r
        time.sleep(0.5)


def luu(u, aid, sid, lop):
    data = None if lop is None else {"nhan": {"labelIds": [lop]}}
    return requests.put(G + f"/tasks/attempts/{aid}/answers/{sid}", headers=u["h"], json={"data": data})


def lam_bai(u, bai, dap_an):
    """dap_an(text) → lop | None (bo trong). Luu tung cau THEO THU TU NGUOC (lam nhu bai kiem tra)."""
    for q in reversed(bai["questions"]):
        lop = dap_an(q["content"]["text"])
        if lop is not None:
            luu(u, bai["attemptId"], q["sampleId"], lop).raise_for_status()


def nop(u, aid):
    return requests.post(G + f"/tasks/attempts/{aid}/submit", headers=u["h"])


def bai_cua_toi(u, pid):
    ds = requests.get(G + f"/tasks/me/attempts?projectId={pid}", headers=u["h"]).json()
    ds = ds if isinstance(ds, list) else ds.get("items", [])
    return next((b for b in ds if b["projectId"] == pid), None)


def kha_dung(u):
    return vi(u).get("availableVnd", 0)


def trang_thai(chu, pid):
    return requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"]


try:
    # Han khieu nai + chu ky worker task ngan de chot du an trong luc test.
    dat_va_cho("task.appeal_window", 15, ["task"])
    dat_va_cho("task.reaper_interval", 2, ["task"])
    dat_va_cho("ledger.reward_hold", 0, ["ledger"])

    biz = dang_ky(["business"], "biz")
    lab = {k: dang_ky(["labeler"], k) for k in ("l1", "l2", "l3", "l4")}
    nap(biz, 3_000_000)

    # =================================================================================
    print("\n[1] Cau hinh gia CA DU AN, nguong t trong [x, y], ky quy theo suat")
    MAU = {"s1": "Tuyet voi", "s2": "Qua te", "s3": "Rat hai long", "s4": "That vong", "s5": "On",
           "g1": "Xuat sac", "g2": "Kinh khung"}
    DUNG = {"Tuyet voi": "a", "Qua te": "b", "Rat hai long": "a", "That vong": "b", "On": "a", "Xuat sac": "a", "Kinh khung": "b"}
    A, mau_a = tao_du_an(biz, "Chinh", MAU, 50000, 4, 80, vang=[("g1", "a"), ("g2", "b")])

    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"rewardVnd": 50000, "slots": 4, "passThresholdPercent": 99,
                                                                          "deadline": "2027-12-31T00:00:00Z"})
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    kiem("t = 99 ngoai [60, 95] → bi chan (400 ngay khi luu, hoac readiness bao thieu)",
         (r.status_code == 400 and "nguong" in r.text) or "nguong_dat_ngoai_khoang" in str(rdy["missing"]), (r.status_code, r.text, rdy))
    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"rewardVnd": 5000, "slots": 4, "passThresholdPercent": 80,
                                                                          "deadline": "2027-12-31T00:00:00Z"})
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    kiem("thuong 5.000 < toi thieu 10.000 → bi chan",
         (r.status_code == 400) or "thuong_duoi_toi_thieu" in str(rdy["missing"]), (r.status_code, r.text, rdy))
    requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"rewardVnd": 50000, "slots": 4, "passThresholdPercent": 80,
                                                                      "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    phi = rdy["platformFeePerPassVnd"]
    kiem("readiness: 5 cau that, gioi han x/y tu setting", rdy["questionCount"] == 5 and rdy["limits"]["passThresholdMin"] == 60
         and rdy["limits"]["passThresholdMax"] == 95 and rdy["ready"], rdy)
    kiem("ky quy uoc tinh = 4 suat x (50.000 + phi)", rdy["estimatedCostVnd"] == 4 * (50000 + phi), rdy)
    publish_va_duyet(biz, A)
    ky_quy = 4 * (50000 + phi)
    kiem("vi doanh nghiep tru dung ky quy", cho(lambda: vi(biz)["businessAvailableVnd"] == 3_000_000 - ky_quy, 20), vi(biz))
    p = requests.get(G + f"/projects/{A}", headers=biz["h"]).json()
    kiem("du an luu tien theo CA DU AN (rewardVnd, slots, questionCount)",
         p.get("rewardVnd") == 50000 and p.get("slots") == 4 and p.get("questionCount") == 5, p)

    # =================================================================================
    print("\n[2] Lam bai nhu bai kiem tra: luu nhap, quay lai, bo qua, nop ca bai")
    for u in lab.values():
        tham_gia(u, A)
    r = bat_dau(lab["l1"], A)
    kiem("bat dau bai → 7 cau (5 that + 2 vang, khong lo cau nao la vang)", r.status_code in (200, 201) and r.json()["questionCount"] == 7
         and "purpose" not in str(r.json()["questions"]), r.text)
    b1 = r.json()
    r2 = bat_dau(lab["l1"], A)
    kiem("bat dau lai → cung bai dang lam", r2.status_code in (200, 201) and r2.json()["attemptId"] == b1["attemptId"], r2.text)

    q0 = b1["questions"][0]
    luu(lab["l1"], b1["attemptId"], q0["sampleId"], "b" if DUNG[q0["content"]["text"]] == "a" else "a").raise_for_status()
    lam_bai(lab["l1"], b1, lambda t: DUNG[t])
    lai = requests.get(G + f"/tasks/attempts/{b1['attemptId']}", headers=lab["l1"]["h"]).json()
    kiem("quay lai sua cau dau: dap an moi ghi de, du 7/7 cau", lai["answeredCount"] == 7
         and lai["questions"][0]["answer"]["nhan"]["labelIds"] == [DUNG[q0["content"]["text"]]], lai)
    kiem("labeler khac xem bai cua l1 → 404", requests.get(G + f"/tasks/attempts/{b1['attemptId']}", headers=lab["l2"]["h"]).status_code == 404)

    sai_dinh_dang = requests.put(G + f"/tasks/attempts/{b1['attemptId']}/answers/{q0['sampleId']}", headers=lab["l1"]["h"],
                                 json={"data": {"nhan": {"labelIds": ["zzz"]}}})
    if sai_dinh_dang.status_code == 200:
        r = nop(lab["l1"], b1["attemptId"])
        kiem("nop bai co cau sai dinh dang → 400 bai_chua_hop_le kem danh sach cau",
             r.status_code == 400 and "bai_chua_hop_le" in r.text and "invalidAnswers" in r.text, r.text)
    else:
        kiem("luu cau sai dinh dang → 400 ngay", sai_dinh_dang.status_code == 400, sai_dinh_dang.text)
    luu(lab["l1"], b1["attemptId"], q0["sampleId"], DUNG[q0["content"]["text"]]).raise_for_status()
    r = nop(lab["l1"], b1["attemptId"])
    kiem("l1 nop ca bai (7/7)", r.status_code == 200 and r.json()["answeredCount"] == 7, r.text)
    kiem("nop lai → 409", nop(lab["l1"], b1["attemptId"]).status_code == 409)
    kiem("sua cau sau khi nop → 409", luu(lab["l1"], b1["attemptId"], q0["sampleId"], "a").status_code == 409)

    # l2: sai 1/5 cau that (80%) — dat dung nguong. l3: sai 1, bo trong 1 (60%) — truot.
    b2 = bat_dau(lab["l2"], A).json()
    lam_bai(lab["l2"], b2, lambda t: ("b" if DUNG[t] == "a" else "a") if t == "On" else DUNG[t])
    kiem("l2 nop", nop(lab["l2"], b2["attemptId"]).status_code == 200)
    b3 = bat_dau(lab["l3"], A).json()
    lam_bai(lab["l3"], b3, lambda t: None if t == "Tuyet voi" else (("b" if DUNG[t] == "a" else "a") if t == "Qua te" else DUNG[t]))
    r = nop(lab["l3"], b3["attemptId"])
    kiem("l3 nop khi con bo trong 1 cau (bo trong tinh la khong giong)", r.status_code == 200 and r.json()["answeredCount"] == 6, r.text)

    sl = requests.get(G + f"/tasks/projects/{A}/slots", headers=lab["l4"]["h"]).json()
    kiem("suat: 4 suat, 3 da nop, con 1", sl["slots"] == 4 and sl["submitted"] == 3 and sl["available"] == 1, sl)
    tien_do = requests.get(G + f"/tasks/projects/{A}/progress", headers=biz["h"]).json()
    kiem("tien do cho chu du an: 3 bai da nop", tien_do["submitted"] == 3 and tien_do["phase"] == "open", tien_do)

    # =================================================================================
    print("\n[3] Ket thuc som → cham MOT lan ca du an (da so + cau vang)")
    r = requests.post(G + f"/projects/{A}/finish", headers=biz["h"])
    kiem("chu du an ket thuc som", r.status_code in (200, 202), r.text)
    kiem("du an sang Grading", cho(lambda: trang_thai(biz, A) == "grading", 30), trang_thai(biz, A))
    r = bat_dau(lab["l4"], A, giay=3)
    kiem("l4 vao lam khi da dong thu bai → bi tu choi", r.status_code in (403, 409), r.text)

    g1 = cho(lambda: (bai_cua_toi(lab["l1"], A) or {}).get("passed") is not None and bai_cua_toi(lab["l1"], A), 60)
    kiem("l1 dat: 100% giong da so, dung 2/2 vang", g1 and g1["passed"] and g1["agreementPercent"] == 100 and g1["goldCorrect"] == 2, g1)
    g2 = cho(lambda: (bai_cua_toi(lab["l2"], A) or {}).get("passed") is not None and bai_cua_toi(lab["l2"], A), 30)
    kiem("l2 dat dung nguong: 80%", g2 and g2["passed"] and g2["agreementPercent"] == 80, g2)
    g3 = cho(lambda: (bai_cua_toi(lab["l3"], A) or {}).get("passed") is not None and bai_cua_toi(lab["l3"], A), 30)
    kiem("l3 truot: 60% < 80%, co the khieu nai", g3 and not g3["passed"] and g3["agreementPercent"] == 60 and g3["canAppeal"], g3)

    kiem("l1, l2 nhan thuong 50.000 vao so du kha dung ngay",
         cho(lambda: kha_dung(lab["l1"]) == 50000 and kha_dung(lab["l2"]) == 50000, 30), (vi(lab["l1"]), vi(lab["l2"])))
    kiem("l3 truot khong nhan gi", kha_dung(lab["l3"]) == 0, vi(lab["l3"]))

    nhan = requests.get(G + f"/annotations/projects/{A}?status=accepted&pageSize=100", headers=biz["h"]).json()
    kiem("ket qua chi gom nhan bai dat: 2 bai x 5 cau = 10", nhan["total"] == 10, nhan.get("total"))
    loai = requests.get(G + f"/annotations/projects/{A}?status=excluded&pageSize=100", headers=biz["h"]).json()
    kiem("nhan bai truot bi loai: 4 cau da tra loi cua l3", loai["total"] == 4, loai.get("total"))
    kq = requests.get(G + f"/annotations/projects/{A}/results", headers=biz["h"]).json()
    kiem("ket qua gop co du 5 mau", len(kq.get("samples", kq.get("items", []))) == 5, str(kq)[:300])
    tq = requests.get(G + f"/quality/projects/{A}/summary", headers=biz["h"]).json()
    kiem("quality: 3 bai cham, 2 dat, dung da so", tq.get("attemptsGraded") == 3 and tq.get("attemptsPassed") == 2
         and tq.get("majorityUsed") is True, tq)

    # =================================================================================
    print("\n[4] Khieu nai CA BAI → admin chap nhan → tra thuong, nhan vao ket qua")
    r = requests.post(G + f"/tasks/attempts/{b3['attemptId']}/appeal", headers=lab["l3"]["h"], json={"message": "Cau 'Qua te' mo ho"})
    kiem("l3 khieu nai ca bai", r.status_code == 200 and r.json()["appealState"] == "open", r.text)
    kiem("khieu nai lan hai → 409", requests.post(G + f"/tasks/attempts/{b3['attemptId']}/appeal", headers=lab["l3"]["h"],
                                                   json={"message": "x"}).status_code == 409)
    kiem("hang doi admin co don khieu nai ca bai", cho(lambda: any(
        d.get("subjectId") == b3["attemptId"] for d in requests.get(G + "/admin/approvals?kind=appeal&status=pending", headers=admin).json().get("items", [])), 30))
    r = requests.post(G + f"/tasks/admin/attempts/{b3['attemptId']}/appeal/resolve", headers=admin, json={"accept": True, "note": "Cau mo ho"})
    kiem("admin chap nhan khieu nai", r.status_code == 200, r.text)
    kiem("l3 duoc tra thuong 50.000", cho(lambda: kha_dung(lab["l3"]) == 50000, 30), vi(lab["l3"]))
    kiem("nhan cua l3 vao ket qua: 10 + 4 = 14",
         cho(lambda: requests.get(G + f"/annotations/projects/{A}?status=accepted&pageSize=1", headers=biz["h"]).json()["total"] == 14, 30))

    # =================================================================================
    print("\n[5] Het han khieu nai → chot du an → hoan ky quy phan suat thua")
    kiem("du an Completed sau han khieu nai", cho(lambda: trang_thai(biz, A) == "completed", 90), trang_thai(biz, A))
    kiem("hoan 1 suat thua ve vi: con lai = nap − 3 x (thuong + phi)",
         cho(lambda: vi(biz)["businessAvailableVnd"] == 3_000_000 - 3 * (50000 + phi), 30), vi(biz))
    cp = requests.get(G + f"/ledger/projects/{A}/cost", headers=biz["h"])
    if cp.status_code == 200:
        c = cp.json()
        kiem("chi phi du an: 3 bai da tra, ky quy dong", c.get("paidAttempts") == 3 and c.get("escrowState") == "closed", c)

    # =================================================================================
    print("\n[6] It hon 3 bai nop → chi xet cau vang")
    B, _ = tao_du_an(biz, "It bai", {"t1": "Tuyet voi", "t2": "Qua te", "g1": "Xuat sac"}, 20000, 3, 60, vang=[("g1", "a")])
    publish_va_duyet(biz, B)
    for k in ("l1", "l2"):
        tham_gia(lab[k], B)
    x1 = bat_dau(lab["l1"], B).json()
    lam_bai(lab["l1"], x1, lambda t: "b" if t != "Xuat sac" else "a")   # sai het cau that, dung vang
    nop(lab["l1"], x1["attemptId"]).raise_for_status()
    x2 = bat_dau(lab["l2"], B).json()
    lam_bai(lab["l2"], x2, lambda t: DUNG[t] if t != "Xuat sac" else "b")   # dung het cau that, sai vang
    nop(lab["l2"], x2["attemptId"]).raise_for_status()
    requests.post(G + f"/projects/{B}/finish", headers=biz["h"]).raise_for_status()
    y1 = cho(lambda: (bai_cua_toi(lab["l1"], B) or {}).get("passed") is not None and bai_cua_toi(lab["l1"], B), 60)
    y2 = cho(lambda: (bai_cua_toi(lab["l2"], B) or {}).get("passed") is not None and bai_cua_toi(lab["l2"], B), 30)
    kiem("2 bai: dung vang → dat du lech da so", y1 and y1["passed"], y1)
    kiem("2 bai: sai vang → truot", y2 and not y2["passed"], y2)

    # =================================================================================
    print("\n[7] Huy du an: co bai nop → 409; chua co bai → hoan toan bo ky quy")
    # Du an B chot (hoan suat thua) sau han khieu nai — doi xong de so du khong doi giua chung.
    kiem("du an B (it bai) cung chot", cho(lambda: trang_thai(biz, B) == "completed", 90), trang_thai(biz, B))
    r = requests.post(G + f"/projects/{A}/cancel", headers=biz["h"], json={"reason": "e2e"})
    kiem("huy du an da co bai nop → bi tu choi", r.status_code in (400, 409), r.text)
    C, _ = tao_du_an(biz, "Huy", {"u1": "Tuyet voi", "u2": "Qua te"}, 15000, 3, 70)
    ky_quy_c = requests.get(G + f"/projects/{C}/readiness", headers=biz["h"]).json()["estimatedCostVnd"]
    publish_va_duyet(biz, C)
    truoc = vi(biz)["businessAvailableVnd"]
    requests.post(G + f"/projects/{C}/pause", headers=biz["h"]).raise_for_status()

    def huy():
        x = requests.post(G + f"/projects/{C}/cancel", headers=biz["h"], json={"reason": "e2e"})
        # chua_dong_bo_tam_dung: task-svc chua nhan project.paused — thu lai (thiet ke, docs 3.18).
        return x if x.status_code in (200, 202) or "chua_dong_bo_tam_dung" not in x.text else None
    r = cho(huy, 30, 1) or requests.post(G + f"/projects/{C}/cancel", headers=biz["h"], json={"reason": "e2e"})
    kiem("huy du an chua co bai nop (sau khi tam dung dong bo)", r.status_code in (200, 202), r.text)
    kiem("hoan DUNG toan bo ky quy du an huy", cho(lambda: vi(biz)["businessAvailableVnd"] == truoc + ky_quy_c, 30), (truoc, ky_quy_c, vi(biz)))

    # =================================================================================
    print("\n[8] Kiem tra tong")
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai lanh", rec.get("healthy") is True, rec)
    for db in ("task", "annotation", "quality", "ledger", "project"):
        kiem(f"{db}: outbox da gui het", cho(lambda: sql(db, "select count(*) from outbox where published_at is null") == "0", 30))
finally:
    print("\n[9] Tra setting ve gia tri ban dau")
    for key, v in GOC.items():
        r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e du an trung tam: tra lai"})
        if r.status_code != 200:
            print("  KHONG tra lai duoc", key, r.text)
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
