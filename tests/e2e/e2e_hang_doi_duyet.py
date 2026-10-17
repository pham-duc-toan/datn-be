# E2E VAN HANH ADMIN:
#   hang doi duyet chung (KYB, du an, khieu nai, link bi bao cao) dung tu event; mau bai toan (FM-03);
#   nhat ky audit chi ghi them o muc CSDL (P-03); do sau DLQ + canh bao ops.dlq_alert (VD-D-06/12).
# Chay: python e2e_hang_doi_duyet.py   (stack demo: E2E_PROFILE=demo)
import json, os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER, URL_LINK  # noqa: E402

MGMT = (RABBIT_USER, RABBIT_PASSWORD)


def don(kind, subject, status="all"):
    r = requests.get(G + f"/admin/approvals?kind={kind}&status={status}&pageSize=200", headers=admin)
    return next((x for x in r.json()["items"] if x["subjectId"] == subject), None)


def cho_don(kind, subject, status, giay=30):
    return cho(lambda: (lambda d: d if d and d["status"] == status else None)(don(kind, subject)), giay)


try:
    # =================================================================================
    print("\n[1] Hang doi duyet: KYB")
    r = requests.post(G + "/admin/approvals/rebuild", headers=admin)
    kiem("dung lai hang doi tu audit → 200", r.status_code == 200 and r.json()["appliedEvents"] >= 0, r.text)
    biz = dang_ky(["business"], "hdd", xac_minh=False)
    nop_kyb(biz, "Cong ty Hang Doi").raise_for_status()
    d = cho_don("kyb", biz["id"], "pending")
    kiem("nop KYB → muc cho duyet kyb (ten cong ty, nguoi xin)", d and d["title"] == "Cong ty Hang Doi" and d["requesterId"] == biz["id"], d)
    tt = requests.get(G + "/admin/approvals/summary", headers=admin).json()
    kiem("tom tat dem duoc don kyb cho duyet", next(k for k in tt["kinds"] if k["kind"] == "kyb")["pending"] >= 1, tt)
    kiem("nguoi khong phai admin → 403", requests.get(G + "/admin/approvals", headers=biz["h"]).status_code == 403)
    ct_kyb = requests.get(G + f"/kyb/admin/{biz['id']}", headers=admin).json()
    kiem("S-07: admin lay link xem giay phep → nhat ky audit ghi lan xem (ai xem, giay to cua ai)",
         cho(lambda: sql("admin", "select count(*) from audit_events where event_type = 'sensitive_document.viewed' "
                                  f"and payload->>'subjectUserId' = '{biz['id']}' and actor_role = 'admin'") == "1", 20), ct_kyb.get("licenseUrl"))
    requests.post(G + f"/kyb/admin/{biz['id']}/approve", headers=admin).raise_for_status()
    d = cho_don("kyb", biz["id"], "approved")
    kiem("admin duyet → approved, ghi ai duyet", d and d["decidedBy"] and d["decidedAt"], d)
    lam_moi_token(biz)

    # =================================================================================
    print("\n[2] Hang doi duyet: du an (cho ky quy → cho duyet → tu choi / duyet)")
    vi_nap = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 500000}).json()
    requests.post(G + f"/payments/sandbox/checkout/{vi_nap['intentId']}/pay?amount=500000")
    cho(lambda: vi(biz)["businessAvailableVnd"] >= 500000, 30)
    P, _ = tao_du_an(biz, "hang doi", {f"c{i}": f"cau {i} {TAG}" for i in range(2)}, 1, 1, 1000)
    R, _ = tao_du_an(biz, "bi tu choi", {f"r{i}": f"cau r{i} {TAG}" for i in range(2)}, 1, 1, 1000)
    for x in (P, R):
        requests.post(G + f"/projects/{x}/publish", headers=biz["h"]).raise_for_status()
    d = cho_don("project", P, "pending")
    kiem("du an ky quy xong → cho duyet (ten + so tien ky quy)", d and "hang doi" in (d["title"] or "") and d["amountVnd"] == 4000, d)
    requests.post(G + f"/projects/{P}/approve", headers=admin).raise_for_status()
    kiem("admin duyet → approved", cho_don("project", P, "approved") is not None)
    cho_don("project", R, "pending")
    requests.post(G + f"/projects/{R}/reject", headers=admin, json={"reason": "Noi dung khong phu hop"}).raise_for_status()
    d = cho_don("project", R, "rejected")
    kiem("admin tu choi → rejected kem ly do", d and "khong phu hop" in (d["note"] or ""), d)

    # =================================================================================
    print("\n[2b] Cong link chi cho cong cu lam nhanh (gate.allowed_tools)")
    C, _ = tao_du_an(biz, "cong link mask", {f"m{i}": f"cau m{i} {TAG}" for i in range(2)}, 1, 1, 1000)
    requests.put(G + f"/projects/{C}/label-schema", headers=biz["h"], json={"modality": "text", "tools": [
        {"name": "nhan", "kind": "classification", "classes": ["a", "b"]},
        {"name": "chep", "kind": "freeText"}]}).raise_for_status()
    requests.put(G + f"/projects/{C}/channels", headers=biz["h"], json={"allowProfessional": True, "allowLinkGateway": True, "allowCollaborative": False}).raise_for_status()
    rd = requests.get(G + f"/projects/{C}/readiness", headers=biz["h"]).json()
    kiem("checklist bao cong_cu_khong_hop_cong_link", "cong_cu_khong_hop_cong_link" in json.dumps(rd), rd)
    r = requests.post(G + f"/projects/{C}/publish", headers=biz["h"])
    kiem("publish → 409 kem ten cong cu (chep)", r.status_code == 409 and "cong_cu_khong_hop_cong_link" in r.text and "chep" in r.text, r.text)
    requests.put(G + f"/projects/{C}/channels", headers=biz["h"], json={"allowProfessional": True, "allowLinkGateway": False, "allowCollaborative": False}).raise_for_status()
    kiem("tat cong link → het vuong", "cong_cu_khong_hop_cong_link" not in json.dumps(requests.get(G + f"/projects/{C}/readiness", headers=biz["h"]).json()))

    # =================================================================================
    print("\n[3] Hang doi duyet: khieu nai")
    lab = dang_ky(["labeler"], "hdd-l")
    tham_gia(lab, P)
    t = nhan_task(lab, P, 20)
    nop(lab, t["assignmentId"], "a").raise_for_status()
    nhan = cho(lambda: [a for a in ds_nhan(biz, P) if a["status"] == "pendingReview"], 30)
    # 409 xung_dot_dong_thoi: quality vua cap nhat nhan cung luc — client thu lai.
    r = cho(lambda: (lambda x: x if x.status_code != 409 else None)(
        requests.post(G + f"/annotations/{nhan[0]['id']}/reject", headers=biz["h"], json={"reason": "Sai nhan"})), 10)
    r.raise_for_status()
    requests.post(G + f"/annotations/{nhan[0]['id']}/appeal", headers=lab["h"], json={"message": "Toi chon dung"}).raise_for_status()
    d = cho_don("appeal", nhan[0]["id"], "pending")
    kiem("labeler khieu nai → cho admin phan xu", d and d["title"] == "Toi chon dung" and d["requesterId"] == lab["id"], d)
    requests.post(G + f"/annotations/{nhan[0]['id']}/appeal/resolve", headers=admin, json={"accept": True, "note": "Chap nhan"}).raise_for_status()
    kiem("phan xu (appeal.resolved) → approved", cho_don("appeal", nhan[0]["id"], "approved") is not None)

    # =================================================================================
    print("\n[4] Hang doi duyet: link bi bao cao")
    sh = dang_ky(["labeler"], "hdd-sh")
    lk = requests.post(G + "/links", headers=sh["h"], json={"url": f"https://example.com/hdd-{TAG}"}).json()
    dat_va_cho("link.report_review_threshold", 1, ["link"])
    requests.post(URL_LINK + f"/links/r/{lk['code']}/report", headers={"X-Forwarded-For": "198.51.100.77"}, json={"reason": "lua dao"})
    d = cho_don("link", lk["id"], "pending")
    kiem("link vuot nguong bao cao → cho kiem duyet", d and d["title"] == lk["code"] and d["note"] == "1 bao cao", d)
    requests.post(G + f"/links/admin/{lk['id']}/dismiss-reports", headers=admin).raise_for_status()
    kiem("admin bo qua bao cao → rejected", cho_don("link", lk["id"], "rejected") is not None)

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
    d = cho_don("referral", moi["id"], "pending")
    kiem("vao hang doi duyet chung (kind=referral)", d and d["requesterId"] == sh["id"], d)
    time.sleep(2)
    kiem("ledger CHUA ghi quan he gioi thieu (chua tinh hoa hong)", sql("ledger", f"select count(*) from referrals where referred_id='{moi['id']}'") == "0")
    kiem("sharer khong goi duoc API admin", requests.get(G + "/links/admin/referrals/flagged", headers=sh["h"]).status_code == 403)
    requests.post(G + f"/links/admin/referrals/{moi['id']}/approve", headers=admin).raise_for_status()
    kiem("admin chap nhan → ledger ghi quan he (bat dau tinh hoa hong)",
         cho(lambda: sql("ledger", f"select referrer_id from referrals where referred_id='{moi['id']}'") == sh["id"], 20))
    kiem("hang doi: approved", cho_don("referral", moi["id"], "approved") is not None)
    kiem("xet lai → 409", requests.post(G + f"/links/admin/referrals/{moi['id']}/reject", headers=admin, json={"reason": "x"}).status_code == 409)

    # =================================================================================
    print("\n[5] Mau bai toan (FM-03)")
    ma = f"ner-{TAG}".lower()[:60]
    mau = {"code": ma, "name": "Gan the thuc the tieng Viet", "description": "Nguoi, dia diem",
           "labelSchema": {"modality": "text", "tools": [{"name": "ner", "kind": "span", "classes": ["PER", "LOC"]}]},
           "guidelineMarkdown": "# Huong dan\nBoi den ten nguoi.", "suggestedUnitPriceVnd": 800, "suggestedRedundancy": 3}
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
    r = requests.put(G + f"/projects/{nhap}/label-schema", headers=biz["h"], json=m1["labelSchema"])
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
    print("\n[7] Do sau DLQ + canh bao")
    dat_va_cho("ops.dlq_check_interval", 10, [])
    q = "admin-svc.audit.dlq"
    requests.post(f"{RABBIT_MGMT}/api/exchanges/%2F/datn.dlx/publish", auth=MGMT, json={
        "properties": {}, "routing_key": "admin-svc.audit", "payload": "{\"thu\":\"message hong e2e\"}", "payload_encoding": "string"}).raise_for_status()
    ops = cho(lambda: (lambda j: j if any(x["queue"] == q and x["overThreshold"] for x in j["deadLetterQueues"]) else None)(
        requests.get(G + "/admin/ops/queues", headers=admin).json()), 40)
    kiem("admin thay DLQ co message, vuot nguong", ops is not None and ops["reachable"], ops)
    kiem("phat ops.dlq_alert (audit)", cho(lambda: sql("admin", f"select count(*) from audit_events where event_type = 'ops.dlq_alert' and payload->>'queue' = '{q}' and occurred_at > now() - interval '5 minutes'") not in ("", "0"), 30))
    requests.delete(f"{RABBIT_MGMT}/api/queues/%2F/{q}/contents", auth=MGMT)
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
