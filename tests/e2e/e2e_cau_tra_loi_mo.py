# E2E CAU TRA LOI MO (kien truc 3.19): cham freeText THEO NGHIA qua ml-svc — tu tao tai khoan + du an moi.
#
# Phu: canh bao readiness "cham_cau_tra_loi_mo_lau"; bai nop toi → quality-svc gui ml-svc nhung cau ngay;
# du suat → cham: cau cung y khac chu (cosine >= quality.freetext_similarity) giong da so, cau lac de / bo trong
# khong giong → truot; ml_db chi giu vector (khong giu chu); tong ket chat luong ghi method = semantic.
# Du phong (chi stack Docker, E2E_PROFILE=demo): DUNG ml-svc → du an cho phan tich (pending) → qua
# quality.freetext_analysis_timeout thi cham bang so chu (lexical) — khong giu tien cua labeler vo thoi han.
# Chay: python e2e_cau_tra_loi_mo.py   (stack demo: E2E_PROFILE=demo)
import os
import subprocess
import sys
import time
import uuid

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import sql_container  # noqa: E402
from tro_giup import (  # noqa: E402
    G, GOC, KQ, TAG, admin, bai_cua_toi, bat_dau, cho, cho_cham, dang_ky, dat_va_cho, kiem, luu, nop_bai, sql, tham_gia, vi,
)

DEMO = os.environ.get("E2E_PROFILE") == "demo"
MO = {"mo_ta": None}


def nap(biz, so):
    k = "e2e-" + uuid.uuid4().hex
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": so})
    r.raise_for_status()
    requests.post(G + f"/payments/sandbox/checkout/{r.json()['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(biz)["businessAvailableVnd"] >= so, 30), vi(biz)


def tao_du_an(chu, ten, mau, thuong=20000, suat=3, nguong=60):
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-3.19] {ten} {TAG}", "description": "e2e",
                                                                "modality": "text", "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json={"modality": "text", "tools": [
        {"name": "mo_ta", "kind": "freeText", "maxLength": 500}]}).raise_for_status()
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "Mo ta noi dung bang mot cau.", "examples": []}).raise_for_status()
    requests.put(G + f"/projects/{pid}/pricing", headers=chu["h"], json={
        "rewardVnd": thuong, "slots": suat, "passThresholdPercent": nguong, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    requests.post(G + f"/projects/{pid}/datasets/manifest", headers=chu["h"],
                  json={"name": "lo", "rows": [{"text": v, "name": k} for k, v in mau.items()]}).raise_for_status()
    assert cho(lambda: [d for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json() if d["status"] == "ready"], 60)
    return pid


def publish_va_duyet(chu, pid):
    requests.post(G + f"/projects/{pid}/publish", headers=chu["h"]).raise_for_status()
    assert cho(lambda: requests.get(G + f"/projects/{pid}", headers=chu["h"]).json()["status"] == "pendingApproval", 40)
    requests.post(G + f"/projects/{pid}/approve", headers=admin).raise_for_status()
    assert cho(lambda: requests.get(G + f"/tasks/projects/{pid}/slots", headers=chu["h"]).status_code == 200, 40)


def lam(u, pid, tra_loi, nop=True):
    """tra_loi: {text cua mau: cau mo ta | None (bo trong)}."""
    b = bat_dau(u, pid, 20)
    assert isinstance(b, dict), b.text
    for q in b["questions"]:
        cau = tra_loi.get(q["content"]["text"])
        if cau is not None:
            luu(u, b["attemptId"], q["sampleId"], {"mo_ta": {"text": cau}}).raise_for_status()
    if nop:
        r = nop_bai(u, b["attemptId"])
        assert r.status_code == 200, r.text
    return b


def tong_ket(biz, pid):
    return requests.get(G + f"/quality/projects/{pid}/summary", headers=biz["h"]).json()


def docker(*args):
    return subprocess.run(["docker", *args], capture_output=True, text=True)


try:
    dat_va_cho("ledger.reward_hold", 0, ["ledger"])
    dat_va_cho("task.appeal_window", 15, ["task"])
    biz = dang_ky(["business"], "mobiz")
    lab = [dang_ky(["labeler"], f"mo{i}") for i in range(1, 4)]
    nap(biz, 1_000_000)

    # =================================================================================
    print("\n[1] Tap nhan co cau tra loi mo → canh bao cham lau (khong chan publish)")
    A = tao_du_an(biz, "Mo ta", {"m1": "Anh 1: con vat trong nha", "m2": "Anh 2: thoi tiet ngoai duong"})
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    kiem("readiness: canh bao cham_cau_tra_loi_mo_lau, van san sang", "cham_cau_tra_loi_mo_lau" in rdy["warnings"] and rdy["ready"], rdy)

    # =================================================================================
    print("\n[2] Cham THEO NGHIA: cung y khac chu → giong; lac de / bo trong → khong giong")
    publish_va_duyet(biz, A)
    for u in lab:
        tham_gia(u, A)
    MEO, MUA = "Anh 1: con vat trong nha", "Anh 2: thoi tiet ngoai duong"
    lam(lab[0], A, {MEO: "Con mèo đang ngủ trên ghế sofa", MUA: "Trời mưa rất to ngoài đường"})
    lam(lab[1], A, {MEO: "Một con mèo nằm ngủ trên ghế", MUA: "Ngoài phố đang mưa lớn"})
    # Hai bai dau da toi quality-svc: worker nhung cau NGAY, khong doi du an ket thuc.
    kiem("bai nop toi → quality-svc nhung cau qua ml-svc ngay (truoc khi dong thu bai)",
         cho(lambda: sql("quality", f"select count(*) from freetext_vectors where project_id = '{A}' and vector is not null") == "4", 60),
         sql("quality", f"select count(*) from freetext_vectors where project_id = '{A}'"))
    lam(lab[2], A, {MEO: "Giá vàng hôm nay tăng mạnh", MUA: None})

    g = [cho_cham(u, A, 120) for u in lab]
    kiem("l1 dat 100% (cau o giua cung y)", g[0] and g[0]["passed"] and g[0]["agreementPercent"] == 100, g[0])
    kiem("l2 dat 100% (khac chu, cung nghia)", g[1] and g[1]["passed"] and g[1]["agreementPercent"] == 100, g[1])
    kiem("l3 truot 0% (lac de + bo trong)", g[2] and not g[2]["passed"] and g[2]["agreementPercent"] == 0, g[2])
    tk = tong_ket(biz, A)
    kiem("tong ket: phan tich xong, so theo nghia", tk.get("freeTextAnalysis") == {"state": "done", "method": "semantic"}, tk)
    kiem("l1, l2 nhan thuong", cho(lambda: vi(lab[0])["availableVnd"] == 20000 and vi(lab[1])["availableVnd"] == 20000, 30),
         (vi(lab[0]), vi(lab[1])))
    so_vec = sql("ml", "select count(*) from embedding_cache")
    cot = sql("ml", "select string_agg(column_name, ',' order by ordinal_position) from information_schema.columns where table_name = 'embedding_cache'")
    kiem("ml_db giu vector theo khoa bam, KHONG giu van ban goc", int(so_vec or 0) >= 5 and "text," not in cot + "," and "text_hash" in cot, (so_vec, cot))
    kiem("ml-svc khong mo qua gateway", requests.post(G + "/internal/embeddings", json={"texts": ["x"]}).status_code == 404)

    # =================================================================================
    print("\n[3] ml-svc sap → du an cho phan tich → qua han thi cham bang so chu (chi stack Docker)")
    if not DEMO:
        print("  SKIP: can E2E_PROFILE=demo (dung / bat container ml)")
    else:
        dat_va_cho("quality.freetext_analysis_timeout", 20, ["quality"])
        B = tao_du_an(biz, "Du phong", {"d1": "Anh 3: con vat trong nha"})
        publish_va_duyet(biz, B)
        for u in lab:
            tham_gia(u, B)
        ml = sql_container("ml").replace("db-ml", "ml")
        r = docker("stop", ml)
        kiem("dung container ml-svc", r.returncode == 0, r.stderr)
        try:
            lam(lab[0], B, {"Anh 3: con vat trong nha": "Con mèo đang ngủ trên ghế sofa"})
            lam(lab[1], B, {"Anh 3: con vat trong nha": "Con mèo ngủ trên ghế sofa"})
            lam(lab[2], B, {"Anh 3: con vat trong nha": "Giá vàng hôm nay tăng mạnh"})
            kiem("du suat nhung ml-svc sap → cho phan tich (pending), chua cham",
                 cho(lambda: (tong_ket(biz, B).get("freeTextAnalysis") or {}).get("state") == "pending", 30)
                 and (bai_cua_toi(lab[0], B) or {}).get("passed") is None, tong_ket(biz, B))
            g = [cho_cham(u, B, 90) for u in lab]
            kiem("qua han → cham bang so chu: l1, l2 dat; l3 truot",
                 all(g) and g[0]["passed"] and g[1]["passed"] and not g[2]["passed"], g)
            kiem("tong ket ghi method = lexical", tong_ket(biz, B).get("freeTextAnalysis") == {"state": "done", "method": "lexical"},
                 tong_ket(biz, B))
        finally:
            docker("start", ml)
        kiem("bat lai ml-svc", cho(lambda: docker("inspect", "-f", "{{.State.Running}}", ml).stdout.strip() == "true", 30))

    # =================================================================================
    print("\n[4] Kiem tra tong")
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai lanh", rec.get("healthy") is True, rec)
    kiem("quality: outbox da gui het", cho(lambda: sql("quality", "select count(*) from outbox where published_at is null") == "0", 30))
finally:
    for key, v in GOC.items():
        r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e cau tra loi mo: tra lai"})
        if r.status_code != 200:
            print("  KHONG tra lai duoc", key, r.text)
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
