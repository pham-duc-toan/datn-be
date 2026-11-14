# E2E CAU HOI (11/2026, kien truc 3.34 + 3.35): MOI CONG CU co cau hoi rieng (labelSchema.tools[].question, bat
# buoc truoc khi publish) — khong co cau hoi chung cua tap nhan, khong co dong nhac "hint". Mau co the them ngu canh /
# cau hoi RIENG (samples.question, tuy chon — chu du an dien khi them mau / sua tung mau). Huong dan Markdown da bo.
#
# Phu: cong cu thieu cau hoi → readiness bao chua_co_cau_hoi + toolsMissingQuestion; question chung / hint → 400;
# dong tai len kem "question"; cung noi dung + cau hoi khac = hai mau, cung noi dung + cung cau = trung;
# PUT .../samples/{id}/question (dat / bo / trung → 409 / sau publish → 409); da co cau vang van sua duoc cau hoi cong
# cu nhung khong doi duoc cong cu; bai lam tra "question" = cau rieng cua mau (khong ke thua gi); view v_gate_samples
# cua gate co cot question = cau rieng.
import os
import sys
import uuid

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import G, KQ, TAG, admin, bat_dau, cho, dang_ky, kiem, nap_mau, publish_va_duyet, sql, tham_gia, vi  # noqa: E402

CAU = "Cau nay mang sac thai gi?"


def nap_tien(biz, so):
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": "e2e-" + uuid.uuid4().hex}), json={"amountVnd": so})
    r.raise_for_status()
    requests.post(G + f"/payments/sandbox/checkout/{r.json()['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(biz)["businessAvailableVnd"] >= so, 30), vi(biz)


def mau_theo_ten(pid, h):
    return {m["originalName"]: m for m in requests.get(G + f"/projects/{pid}/samples?pageSize=100", headers=h).json()["items"]}


def dat_cau_hoi_mau(h, pid, sid, cau):
    return requests.put(G + f"/projects/{pid}/samples/{sid}/question", headers=h, json={"question": cau})


TOOL = {"name": "nhan", "kind": "classification", "classes": ["vui", "buon"]}
TOOL2 = {"name": "y_kien", "kind": "freeText"}

try:
    biz = dang_ky(["business"], "cauhoi-biz")
    lab = dang_ky(["labeler"], "cauhoi-lab")
    nap_tien(biz, 200_000)

    print("\n[1] Moi cong cu bat buoc co cau hoi truoc khi publish (khong con cau hoi chung)")
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-cauhoi] {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
    P = r.json()["id"]
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [dict(TOOL, question=CAU), TOOL2]}})
    kiem("luu nhap duoc khi con cong cu chua co cau hoi", r.status_code == 200, r.text)
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"pricing": {"rewardVnd": 10000, "slots": 3, "passThresholdPercent": 60,
                                                                      "deadline": "2027-12-31T00:00:00Z"}}).raise_for_status()
    rdy = requests.get(G + f"/projects/{P}/readiness", headers=biz["h"]).json()
    kiem("cong cu thieu cau hoi → readiness bao chua_co_cau_hoi", "chua_co_cau_hoi" in rdy["missing"] and not rdy["ready"], rdy["missing"])
    kiem("readiness neu ten cong cu thieu cau hoi", rdy.get("toolsMissingQuestion") == ["y_kien"], rdy.get("toolsMissingQuestion"))
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"guideline": {"markdown": "x", "examples": []}})
    kiem("nhom guideline khong con (PATCH chi co guideline → 400 khong co gi de sua)", r.status_code == 400, r.text)
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "question": CAU, "tools": [dict(TOOL, question=CAU)]}})
    kiem("cau hoi chung cua tap nhan khong con → 400 tap_nhan_sai_dinh_dang", r.status_code == 400 and "tap_nhan_sai_dinh_dang" in r.text, r.text)
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [dict(TOOL, question=CAU, hint="Chi chon mot")]}})
    kiem("dong nhac hint khong con → 400", r.status_code == 400, r.text)
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text",
                                                                                     "tools": [dict(TOOL, question="  " + CAU + " "), dict(TOOL2, question="   ")]}})
    tn = r.json()["labelSchema"]
    kiem("cau hoi cong cu duoc cat khoang trang; toan khoang trang = chua co",
         r.status_code == 200 and tn["tools"][0]["question"] == CAU and "question" not in tn["tools"][1] and "question" not in tn, tn)
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [dict(TOOL, question=CAU)]}}).raise_for_status()
    kiem("project khong con truong guideline", "guideline" not in requests.get(G + f"/projects/{P}", headers=biz["h"]).json())

    print("\n[2] Cau hoi rieng khi them mau; chong trung theo (noi dung, cau hoi)")
    r = requests.post(G + f"/projects/{P}/samples", headers=biz["h"], json={"rows": [
        {"text": "Hom nay troi dep", "name": "a1"},
        {"text": "Mat dien ca ngay", "name": "b1", "question": "  Nguoi viet co buc minh khong?  "},
        {"text": "Hom nay troi dep", "name": "a2", "question": "Cau nay noi ve thoi tiet hay con nguoi?"},
        {"text": "Hom nay troi dep", "name": "a3"},
        {"text": "Qua dai", "name": "dai", "question": "x" * 1001},
    ]})
    j = r.json()
    kiem("nhan 3 mau, bo 2 (trung noi dung + cau hoi; cau hoi qua 1000 ky tu)",
         r.status_code == 202 and j["ready"] == 3 and j["skipped"] == 2 and len(j["errors"]) == 2, j)
    m = mau_theo_ten(P, biz["h"])
    kiem("mau khong gui question → question null", m["a1"]["question"] is None, m["a1"])
    kiem("mau co cau rieng (da cat khoang trang)", m["b1"]["question"] == "Nguoi viet co buc minh khong?", m["b1"])
    kiem("cung noi dung, khac cau hoi = hai mau", m["a2"]["question"] and m["a2"]["content"]["text"] == m["a1"]["content"]["text"], m["a2"])

    print("\n[3] Sua cau hoi tung mau (PUT .../samples/{id}/question)")
    r = dat_cau_hoi_mau(biz["h"], P, m["a1"]["id"], "Cau nay vui hay buon?")
    kiem("dat cau rieng cho mau → 200", r.status_code == 200 and r.json()["question"] == "Cau nay vui hay buon?", r.text)
    r = dat_cau_hoi_mau(biz["h"], P, m["a1"]["id"], "Cau nay noi ve thoi tiet hay con nguoi?")
    kiem("trung (cung noi dung + cung cau hoi voi a2) → 409 noi_dung_trung", r.status_code == 409 and "noi_dung_trung" in r.text, r.text)
    r = dat_cau_hoi_mau(biz["h"], P, m["a1"]["id"], "")
    kiem("chuoi rong → bo cau rieng", r.status_code == 200 and r.json()["question"] is None, r.text)
    kiem("labeler khong sua duoc cau hoi mau cua du an nguoi khac", dat_cau_hoi_mau(lab["h"], P, m["a1"]["id"], "hack").status_code in (403, 404))
    kiem("mau khong thuoc du an → 404", dat_cau_hoi_mau(biz["h"], P, str(uuid.uuid4()), "x").status_code == 404)

    print("\n[4] Da co cau vang: sua cau hoi cong cu duoc, doi cong cu thi khong")
    r = requests.post(G + f"/projects/{P}/gold-items", headers=biz["h"], json={"items": [
        {"sampleId": m["b1"]["id"], "purpose": "qualityCheck", "expectedPayload": {"nhan": {"labelIds": ["buon"]}}}]})
    kiem("them cau vang", r.status_code == 200, r.text)
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [dict(TOOL, question=CAU + " (sua)")]}})
    kiem("co cau vang, chi doi cau hoi cong cu → 200", r.status_code == 200 and r.json()["labelSchema"]["tools"][0]["question"] == CAU + " (sua)", r.text)
    r = requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text",
                                                                                     "tools": [dict(TOOL, question=CAU, classes=["vui", "buon", "binh_thuong"])]}})
    kiem("co cau vang, doi lop cua cong cu → 409 da_co_cau_hoi_vang", r.status_code == 409 and "da_co_cau_hoi_vang" in r.text, r.text)
    requests.patch(G + f"/projects/{P}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [dict(TOOL, question=CAU)]}}).raise_for_status()

    print("\n[5] Publish → bai lam tra cau hoi cong cu (tap nhan) + cau rieng cua mau")
    rdy = requests.get(G + f"/projects/{P}/readiness", headers=biz["h"]).json()
    kiem("moi cong cu du cau hoi → san sang", rdy["ready"] and rdy["toolsMissingQuestion"] == [], rdy)
    publish_va_duyet(biz, P)
    r = dat_cau_hoi_mau(biz["h"], P, m["a1"]["id"], "doi giua chung")
    kiem("du an da chay → khong sua cau hoi mau (409)", r.status_code == 409, r.text)
    tham_gia(lab, P)
    bai = bat_dau(lab, P)
    assert isinstance(bai, dict), bai.text
    q = {x["sampleId"]: x["question"] for x in bai["questions"]}
    kiem("mau khong co cau rieng → question null (khong ke thua cau nao)", m["a1"]["id"] in q and q[m["a1"]["id"]] is None, q)
    kiem("mau co cau rieng → cau rieng", q.get(m["a2"]["id"]) == "Cau nay noi ve thoi tiet hay con nguoi?", q)
    kiem("cau vang cung hien cau rieng (khong lo la cau vang)", q.get(m["b1"]["id"]) == "Nguoi viet co buc minh khong?", q)
    kiem("bai lam kem tap nhan co cau hoi cong cu", (bai.get("labelSchema") or {}).get("tools", [{}])[0].get("question") == CAU, bai.get("labelSchema"))
    kiem("bai lam khong con truong guideline / content khong chua cau hoi",
         all("prompt" not in (x.get("content") or {}) for x in bai["questions"]), bai["questions"][0])

    print("\n[6] Cong link doc cau hoi qua view cua project")
    dinh_nghia = sql("project", "select pg_get_viewdef('v_gate_samples')")
    kiem("v_gate_samples co cot question = cau rieng cua mau (khong COALESCE cau chung)", "COALESCE" not in dinh_nghia and "question" in dinh_nghia, dinh_nghia)

    print("\n[7] Mau bai toan cua admin: cau hoi nam o tung cong cu, khong con huong dan Markdown")
    ds = requests.get(G + "/admin/templates", headers=admin).json()
    ds = ds if isinstance(ds, list) else ds.get("items", [])
    kiem("mau bai toan khong con guidelineMarkdown", ds and all("guidelineMarkdown" not in t for t in ds), ds[:1])
    seed = [t for t in ds if t.get("code") in ("phan-loai-anh", "ner-tieng-viet", "chep-loi-am-thanh", "phat-hien-vat-the-xoay")]
    kiem("mau bai toan seed: moi cong cu co cau hoi, khong con cau hoi chung",
         bool(seed) and all("question" not in t["labelSchema"] and all(c.get("question") for c in t["labelSchema"]["tools"]) for t in seed),
         [t.get("labelSchema") for t in seed][:2])
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    KQ["fail"] += 1
    KQ["loi"].append("ngoai le — xem traceback")
finally:
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
sys.exit(1 if KQ["fail"] else 0)
