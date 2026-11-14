# E2E VAN HANH ADMIN:
#   duyet tung loai don o API cua service chu (KYB, du an, khieu nai, link bi bao cao, gioi thieu) — hang doi duyet
#   chung da bo (kien truc 3.33); mau bai toan (FM-03);
#   nhat ky audit chi ghi them o muc CSDL (P-03); log loi di Elasticsearch (khong con man hinh DLQ).
# Chay: python e2e_hang_doi_duyet.py   (stack demo: E2E_PROFILE=demo)
import json, os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER, URL_LINK  # noqa: E402

MGMT = (RABBIT_USER, RABBIT_PASSWORD)


def kyb(status, business_id):
    r = requests.get(G + f"/kyb/admin?status={status}&pageSize=200", headers=admin)
    return next((x for x in r.json()["items"] if x["profile"]["businessId"] == business_id), None)


def du_an_cho_duyet(pid):
    return any(x["id"] == pid for x in requests.get(G + "/projects/pending-approval", headers=admin).json())


def khieu_nai(state, attempt_id):
    return next((x for x in requests.get(G + f"/tasks/admin/appeals?state={state}", headers=admin).json()
                 if x["attemptId"] == attempt_id), None)


def link_cho_kiem(link_id):
    r = requests.get(G + "/links/admin/review-queue?pageSize=200", headers=admin)
    return next((x for x in r.json()["items"] if x["link"]["id"] == link_id), None)


try:
    # =================================================================================
    print("\n[1] Duyet KYB (API cua identity)")
    kiem("hang doi duyet chung da bo (3.33): /admin/approvals → 404",
         requests.get(G + "/admin/approvals", headers=admin).status_code == 404
         and requests.get(G + "/admin/approvals/summary", headers=admin).status_code == 404)
    kiem("bang approval_requests khong con", sql("admin", "select count(*) from pg_tables where tablename='approval_requests'") == "0")
    biz = dang_ky(["business"], "hdd", xac_minh=False)
    nop_kyb(biz, "Cong ty Hang Doi").raise_for_status()
    d = kyb("submitted", biz["id"])
    kiem("nop KYB → co trong danh sach cho duyet cua identity (ten cong ty)", d and d["profile"]["companyName"] == "Cong ty Hang Doi", d)
    kiem("nguoi khong phai admin → 403", requests.get(G + "/kyb/admin", headers=biz["h"]).status_code == 403)
    ct_kyb = requests.get(G + f"/kyb/admin/{biz['id']}", headers=admin).json()
    kiem("S-07: admin lay link xem giay phep → nhat ky audit ghi lan xem (ai xem, giay to cua ai)",
         cho(lambda: sql("admin", "select count(*) from audit_events where event_type = 'sensitive_document.viewed' "
                                  f"and payload->>'subjectUserId' = '{biz['id']}' and actor_role = 'admin'") == "1", 20), ct_kyb.get("licenseUrl"))
    requests.post(G + f"/kyb/admin/{biz['id']}/approve", headers=admin).raise_for_status()
    kiem("admin duyet → roi danh sach cho duyet, sang verified", kyb("submitted", biz["id"]) is None and kyb("verified", biz["id"]) is not None)
    lam_moi_token(biz)

    # =================================================================================
    print("\n[2] Duyet du an (API cua project: cho ky quy → cho duyet → tu choi / duyet)")
    vi_nap = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 500000}).json()
    requests.post(G + f"/payments/sandbox/checkout/{vi_nap['intentId']}/pay?amount=500000")
    cho(lambda: vi(biz)["businessAvailableVnd"] >= 500000, 30)
    P, _ = tao_du_an(biz, "hang doi", {f"c{i}": f"cau {i} {TAG}" for i in range(2)}, 1, 1, 1000)
    R, _ = tao_du_an(biz, "bi tu choi", {f"r{i}": f"cau r{i} {TAG}" for i in range(2)}, 1, 1, 1000)
    for x in (P, R):
        requests.post(G + f"/projects/{x}/publish", headers=biz["h"]).raise_for_status()
    # Ky quy = 3 suat x (thuong 10.000 + phi 30%) = 39.000.
    kiem("du an ky quy xong → co trong danh sach cho duyet, ledger giu 39.000",
         cho(lambda: du_an_cho_duyet(P), 30)
         and sql("ledger", f"select reserved_vnd from project_escrows where project_id='{P}'") == "39000",
         sql("ledger", f"select reserved_vnd from project_escrows where project_id='{P}'"))
    requests.post(G + f"/projects/{P}/approve", headers=admin).raise_for_status()
    kiem("admin duyet → dang chay, roi danh sach cho duyet",
         requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "running" and not du_an_cho_duyet(P))
    cho(lambda: du_an_cho_duyet(R), 30)
    requests.post(G + f"/projects/{R}/reject", headers=admin, json={"reason": "Noi dung khong phu hop"}).raise_for_status()
    pr = requests.get(G + f"/projects/{R}", headers=biz["h"]).json()
    kiem("admin tu choi → du an huy kem ly do", pr["status"] == "cancelled" and "khong phu hop" in (pr.get("statusReason") or ""), (pr.get("status"), pr.get("statusReason")))

    # =================================================================================
    print("\n[2b] Cong link chi cho cong cu lam nhanh (gate.allowed_tools)")
    C, _ = tao_du_an(biz, "cong link mask", {f"m{i}": f"cau m{i} {TAG}" for i in range(2)}, 1, 1, 1000)
    requests.patch(G + f"/projects/{C}", headers=biz["h"], json={"labelSchema": {"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]},
        {"name": "chep", "kind": "freeText"}]}}).raise_for_status()
    requests.patch(G + f"/projects/{C}", headers=biz["h"], json={"channels": {"allowProfessional": True, "allowLinkGateway": True, "allowCollaborative": False}}).raise_for_status()
    rd = requests.get(G + f"/projects/{C}/readiness", headers=biz["h"]).json()
    kiem("checklist bao cong_cu_khong_hop_cong_link", "cong_cu_khong_hop_cong_link" in json.dumps(rd), rd)
    r = requests.post(G + f"/projects/{C}/publish", headers=biz["h"])
    kiem("publish → 409 kem ten cong cu (chep)", r.status_code == 409 and "cong_cu_khong_hop_cong_link" in r.text and "chep" in r.text, r.text)
    requests.patch(G + f"/projects/{C}", headers=biz["h"], json={"channels": {"allowProfessional": True, "allowLinkGateway": False, "allowCollaborative": False}}).raise_for_status()
    kiem("tat cong link → het vuong", "cong_cu_khong_hop_cong_link" not in json.dumps(requests.get(G + f"/projects/{C}/readiness", headers=biz["h"]).json()))

    # =================================================================================
    print("\n[3] Khieu nai CA BAI (API cua project)")
    lab = dang_ky(["labeler"], "hdd-l")
    K, _ = tao_du_an(biz, "khieu nai", {"k1": f"cau k1 {TAG}", "v1": f"cau vang {TAG}"}, vang=[("v1", "a", "qualityCheck")])
    publish_va_duyet(biz, K)
    tham_gia(lab, K)
    b, r = lam_bai(lab, K, lambda t: "b")   # sai cau vang → truot
    r.raise_for_status()
    ket_thuc_som(biz, K)
    kq = cho_cham(lab, K)
    kiem("sai cau vang → bai truot, khieu nai duoc", kq and kq["passed"] is False and kq["canAppeal"], kq)
    requests.post(G + f"/tasks/attempts/{b['attemptId']}/appeal", headers=lab["h"], json={"message": "Toi chon dung"}).raise_for_status()
    kiem("labeler khieu nai → co trong danh sach khieu nai dang mo", khieu_nai("open", b["attemptId"]) is not None)
    requests.post(G + f"/tasks/admin/attempts/{b['attemptId']}/appeal/resolve", headers=admin, json={"accept": True, "note": "Chap nhan"}).raise_for_status()
    kiem("phan xu → sang accepted", khieu_nai("open", b["attemptId"]) is None and khieu_nai("accepted", b["attemptId"]) is not None)

    # =================================================================================
    print("\n[4] Link bi bao cao (API cua link)")
    sh = dang_ky(["labeler"], "hdd-sh")
    lk = requests.post(G + "/links", headers=sh["h"], json={"url": f"https://example.com/hdd-{TAG}"}).json()
    dat_va_cho("link.report_review_threshold", 1, ["link"])
    requests.post(URL_LINK + f"/links/r/{lk['code']}/report", headers={"X-Forwarded-For": "198.51.100.77"}, json={"reason": "lua dao"})
    d = cho(lambda: link_cho_kiem(lk["id"]), 20)
    kiem("link vuot nguong bao cao → vao hang kiem duyet cua link (1 bao cao)", d and d["reportCount"] == 1, d)
    requests.post(G + f"/links/admin/{lk['id']}/dismiss-reports", headers=admin).raise_for_status()
    kiem("admin bo qua bao cao → roi hang kiem duyet", link_cho_kiem(lk["id"]) is None)

    # =================================================================================
    print("\n[4b] Gioi thieu nghi lam dung → cho admin xet, chua tinh hoa hong")
    ref = requests.get(G + "/links/referrals/me", headers=sh["h"]).json()
    moi = dang_ky(["labeler"], "hdd-moi")
    time.sleep(1.5)  # user.registered toi link-svc
    r = requests.post(G + "/links/referrals/claim", headers=moi["h"], json={"code": ref["code"]})
    kiem("nhap ma tu CUNG IP nguoi gioi thieu (da tao link) → van 204 (khong chan nguoi dung)", r.status_code == 204, r.text)
    nn = requests.get(G + "/links/admin/referrals/flagged", headers=admin).json()
    it = next((x for x in nn if x["referredId"] == moi["id"]), None)
    kiem("bi danh dau trung_ip_nguoi_gioi_thieu", it and "trung_ip_nguoi_gioi_thieu" in it["reasons"], nn[:2])
    time.sleep(2)
    # 3.32: ledger doc view v_referrals cua link (chi quan he active) — dang cho duyet thi view khong tra ve.
    kiem("ledger CHUA thay quan he gioi thieu (chua tinh hoa hong)", sql("link", f"select count(*) from v_referrals where referred_id='{moi['id']}'") == "0")
    kiem("sharer khong goi duoc API admin", requests.get(G + "/links/admin/referrals/flagged", headers=sh["h"]).status_code == 403)
    requests.post(G + f"/links/admin/referrals/{moi['id']}/approve", headers=admin).raise_for_status()
    kiem("admin chap nhan → ledger ghi quan he (bat dau tinh hoa hong)",
         cho(lambda: sql("link", f"select referrer_id from v_referrals where referred_id='{moi['id']}'") == sh["id"], 20))
    kiem("roi danh sach nghi lam dung", all(x["referredId"] != moi["id"] for x in requests.get(G + "/links/admin/referrals/flagged", headers=admin).json()))
    kiem("xet lai → 409", requests.post(G + f"/links/admin/referrals/{moi['id']}/reject", headers=admin, json={"reason": "x"}).status_code == 409)

    # =================================================================================
    print("\n[5] Mau bai toan (FM-03)")
    ma = f"ner-{TAG}".lower()[:60]
    mau = {"code": ma, "name": "Gan the thuc the tieng Viet", "description": "Nguoi, dia diem",
           "labelSchema": {"modality": "text",
                           "tools": [{"name": "ner", "kind": "span", "classes": ["PER", "LOC"], "question": "Boi den ten nguoi, dia diem (ca cum tu)"}]},
           "suggestedUnitPriceVnd": 800, "suggestedRedundancy": 3}
    r = requests.post(G + "/admin/templates", headers=admin, json=mau)
    kiem("admin tao mau → 201, version 1", r.status_code == 201 and r.json()["version"] == 1 and r.json()["modality"] == "text", r.text)
    mid = r.json().get("id")
    kiem("trung code → 409", requests.post(G + "/admin/templates", headers=admin, json=mau).status_code == 409)
    sai = dict(mau, code=ma[:50] + "-sai", labelSchema={"modality": "text", "tools": [{"name": "x", "kind": "khong-co", "classes": ["a"]}]})
    r = requests.post(G + "/admin/templates", headers=admin, json=sai)
    kiem("tap nhan sai (cung bo kiem cua project-svc) → 400", r.status_code == 400 and "tap_nhan_khong_hop_le" in r.text, r.text)
    kiem("doanh nghiep khong tao duoc mau → 403", requests.post(G + "/admin/templates", headers=biz["h"], json=mau).status_code == 403)
    ds_mau = requests.get(G + "/templates?modality=text", headers=biz["h"]).json()
    kiem("doanh nghiep thay mau dang bat", any(m["id"] == mid for m in ds_mau), ds_mau[:1])
    m1 = requests.get(G + f"/templates/{mid}", headers=biz["h"]).json()
    nhap = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e] tu mau {TAG}", "description": m1["description"],
                                                               "modality": m1["modality"], "visibility": "public"}).json()["id"]
    r = requests.patch(G + f"/projects/{nhap}", headers=biz["h"], json={"labelSchema": m1["labelSchema"]})
    kiem("tap nhan cua mau ap thang vao du an nhap → 200 (cung dinh dang)", r.status_code == 200, r.text)
    r = requests.put(G + f"/admin/templates/{mid}", headers=admin, json=dict(mau, name="NER v2"))
    kiem("sua mau → version 2", r.status_code == 200 and r.json()["version"] == 2 and r.json()["name"] == "NER v2", r.text)
    requests.post(G + f"/admin/templates/{mid}/deactivate", headers=admin).raise_for_status()
    kiem("tat mau → doanh nghiep khong thay / 404", all(m["id"] != mid for m in requests.get(G + "/templates", headers=biz["h"]).json())
         and requests.get(G + f"/templates/{mid}", headers=biz["h"]).status_code == 404)
    kiem("admin van thay mau da tat", any(m["id"] == mid and not m["active"] for m in requests.get(G + "/admin/templates", headers=admin).json()))

    # =================================================================================
    print("\n[6] Nhat ky audit chi ghi them o muc CSDL (P-03)")
    cho(lambda: sql("admin", "select count(*) from audit_events where occurred_at > now() - interval '1 hour'") not in ("", "0"), 20)
    loi = sql("admin", "update audit_events set producer = 'gia mao' where event_id = (select event_id from audit_events limit 1)")
    kiem("UPDATE audit_events bi chan", sql("admin", "select count(*) from audit_events where producer = 'gia mao'") == "0", loi)
    truoc = sql("admin", "select count(*) from audit_events where occurred_at > now() - interval '1 hour'")
    sql("admin", "delete from audit_events where occurred_at > now() - interval '1 hour'")
    kiem("DELETE dong moi (chua qua han luu) bi chan", sql("admin", "select count(*) from audit_events where occurred_at > now() - interval '1 hour'") == truoc, truoc)
    eid = str(uuid.uuid4())
    sql("admin", f"insert into audit_events (event_id, event_type, producer, occurred_at, correlation_id, payload, received_at) "
                 f"values ('{eid}', 'e2e.cu', 'e2e', now() - interval '30 days', '{eid}', '{{}}', now())")
    sql("admin", f"delete from audit_events where event_id = '{eid}'")
    kiem("DELETE dong qua han (nhu job don nhat ky) van duoc", sql("admin", f"select count(*) from audit_events where event_id = '{eid}'") == "0")
    sql("admin", "truncate audit_events")
    kiem("TRUNCATE bi chan", sql("admin", "select count(*) from audit_events") not in ("", "0"))

    # =================================================================================
    print("\n[7] Loi he thong: khong co man hinh DLQ tho; chi the 'Tinh trang he thong' bang loi (3.29 — xem e2e_ba_loai_loi.py)")
    kiem("GET /admin/ops/queues (ten hang doi tho) khong con", requests.get(G + "/admin/ops/queues", headers=admin).status_code in (404, 405))
    tt = requests.get(G + "/admin/system-health", headers=admin)
    kiem("GET /admin/system-health tra trang thai bang ma de hien loi thuong", tt.status_code == 200 and tt.json()["status"] in ("ok", "co_loi", "khong_ro"), tt.text)
    st = requests.get(G + "/admin/settings/ops.dlq_check_interval", headers=admin)
    kiem("setting theo doi loi he thong nam o nhom Ky thuat (muc Nang cao)", st.status_code == 200 and st.json()["group"] == "Kỹ thuật", st.text[:200])
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
