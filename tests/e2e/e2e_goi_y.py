# E2E GOI Y DU AN (kien truc 3.39): reco-svc — feed ca nhan hoa theo kha nang kiem tien.
#   [1] the chon luc dang ky → so khoi dau bo dem so thich (user.registered v2), token co claim cap do
#   [2] feed: khong lo so nguoi lam, co ly do / phut uoc tinh; doc het cac trang khong lap the nao
#   [3] du an MOI mo giua luc luot → chen vao trang KE TIEP (khong lap, khong mat the)
#   [4] "khong quan tam" → an khoi feed; luot hien → ClickHouse (reco_events, feed_served kem dac trung)
#   [5] nop bai → du an bien khoi feed + bo dem so thich tang (attempt.started / attempt.submitted)
#   [6] mau xem truoc (P1): he thong tu lay 1 mau, doanh nghiep bat / tat; the co toan bo cau hoi + cong cu; NDA khong bat duoc
#   [6b] tab Moi nhat (order=newest): cung dang the, publish moi nhat truoc, khong lap, khong tinh A/B
#   [7] bai xem thu (Q2): du lieu that, khong ghi gi, cham ngay cau kiem tra; labeler khong goi duoc
#   [8] mo hinh hoc may: ml-svc chay lan hoc (chua du du lieu → giu ban dang chay); reco nap mo hinh logistic tu MinIO
#   [9] cho du phong GET /projects/market van chay
# Chay: python e2e_goi_y.py   (stack demo: E2E_PROFILE=demo)
import base64
import json
import os
import subprocess
import sys
import time

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import INTERNAL_KEY, TIEN_TO_CONTAINER, _doc_env  # noqa: E402
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402

CH_RECO_PW = os.environ.get("E2E_CH_RECO_PASSWORD") or _doc_env("CH_RECO_PASSWORD") or "dev_ch_reco_pw"
MINIO_PW = os.environ.get("E2E_MINIO_PASSWORD") or _doc_env("MINIO_PASSWORD") or "dev_minio_pw"


def ch(q):
    """Truy van ClickHouse database reco bang user cua reco-svc (trong container ch-gate)."""
    r = subprocess.run(["docker", "exec", f"{TIEN_TO_CONTAINER}ch-gate", "clickhouse-client", "--user", "reco_user",
                        "--password", CH_RECO_PW, "--database", "reco", "-q", q], capture_output=True, text=True)
    return r.stdout.strip()


def mc(lenh, stdin=None):
    """Lenh mc trong container MinIO (bucket ml-models)."""
    goc = f"mc alias set l http://localhost:9000 datn {MINIO_PW} >/dev/null && " + lenh
    return subprocess.run(["docker", "exec", "-i", f"{TIEN_TO_CONTAINER}minio", "sh", "-c", goc],
                          input=stdin, capture_output=True, text=True)


def claim(u):
    tok = u["h"]["Authorization"].split()[1].split(".")[1]
    return json.loads(base64.urlsafe_b64decode(tok + "=" * (-len(tok) % 4)))


def su_kien(u, kind, pid, trang=None, vi_tri=0):
    body = {"events": [{"kind": kind, "projectId": pid, "position": vi_tri,
                        "requestId": trang["requestId"] if trang else None, "sessionId": trang["sessionId"] if trang else None}]}
    r = requests.post(G + "/reco/events", headers=u["h"], json=body)
    assert r.status_code == 200, r.text
    return r.json()["accepted"]


try:
    biz = dang_ky(["business"], "gy-biz")
    h.nap(biz, 20_000_000)

    # =================================================================================
    print("\n[1] The chon luc dang ky → so khoi dau so thich; token co cap do")
    lab = dang_ky(["labeler"], "gy-lab", so_thich={"domains": ["Y te"], "languages": ["vi"], "modalities": ["text"]})
    dong = cho(lambda: sql("reco", f"select tag_kind||':'||tag_code||':'||a from labeler_affinity where user_id='{lab['id']}' order by 1"), 30)
    the = sql("reco", f"select tag_kind||':'||tag_code||':'||a from labeler_affinity where user_id='{lab['id']}' order by 1").split("\n")
    kiem("bo dem khoi dau tu the dang ky (a = 2)", "domain:y-te:2" in the and "modality:text:2" in the and "language:vi:2" in the, the)
    kiem("access token co claim cap do (reco loc khong hoi identity)", claim(lab).get("level") in (1, "1"), claim(lab))

    # =================================================================================
    print("\n[2] Feed: khong lo so nguoi lam, khong lap the khi luot het")
    duan = []
    for i in range(5):
        pid, _ = tao_du_an(biz, f"GY-{i}", {f"g{i}{k}": f"cau {i}-{k} {TAG}" for k in range(3)}, suat=3, thuong=20000 + i * 10000)
        publish_va_duyet(biz, pid)
        duan.append(pid)
    nap_lai_goi_y()
    the_all, trang = feed(lab, page_size=2)
    ids = [x["id"] for x in the_all]
    kiem("5 du an moi deu co trong feed", set(duan) <= set(ids), set(duan) - set(ids))
    kiem("doc het cac trang: khong the nao lap", len(ids) == len(set(ids)), ids)
    kiem("vi tri the lien tuc qua cac trang", [x["position"] for x in the_all] == list(range(len(the_all))))
    x0 = next(x for x in the_all if x["id"] == duan[0])
    kiem("the KHONG co so nguoi dang lam / da nop", not {"submitted", "submittedCount", "inProgress", "participants", "maxParticipants"} & set(x0), list(x0))
    kiem("the co so suat, thuong, phut uoc tinh, ly do", x0["slots"] == 3 and x0["rewardVnd"] == 20000 and x0["estimatedMinutes"] >= 1 and isinstance(x0["reasons"], list), x0)
    kiem("du an moi co ly do 'moi'", "moi" in x0["reasons"], x0["reasons"])
    kiem("nhieu trang (pageSize 2)", len(trang) >= 3, len(trang))

    # =================================================================================
    print("\n[3] Du an MOI mo giua luc luot → chen vao trang ke tiep")
    t1 = requests.get(G + "/projects/feed", headers=lab["h"], params={"pageSize": 2}).json()
    M, _ = tao_du_an(biz, "GY-MOI", {f"m{k}": f"moi {k} {TAG}" for k in range(3)}, suat=5, thuong=300000)
    publish_va_duyet(biz, M)
    kiem("reco nhan du an moi (event project.published)", cho(lambda: M in json.dumps(requests.get(G + "/projects/feed", headers=lab["h"], params={"pageSize": 30}).json()), 30))
    t2 = requests.get(G + "/projects/feed", headers=lab["h"], params={"pageSize": 2, "cursor": t1["nextCursor"]}).json()
    kiem("du an moi dung DAU trang ke tiep", t2["items"] and t2["items"][0]["id"] == M, [x["id"] for x in t2["items"]])
    kiem("trang ke tiep khong lap the trang truoc", not {x["id"] for x in t1["items"]} & {x["id"] for x in t2["items"]})
    phien, con_tro = t2["sessionId"], t2["nextCursor"]
    con_lai = []
    while con_tro:
        tt = requests.get(G + "/projects/feed", headers=lab["h"], params={"pageSize": 2, "cursor": con_tro}).json()
        con_lai.extend(x["id"] for x in tt["items"])
        con_tro = tt.get("nextCursor") if tt["items"] else None
    ca_luot = [x["id"] for x in t1["items"]] + [x["id"] for x in t2["items"]] + con_lai
    kiem("ca luot: khong lap, khong mat du an nao", len(ca_luot) == len(set(ca_luot)) and set(duan) | {M} <= set(ca_luot), ca_luot)

    # =================================================================================
    print("\n[4] Khong quan tam → an; luot hien → ClickHouse")
    an = duan[4]
    kiem("gui luot hien + khong quan tam", su_kien(lab, "impression", duan[3], t1) == 1 and su_kien(lab, "not_interested", an, t1) == 1)
    kiem("du an 'khong quan tam' bien khoi feed", an not in goi_y(lab))
    kiem("ghi ben vao reco_db.hidden_projects", sql("reco", f"select count(*) from hidden_projects where user_id='{lab['id']}' and project_id='{an}'") == "1")
    kiem("luot hien vao ClickHouse reco_events (relay theo lo)",
         cho(lambda: ch(f"SELECT count() FROM reco_events WHERE user_id = '{lab['id']}' AND kind = 'impression'") not in ("", "0"), 30))
    kiem("the da tra vao feed_served KEM vector dac trung (du lieu hoc)",
         cho(lambda: ch(f"SELECT min(length(features)) FROM feed_served WHERE user_id = '{lab['id']}'") == "21", 30),
         ch(f"SELECT count(), min(length(features)) FROM feed_served WHERE user_id = '{lab['id']}'"))
    kiem("luot hien gan nhom A/B + phien ban mo hinh",
         ch(f"SELECT any(ab_group) || '/' || any(model_version) FROM reco_events WHERE user_id = '{lab['id']}' AND kind = 'impression'") in ("control/g1", "treatment/g1"))

    # =================================================================================
    print("\n[5] Nop bai → bien khoi feed, so thich tang")
    a_truoc = float(sql("reco", f"select a from labeler_affinity where user_id='{lab['id']}' and tag_kind='modality' and tag_code='text'") or 0)
    tham_gia(lab, duan[0])
    lam_het(lab, duan[0], lambda _t: "a")
    kiem("du an da nop bien khoi feed", cho(lambda: duan[0] not in goi_y(lab), 30))
    kiem("bo dem 'text' tang sau bat dau + nop (≈ +0,7)",
         cho(lambda: float(sql("reco", f"select a from labeler_affinity where user_id='{lab['id']}' and tag_kind='modality' and tag_code='text'") or 0) >= a_truoc + 0.6, 30),
         sql("reco", f"select a from labeler_affinity where user_id='{lab['id']}' and tag_kind='modality' and tag_code='text'"))
    kiem("hanh vi start / submit vao ClickHouse",
         cho(lambda: ch(f"SELECT countIf(kind = 'start') > 0 AND countIf(kind = 'submit') > 0 FROM reco_events WHERE user_id = '{lab['id']}'") == "1", 30))

    # =================================================================================
    print("\n[6] Mau xem truoc (P1) — tu lay 1 mau, doanh nghiep chi bat / tat; the co toan bo cau hoi + cong cu")
    P = duan[1]
    ct = requests.get(G + f"/projects/{P}", headers=biz["h"]).json()
    kiem("mac dinh bat xem truoc, du an duoc phep", ct.get("previewEnabled") is True and ct.get("previewBlockedReason") is None, ct.get("previewEnabled"))
    nap_lai_goi_y()
    tp = next((x for x in feed(lab)[0] if x["id"] == P), None)
    kiem("the feed co DUNG 1 mau xem truoc (tu lay, van ban co doan trich)",
         tp and len(tp["previews"]) == 1 and TAG in (tp["previews"][0]["snippet"] or ""), tp and tp["previews"])
    kiem("the feed co toan bo cong cu + cau hoi + lop",
         tp and len(tp["tools"]) == 1 and tp["tools"][0]["kind"] == "classification" and tp["tools"][0]["question"]
         and tp["tools"][0]["classes"] == ["a", "b"], tp and tp["tools"])
    kiem("mau xem truoc khong bao gio la cau vang",
         sql("project", f"select count(*) from gold_items g join v_reco_preview_samples v on v.sample_id = g.sample_id") == "0")
    r = requests.put(G + f"/projects/{P}/preview", headers=biz["h"], json={"enabled": False})
    kiem("doanh nghiep tat xem truoc", r.status_code == 200 and r.json()["enabled"] is False and r.json()["showing"] is False, r.text)
    nap_lai_goi_y()
    tp = next((x for x in feed(lab)[0] if x["id"] == P), None)
    kiem("tat → the khong con mau nhung van co cau hoi + cong cu", tp and tp["previews"] == [] and tp["tools"], tp and (tp["previews"], tp["tools"]))
    r = requests.put(G + f"/projects/{P}/preview", headers=biz["h"], json={"enabled": True})
    kiem("bat lai sau publish van duoc", r.status_code == 200 and r.json()["showing"] is True, r.text)
    kiem("labeler khong bat / tat duoc", requests.put(G + f"/projects/{P}/preview", headers=lab["h"], json={"enabled": False}).status_code == 403)
    nda, _ = tao_du_an(biz, "GY-NDA", {"n1": f"nda {TAG}", "n2": f"nda2 {TAG}"})
    requests.patch(G + f"/projects/{nda}", headers=biz["h"], json={"protection": {"sensitiveContent": False, "contentWarning": None, "ndaRequired": True,
                   "ndaText": "Khong tiet lo.", "requireVerifiedIdentity": False}}).raise_for_status()
    cn = requests.get(G + f"/projects/{nda}", headers=biz["h"]).json()
    r = requests.put(G + f"/projects/{nda}/preview", headers=biz["h"], json={"enabled": True})
    kiem("du an NDA: bao ly do, khong bat duoc",
         cn.get("previewBlockedReason") == "xem_truoc_khi_co_nda" and r.status_code == 400 and r.json().get("code") == "xem_truoc_khi_co_nda", r.text)
    kiem("du an NDA: tat thi luon duoc", requests.put(G + f"/projects/{nda}/preview", headers=biz["h"], json={"enabled": False}).status_code == 200)

    print("\n[6b] Tab Moi nhat — cung dang the, moi publish truoc, khong lap")
    nap_lai_goi_y()
    moi, trang_moi = feed(lab, page_size=2, order="newest")
    ids_moi = [x["id"] for x in moi]
    kiem("nhieu trang, khong lap the", len(trang_moi) >= 2 and len(ids_moi) == len(set(ids_moi)), ids_moi)
    kiem("phien tab Moi nhat co tien to rieng (khong tinh A/B)", all(t["sessionId"].startswith("moi_") for t in trang_moi))
    thu_tu = sql("project", "select string_agg(id::text, ',' order by published_at desc, id desc) from projects where id in ("
                 + ",".join(f"'{i}'" for i in ids_moi) + ")").split(",")
    kiem("xep dung thu tu publish moi nhat truoc", thu_tu == ids_moi, (thu_tu, ids_moi))
    tm = next((x for x in moi if x["id"] == P), None)
    kiem("the o tab Moi nhat cung co mau + cau hoi + cong cu", tm and len(tm["previews"]) == 1 and tm["tools"] and tm["explore"] is False, tm)
    kiem("du an da nop khong hien o Moi nhat", duan[0] not in ids_moi)

    # =================================================================================
    print("\n[7] Bai xem thu bang du lieu that (Q2)")
    Q, ids = tao_du_an(biz, "GY-THU", {"q1": f"thu 1 {TAG}", "q2": f"thu 2 {TAG}", "q3": f"thu 3 {TAG}"},
                       vang=[("q3", "b", "qualityCheck")])
    bai = requests.get(G + f"/projects/{Q}/trial", headers=biz["h"]).json()
    kiem("bai xem thu co cau that + cau kiem tra, chua co cau tra loi",
         bai.get("questionCount") == 3 and all(q["answer"] is None for q in bai["questions"]) and bai["labelSchema"]["tools"], bai)
    kiem("du an nhap van xem thu duoc; khong tao bai lam nao",
         sql("project", f"select count(*) from attempts where project_id='{Q}'") == "0")
    dung = {"answers": [{"sampleId": ids["q3"], "data": {"nhan": {"labelIds": ["b"]}}}, {"sampleId": ids["q1"], "data": {"nhan": {"labelIds": ["a"]}}}]}
    sai = {"answers": [{"sampleId": ids["q3"], "data": {"nhan": {"labelIds": ["a"]}}}]}
    kd = requests.post(G + f"/projects/{Q}/trial/check", headers=biz["h"], json=dung).json()
    ks = requests.post(G + f"/projects/{Q}/trial/check", headers=biz["h"], json=sai).json()
    kiem("tra loi dung cau kiem tra → khong truot", kd["goldTotal"] == 1 and kd["goldCorrect"] == 1 and not kd["wouldFailGold"], kd)
    kiem("tra loi sai → bao se truot, kem dap an dung", ks["goldCorrect"] == 0 and ks["wouldFailGold"] and ks["gold"][0]["expected"]["nhan"]["labelIds"] == ["b"], ks)
    r = requests.post(G + f"/projects/{Q}/trial/check", headers=biz["h"], json={"answers": [{"sampleId": ids["q1"], "data": {"nhan": {"labelIds": ["zz"]}}}]})
    kiem("cau sai dinh dang → liet ke nhu nop bai that", r.status_code == 200 and r.json()["invalidAnswers"], r.text)
    kiem("labeler khong goi duoc bai xem thu", requests.get(G + f"/projects/{Q}/trial", headers=lab["h"]).status_code == 403)

    # =================================================================================
    print("\n[8] Mo hinh hoc may")
    r = subprocess.run(["docker", "exec", f"{TIEN_TO_CONTAINER}ml", "python", "-c",
                        "import httpx,os,json;print(json.dumps(httpx.post('http://localhost:8202/internal/reco/train',"
                        "headers={'X-Internal-Key':os.environ['ML_INTERNAL_API_KEY']},timeout=600).json()))"],
                       capture_output=True, text=True)
    try:
        hoc = json.loads(r.stdout.strip().splitlines()[-1])
    except (ValueError, IndexError):
        hoc = {"loi": r.stdout + r.stderr}
    kiem("ml-svc doc ClickHouse (user chi doc) va chay lan hoc — chua du du lieu thi giu ban dang chay",
         hoc.get("status") == "skipped" and hoc.get("reason") == "chua_du_du_lieu" and hoc.get("impressions", 0) >= 1, hoc)

    # Mo hinh logistic tay: P(bat dau) chi theo thuong. reco doc manifest → feed cham bang mo hinh nay.
    ten = ["log_reward", "slots", "competitors", "p_slot", "est_minutes", "question_count", "match", "match_domain",
           "match_language", "match_modality", "match_tool", "p_pass_prior", "reputation", "level", "is_new",
           "hours_to_deadline", "hour_of_day", "position", "requires_test", "project_start_rate", "tried_before"]
    w = [1.0 if t == "log_reward" else 0.0 for t in ten]
    mo_hinh = {"version": "log-e2e", "kind": "logistic", "features": ten,
               "start": {"intercept": -1, "coef": w, "mean": [0] * len(ten), "scale": [1] * len(ten)},
               "pass": {"intercept": 0, "coef": [0] * len(ten), "mean": [0] * len(ten), "scale": [1] * len(ten)}}
    ok1 = mc("mc pipe l/ml-models/reco/models/log-e2e.json", json.dumps(mo_hinh)).returncode == 0
    ok2 = mc("mc pipe l/ml-models/reco/manifest.json", json.dumps({"production": "reco/models/log-e2e.json", "production_version": "log-e2e",
                                                                   "candidate": None, "candidate_version": None, "candidate_since": None})).returncode == 0
    nl = nap_lai_goi_y()
    kiem("reco nap mo hinh logistic tu MinIO (ban dang chay)", ok1 and ok2 and nl.get("productionModel") == "log-e2e", nl)
    tr = feed(lab)[1][0]
    kiem("feed van tra duoc voi mo hinh moi", tr["items"] != [], tr)
    kiem("the tra ghi phien ban mo hinh moi",
         cho(lambda: ch(f"SELECT count() FROM feed_served WHERE user_id = '{lab['id']}' AND model_version = 'log-e2e'") not in ("", "0"), 30))
    mc("mc rm l/ml-models/reco/manifest.json")
    nl = nap_lai_goi_y()

    # =================================================================================
    print("\n[9] Cho du phong (reco sap → frontend dung danh sach nay)")
    r = requests.get(G + "/projects/market", headers=lab["h"], params={"pageSize": 100})
    kiem("GET /projects/market van chay", r.status_code == 200 and duan[2] in {p["id"] for p in r.json()["items"]}, r.text[:300])
    kiem("GET /projects/recommended da bo", requests.get(G + "/projects/recommended", headers=lab["h"]).status_code in (404, 405))
except Exception:  # noqa: BLE001
    import traceback
    traceback.print_exc()
    KQ["fail"] += 1
    KQ["loi"].append("ngoai le — xem traceback")
finally:
    # Tra lai G1 neu bo kiem dung giua chung.
    try:
        mc("mc rm l/ml-models/reco/manifest.json")
        nap_lai_goi_y()
    except Exception:  # noqa: BLE001
        pass
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
sys.exit(1 if KQ["fail"] else 0)
