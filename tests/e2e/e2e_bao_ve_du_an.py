# E2E BAO VE DU AN + QUAN HE DOANH NGHIEP - LABELER:
#   FM-04 don gia toi thieu; muc 2.9 noi dung nhay cam (xac minh du tuoi, dong y canh bao, ep nghi giua phien,
#   roi du an); FP-01 NDA dien tu; FP-05 chi labeler da xac minh; loi moi cho (email chua co tai khoan / can
#   tu dong y); FB-23 danh sach chan / uu tien cap doanh nghiep + danh gia; FL-02 goi y du an; don xin het han.
# Chay: python e2e_bao_ve_du_an.py   (stack demo: E2E_PROFILE=demo)
import hashlib
import base64, json, os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from tai_khoan_e2e import xac_thuc_email  # noqa: E402

ANH = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
SO = [0]


def xac_minh(u, ngay_sinh="1995-05-05"):
    """Ho so xac minh danh tinh + admin duyet + lam moi token."""
    SO[0] += 1
    so_giay_to = "0790" + TAG.translate(str.maketrans("abcdef", "123456"))[:6] + "%02d" % SO[0]
    requests.put(G + "/me/identity-verification", headers=u["h"], json={"fullName": "Nguoi " + u["email"][:8], "dateOfBirth": ngay_sinh, "idNumber": so_giay_to}).raise_for_status()
    for mat in ("front", "back"):
        r = requests.post(G + "/me/identity-verification/upload-url", headers=u["h"], json={"side": mat, "fileName": mat + ".png", "sizeBytes": len(ANH)})
        requests.put(r.json()["uploadUrl"], data=ANH).raise_for_status()
    requests.post(G + "/me/identity-verification/submit", headers=u["h"]).raise_for_status()
    requests.post(G + f"/users/admin/identity-verifications/{u['id']}/approve", headers=admin).raise_for_status()
    lam_moi_token(u)


def thanh_vien(chu, pid):
    return {m["userId"]: m for m in requests.get(G + f"/projects/{pid}/members", headers=chu["h"]).json()}


def nap_tien(biz, so):
    i = requests.post(G + "/payments/deposits", headers={**biz["h"], "Idempotency-Key": uuid.uuid4().hex}, json={"amountVnd": so}).json()["intentId"]
    requests.post(G + f"/payments/sandbox/checkout/{i}/pay?amount={so}").raise_for_status()
    cho(lambda: vi(biz)["businessAvailableVnd"] >= so, 20)


try:
    biz = dang_ky(["business"], "bv-biz")
    nap_tien(biz, 3000000)

    # =================================================================================
    print("\n[1] FM-04 tien thuong toi thieu (project.min_reward_vnd) — gia tinh cho CA DU AN")
    dat_va_cho("project.min_reward_vnd", 20000, ["project"])
    r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e] gia {TAG}", "description": "x", "modality": "text", "visibility": "public"})
    p0 = r.json()["id"]
    gia = lambda thuong: requests.put(G + f"/projects/{p0}/pricing", headers=biz["h"], json={
        "rewardVnd": thuong, "slots": 3, "passThresholdPercent": 60, "deadline": "2027-12-31T00:00:00Z"})
    r = gia(15000)
    kiem("thuong 15.000 < toi thieu 20.000 → bi chan (luu hoac checklist)",
         (r.status_code == 400 and "thuong" in r.text) or "thuong_duoi_toi_thieu" in requests.get(G + f"/projects/{p0}/readiness", headers=biz["h"]).json()["missing"], r.text)
    r = gia(25000)
    kiem("thuong 25.000 → 200", r.status_code == 200, r.text)
    dat_va_cho("project.min_reward_vnd", 30000, ["project"])
    kiem("admin nang muc sau khi cau hinh → readiness bao thuong_duoi_toi_thieu",
         "thuong_duoi_toi_thieu" in requests.get(G + f"/projects/{p0}/readiness", headers=biz["h"]).json()["missing"])
    dat_va_cho("project.min_reward_vnd", 10000, ["project"])

    # =================================================================================
    print("\n[2] Du an noi dung nhay cam + NDA (muc 2.9, FP-01)")
    P, _ = tao_du_an(biz, "Nhay cam", {f"n{i}": f"Mau nhay cam {i}" for i in range(1, 9)}, 1, 1, 5000)
    r = requests.put(G + f"/projects/{P}/protection", headers=biz["h"], json={"sensitiveContent": True, "ndaRequired": True, "ndaText": "Khong sao chep, khong tiet lo."})
    kiem("nhay cam ma thieu loi canh bao → 400", r.status_code == 400 and "thieu_canh_bao" in r.text, r.text)
    r = requests.put(G + f"/projects/{P}/protection", headers=biz["h"], json={"sensitiveContent": True, "contentWarning": "Co hinh anh bao luc.", "ndaRequired": True, "ndaText": "Khong sao chep, khong tiet lo."})
    kiem("cau hinh bao ve → consentRequired, ndaVersion 1", r.status_code == 200 and r.json()["consentRequired"] and r.json()["ndaVersion"] == 1, r.text)
    publish_va_duyet(biz, P)
    kiem("sua bao ve sau publish → 409", requests.put(G + f"/projects/{P}/protection", headers=biz["h"],
         json={"sensitiveContent": False, "ndaRequired": False}).status_code == 409)

    chua = dang_ky(["labeler"], "bv-chua")
    r = requests.post(G + f"/projects/{P}/join", headers=chua["h"])
    kiem("chua xac minh danh tinh → 409 can_xac_minh_danh_tinh", r.status_code == 409 and "can_xac_minh_danh_tinh" in r.text, r.text)
    tre = dang_ky(["labeler"], "bv-tre")
    xac_minh(tre, "2012-01-01")
    r = requests.post(G + f"/projects/{P}/join", headers=tre["h"])
    kiem("da xac minh nhung 14 tuoi → 409 chua_du_tuoi", r.status_code == 409 and "chua_du_tuoi" in r.text, r.text)

    lab = dang_ky(["labeler"], "bv-lab")
    xac_minh(lab)
    r = requests.post(G + f"/projects/{P}/join", headers=lab["h"])
    kiem("du tuoi nhung chua dong y → 409 can_dong_y_dieu_khoan", r.status_code == 409 and "can_dong_y_dieu_khoan" in r.text, r.text)
    t = requests.get(G + f"/projects/{P}/terms", headers=lab["h"]).json()
    kiem("terms tra van ban NDA + canh bao, chua dong y", t["ndaText"].startswith("Khong sao chep") and t["myConsentValid"] is False, t)
    r = requests.post(G + f"/projects/{P}/consent", headers=lab["h"], json={"acceptContentWarning": True, "acceptNda": True, "fullName": " "})
    kiem("ky NDA thieu ho ten → 400 thieu_ho_ten_ky", r.status_code == 400 and "thieu_ho_ten_ky" in r.text, r.text)
    r = requests.post(G + f"/projects/{P}/consent", headers=lab["h"], json={"acceptContentWarning": True, "acceptNda": True, "fullName": "Nguyen Van Lab"})
    kiem("dong y + ky NDA → myConsentValid", r.status_code == 200 and r.json()["myConsentValid"] is True, r.text)
    r = requests.post(G + f"/projects/{P}/join", headers=lab["h"])
    kiem("vao du an sau khi dong y", r.status_code == 200, r.text)
    c = requests.get(G + f"/projects/{P}/consents", headers=biz["h"]).json()
    kiem("chu du an thay chu ky NDA (ten, ban bam, IP)", any(x["userId"] == lab["id"] and x["ndaSignedName"] == "Nguyen Van Lab" and len(x["ndaTextHash"]) == 64 and x["ip"] for x in c), c)
    kiem("labeler khong xem duoc danh sach chu ky", requests.get(G + f"/projects/{P}/consents", headers=lab["h"]).status_code == 403)
    b = bat_dau(lab, P, 20)
    kiem("bai cua du an nhay cam co sensitiveContent = true (frontend lam mo)", isinstance(b, dict) and b.get("sensitiveContent") is True, b)

    print("\n[3] Bo bai: tra suat, bat dau lai duoc bai moi")
    r = requests.post(G + f"/tasks/attempts/{b['attemptId']}/abandon", headers=lab["h"])
    kiem("bo bai → 200", r.status_code in (200, 204), r.text)
    b2 = bat_dau(lab, P, 20)
    kiem("bat dau lai → bai MOI", isinstance(b2, dict) and b2["attemptId"] != b["attemptId"], b2)

    print("\n[4] Roi du an bat ky luc nao")
    r = requests.post(G + f"/projects/{P}/leave", headers=lab["h"])
    kiem("labeler roi du an → 204", r.status_code == 204, r.text)
    kiem("khong con la thanh vien", lab["id"] not in thanh_vien(biz, P))
    kiem("roi du an → bai dang lam bi huy",
         cho(lambda: sql("task", f"select state from attempts where id='{b2['attemptId']}'") == "Cancelled", 20),
         sql("task", f"select state from attempts where id='{b2['attemptId']}'"))
    kiem("vao lai duoc (da dong y tu truoc)", requests.post(G + f"/projects/{P}/join", headers=lab["h"]).status_code == 200)

    # =================================================================================
    print("\n[5] Loi moi cho: du an can tu dong y; email chua co tai khoan")
    moi = dang_ky(["labeler"], "bv-moi")
    xac_minh(moi)
    r = requests.post(G + f"/projects/{P}/members/batch", headers=biz["h"], json={"role": "labeler", "userIds": [moi["id"]]})
    j = r.json()
    kiem("moi vao du an can dong y → pending_consent + invitationId", r.status_code == 200 and j["results"][0]["status"] == "pending_consent" and j["invited"] == 1, j)
    kiem("chua thanh thanh vien", moi["id"] not in thanh_vien(biz, P))
    ds_moi = requests.get(G + f"/projects/{P}/invitations", headers=biz["h"]).json()
    kiem("chu du an thay loi moi theo userId KHONG kem email cua nguoi do (S-16)",
         any(x["userId"] == moi["id"] and x["email"] is None and not x["byEmail"] for x in ds_moi), ds_moi)
    lm = requests.get(G + "/projects/invitations/mine", headers=moi["h"]).json()
    kiem("nguoi duoc moi thay loi moi (requiresConsent)", any(x["projectId"] == P and x["requiresConsent"] for x in lm), lm)
    iid = [x for x in lm if x["projectId"] == P][0]["invitationId"]
    r = requests.post(G + f"/projects/invitations/{iid}/accept", headers=moi["h"])
    kiem("chap nhan khi chua dong y → 409 can_dong_y_dieu_khoan", r.status_code == 409 and "can_dong_y_dieu_khoan" in r.text, r.text)
    kiem("nguoi duoc moi doc duoc dieu khoan du chua vao", requests.get(G + f"/projects/{P}/terms", headers=moi["h"]).status_code == 200)
    requests.post(G + f"/projects/{P}/consent", headers=moi["h"], json={"acceptContentWarning": True, "acceptNda": True, "fullName": "Tran Moi"}).raise_for_status()
    r = requests.post(G + f"/projects/invitations/{iid}/accept", headers=moi["h"])
    kiem("dong y roi chap nhan → thanh vien", r.status_code == 200 and moi["id"] in thanh_vien(biz, P), r.text)

    Q, _ = tao_du_an(biz, "Thuong", {f"q{i}": f"Mau thuong {i}" for i in range(1, 5)}, 1, 1, 5000)
    publish_va_duyet(biz, Q)
    email_moi = f"e2e-{TAG}-chuaco@crowd.local"
    r = requests.post(G + f"/projects/{Q}/members", headers=biz["h"], json={"role": "labeler", "email": email_moi.upper()})
    kiem("moi email chua co tai khoan → 202 invitation", r.status_code == 202 and r.json()["email"] == email_moi and r.json()["userId"] is None, r.text)
    kiem("chu du an thay loi moi cho", any(x["email"] == email_moi and x["status"] == "pending" for x in requests.get(G + f"/projects/{Q}/invitations", headers=biz["h"]).json()))
    requests.post(G + "/auth/register", json={"email": email_moi, "username": "u" + hashlib.sha1((email_moi).encode()).hexdigest()[:20], "password": PW, "displayName": "moi", "roles": ["labeler"]}).raise_for_status()
    t_moi = login_raw(email_moi).json()
    uid_moi = requests.get(G + "/me", headers=hdr(t_moi["accessToken"])).json()["userId"]
    time.sleep(2)
    kiem("dang ky nhung CHUA xac thuc email → chua vao (chong dang ky ho email nguoi khac)", uid_moi not in thanh_vien(biz, Q))
    xac_thuc_email(email_moi)
    kiem("xac thuc email → tu vao du an (loi moi accepted)", cho(lambda: uid_moi in thanh_vien(biz, Q), 30), thanh_vien(biz, Q).keys())

    # =================================================================================
    print("\n[6] FB-23 danh sach chan / uu tien, danh gia")
    l2 = dang_ky(["labeler"], "bv-l2")
    tham_gia(l2, Q)
    kiem("l2 vao du an Q", l2["id"] in thanh_vien(biz, Q))
    r = requests.put(G + f"/projects/labeler-lists/{l2['id']}", headers=biz["h"], json={"kind": "blocked", "note": "spam"})
    kiem("chan l2 o cap doanh nghiep → chan o 1 du an dang tham gia", r.status_code == 200 and r.json()["affectedProjects"] == 1, r.text)
    kiem("trang thai l2 o Q: blocked", thanh_vien(biz, Q)[l2["id"]]["state"] == "blocked")
    R, _ = tao_du_an(biz, "Khac", {f"r{i}": f"Mau khac {i}" for i in range(1, 4)}, 1, 1, 5000)
    publish_va_duyet(biz, R)
    r = requests.post(G + f"/projects/{R}/join", headers=l2["h"])
    kiem("l2 vao du an KHAC cua doanh nghiep → 409 bi_chan_boi_doanh_nghiep", r.status_code == 409 and "bi_chan_boi_doanh_nghiep" in r.text, r.text)
    r = requests.post(G + f"/projects/{R}/members/batch", headers=biz["h"], json={"role": "labeler", "userIds": [l2["id"]]})
    kiem("moi l2 → bi_chan_boi_doanh_nghiep", r.json()["results"][0]["status"] == "bi_chan_boi_doanh_nghiep", r.text)
    kiem("goi y khong co du an cua doanh nghiep da chan", all(x["id"] not in (Q, R) for x in requests.get(G + "/projects/recommended?limit=50", headers=l2["h"]).json()))
    r = requests.put(G + f"/projects/labeler-lists/{l2['id']}", headers=biz["h"], json={"kind": "preferred"})
    kiem("doi sang uu tien → mo lai o Q", r.json()["affectedProjects"] == 1 and thanh_vien(biz, Q)[l2["id"]]["state"] == "active", r.text)

    A, _ = tao_du_an(biz, "Duyet", {f"a{i}": f"Mau duyet {i}" for i in range(1, 4)}, 1, 1, 5000)
    requests.put(G + f"/projects/{A}", headers=biz["h"], json={"name": f"[e2e] Duyet {TAG}", "description": "x", "visibility": "approval"}).raise_for_status()
    publish_va_duyet(biz, A)
    r = requests.post(G + f"/projects/{A}/join-requests", headers=l2["h"], json={"message": "xin vao"})
    kiem("labeler uu tien gui don → tu duyet, vao luon", r.status_code in (200, 201) and r.json()["status"] == "approved" and l2["id"] in thanh_vien(biz, A), r.text)
    l3 = dang_ky(["labeler"], "bv-l3")
    r = requests.post(G + f"/projects/{A}/join-requests", headers=l3["h"], json={"message": "xin vao"})
    kiem("labeler thuong gui don → cho duyet", r.json()["status"] == "pending", r.text)

    r = requests.put(G + f"/projects/{Q}/ratings/{l2['id']}", headers=biz["h"], json={"stars": 4, "comment": "Nhanh, can than"})
    kiem("danh gia l2 4 sao", r.status_code == 200 and r.json()["stars"] == 4, r.text)
    requests.put(G + f"/projects/{A}/ratings/{l2['id']}", headers=biz["h"], json={"stars": 5}).raise_for_status()
    r = requests.put(G + f"/projects/{Q}/ratings/{l3['id']}", headers=biz["h"], json={"stars": 1})
    kiem("danh gia nguoi khong lam du an → 409", r.status_code == 409 and "khong_phai_labeler_cua_du_an" in r.text, r.text)
    kiem("so sao 6 → 400", requests.put(G + f"/projects/{Q}/ratings/{l2['id']}", headers=biz["h"], json={"stars": 6}).status_code == 400)
    s = requests.get(G + f"/projects/labelers/{l2['id']}/rating-summary", headers=biz["h"]).json()
    kiem("tong hop: trung binh 4.5 tu 2 danh gia", s["count"] == 2 and s["average"] == 4.5 and s["distribution"] == [0, 0, 0, 1, 1], s)
    kiem("chinh labeler xem duoc tong hop", requests.get(G + f"/projects/labelers/{l2['id']}/rating-summary", headers=l2["h"]).status_code == 200)
    kiem("labeler khac khong xem duoc", requests.get(G + f"/projects/labelers/{l2['id']}/rating-summary", headers=l3["h"]).status_code == 403)
    r = requests.post(G + f"/projects/{R}/members/invite-preferred", headers=biz["h"])
    kiem("moi ca danh sach uu tien vao du an R", r.status_code == 200 and l2["id"] in thanh_vien(biz, R), r.text)

    # =================================================================================
    print("\n[7] FL-02 goi y du an")
    ky = dang_ky(["labeler"], "bv-ky")
    requests.put(G + "/me/skills", headers=ky["h"], json={"domains": ["Y te"], "languages": ["vi"], "modalities": ["text"]}).raise_for_status()
    requests.put(G + f"/projects/{R}/tags", headers=biz["h"], json={"domains": ["y te", "giao thong"], "languages": ["vi"]}).raise_for_status()
    g = requests.get(G + "/projects/recommended?limit=50", headers=ky["h"]).json()
    rr = [x for x in g if x["id"] == R]
    kiem("du an R duoc goi y voi diem 7 (text +3, y te +2, vi +2)", rr and rr[0]["score"] == 7 and "domain:y te" in rr[0]["reasons"], rr)
    kiem("du an diem cao xep truoc", g and g[0]["score"] >= (rr[0]["score"] if rr else 0), [x["score"] for x in g][:5])
    kiem("du an nhay cam KHONG goi y cho nguoi chua xac minh", all(x["id"] != P for x in g))

    # =================================================================================
    print("\n[8] Don xin tham gia het han (project.join_request_ttl)")
    don = [d for d in requests.get(G + "/projects/join-requests/mine", headers=l3["h"]).json() if d["projectId"] == A][0]
    sql("project", f"update join_requests set created_at = now() - interval '30 days' where id = '{don['id']}'")
    dat_va_cho("project.housekeeping_interval", 10, ["project"])
    ok = cho(lambda: [d for d in requests.get(G + "/projects/join-requests/mine", headers=l3["h"]).json() if d["id"] == don["id"]][0]["status"] == "expired", 40)
    kiem("don cho qua 14 ngay → expired", ok)
    r = requests.post(G + f"/projects/{A}/join-requests", headers=l3["h"], json={"message": "xin lai"})
    kiem("het han roi gui lai duoc ngay", r.status_code in (200, 201) and r.json()["status"] == "pending", r.text)
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
