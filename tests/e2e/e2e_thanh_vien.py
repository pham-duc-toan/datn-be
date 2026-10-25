# E2E THANH VIEN DU AN: moi truc tiep (email / userId, nhieu nguoi), ma moi, che do approval (don xin
# tham gia, ca qua bai test dau vao), kiem cap do / uy tin ngay luc vao du an, thong bao.
# Tu tao tai khoan + du an moi. Cap do / uy tin dat thang trong identity_db (bang labeler_stats) — chinh
# nguon project-svc hoi qua POST /internal/users/lookup.
# Chay: python e2e_thanh_vien.py   (stack demo: E2E_PROFILE=demo)
import os, sys, threading, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403

GIA = 10000


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def tb(u):
    return [x["title"] + " | " + x["body"] for x in requests.get(G + "/notifications/me?pageSize=100", headers=u["h"]).json()["items"]]


def co_tb(u, chu):
    return any(chu in x for x in tb(u))


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


def dat_cap(u, cap, uy_tin=None):
    """Ghi cap do / uy tin vao identity_db (nhu sau khi duoc duyet nhan va duoc cham)."""
    ut = "null" if uy_tin is None else str(uy_tin)
    sql("identity", "insert into labeler_stats(user_id, approved_count, reputation, reputation_at, level, updated_at) "
                    f"values ('{u['id']}', 0, {ut}, now(), {cap}, now()) "
                    f"on conflict (user_id) do update set level = {cap}, reputation = {ut}, reputation_at = now()")


def che_do(chu, pid, ten, vis):
    r = requests.put(G + f"/projects/{pid}", headers=chu["h"], json={"name": ten, "description": "e2e", "visibility": vis})
    assert r.status_code == 200, r.text


def du_an(chu, ten, vis, **kw):
    pid, ids = tao_du_an(chu, ten, {f"{ten}-{i}": f"Mau {i}" for i in range(4)}, 1, 1, GIA, **kw)
    che_do(chu, pid, f"[e2e-tong] {ten} {TAG}", vis)
    return pid, ids


def thanh_vien(chu, pid):
    return {m["userId"]: m for m in requests.get(G + f"/projects/{pid}/members", headers=chu["h"]).json()}


def tao_ma(chu, pid, **body):
    return requests.post(G + f"/projects/{pid}/invite-codes", headers=chu["h"], json=body)


def vao_ma(u, ma):
    return requests.post(G + "/projects/join-by-code", headers=u["h"], json={"code": ma})


def xin(u, pid, loi_nhan=None):
    return requests.post(G + f"/projects/{pid}/join-requests", headers=u["h"], json={"message": loi_nhan})


def hang_doi(chu, pid, trang_thai=None):
    return requests.get(G + f"/projects/{pid}/join-requests", headers=chu["h"], params={"status": trang_thai} if trang_thai else None).json()


try:
    biz = dang_ky(["business"], "chu")
    biz2 = dang_ky(["business"], "dn-khac")
    nap(biz, 2000000)
    L = {k: dang_ky(["labeler"], k) for k in ("l1", "l2", "l3", "l4", "l5", "l6", "l7", "l8", "thap", "chuaut")}
    for k in ("l1", "l2", "l3", "l4", "l5", "l6", "l7", "l8"):
        dat_cap(L[k], 2, 80)
    dat_cap(L["chuaut"], 3, None)

    # =================================================================================
    print("\n[1] Che do rieng tu: chu du an moi truc tiep theo email / userId, mot va nhieu nguoi")
    P1, _ = du_an(biz, "RIENG", "private")
    publish_va_duyet(biz, P1)

    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"email": "  " + L["l1"]["email"].upper() + " ", "role": "labeler"})
    co_tk = r.json() if r.status_code == 202 else {}
    kiem("moi 1 nguoi theo email (hoa / khoang trang) → 202 loi moi, KHONG kem userId (S-16)",
         r.status_code == 202 and co_tk["userId"] is None and co_tk["email"] == L["l1"]["email"].lower() and co_tk["byEmail"], r.text)
    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"email": f"khong-co-{TAG}@crowd.local", "role": "labeler"})
    chua_tk = r.json() if r.status_code == 202 else {}
    kiem("email chua co tai khoan → 202 loi moi, cung dang voi email co tai khoan (S-16)",
         r.status_code == 202 and sorted(chua_tk) == sorted(co_tk) and chua_tk["userId"] is None and chua_tk["status"] == "pending", r.text)
    kiem("l1 chua thanh thanh vien (phai tu chap nhan)", L["l1"]["id"] not in thanh_vien(biz, P1))
    lm = cho(lambda: [x for x in requests.get(G + "/projects/invitations/mine", headers=L["l1"]["h"]).json() if x["projectId"] == P1], 10)
    kiem("l1 thay loi moi trong ung dung (kem email cua minh)", lm and lm[0]["invitationId"] == co_tk.get("invitationId") and lm[0]["userId"] == L["l1"]["id"], lm)
    r = requests.post(G + f"/projects/invitations/{co_tk.get('invitationId')}/accept", headers=L["l1"]["h"])
    kiem("l1 chap nhan → thanh vien", r.status_code == 200 and L["l1"]["id"] in thanh_vien(biz, P1), r.text)
    ds_moi = requests.get(G + f"/projects/{P1}/invitations", headers=biz["h"]).json()
    kiem("chu du an: loi moi da chap nhan moi hien userId, loi moi dang cho thi khong",
         any(x["invitationId"] == co_tk.get("invitationId") and x["status"] == "accepted" and x["userId"] == L["l1"]["id"] for x in ds_moi)
         and all(x["userId"] is None for x in ds_moi if x["status"] == "pending"), ds_moi)
    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"email": L["l2"]["email"], "userId": L["l2"]["id"], "role": "labeler"})
    kiem("ca userId lan email → 400 thieu_thong_tin", r.status_code == 400 and code(r) == "thieu_thong_tin", r.text)
    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"userId": str(uuid.uuid4()), "role": "labeler"})
    kiem("userId khong ton tai → 404 (truoc day van them duoc)", r.status_code == 404, r.text)
    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"email": L["l1"]["email"], "role": "labeler"})
    kiem("moi lai thanh vien theo email → van 202 nhu moi nguoi khac (khong lo, S-16)", r.status_code == 202 and r.json()["userId"] is None, r.text)
    time.sleep(2)
    kiem("... nhung la loi moi 'cam': thanh vien khong nhan them loi moi",
         not [x for x in requests.get(G + "/projects/invitations/mine", headers=L["l1"]["h"]).json() if x["projectId"] == P1])
    r = requests.post(G + f"/projects/{P1}/members", headers=biz["h"], json={"userId": L["l1"]["id"], "role": "labeler"})
    kiem("theo userId thi van bao chi tiet: 409 da_la_thanh_vien", r.status_code == 409 and code(r) == "da_la_thanh_vien", r.text)

    lo = {"role": "labeler",
          "userIds": [L["l2"]["id"], str(uuid.uuid4())],
          "emails": [L["l3"]["email"], L["l1"]["email"], L["l2"]["email"].upper(), "khong-phai-email", biz2["email"], L["l3"]["email"].upper()]}
    r = requests.post(G + f"/projects/{P1}/members/batch", headers=biz["h"], json=lo)
    kq = r.json()["results"] if r.status_code == 200 else []
    st = [x["status"] for x in kq] if kq else r.text
    kiem("moi nhieu nguoi → userId chi tiet; MOI email hop le deu 'invited' (S-16); trung email → trung_lap",
         st == ["added", "khong_tim_thay", "invited", "invited", "invited", "email_khong_hop_le", "invited", "trung_lap"], st)
    kiem("added = 1 (kem displayName), invited = 4",
         r.status_code == 200 and r.json()["added"] == 1 and r.json()["invited"] == 4 and kq[0]["displayName"] == "l2", r.text)
    dong_email = [kq[i] for i in (2, 3, 4, 6)] if len(kq) == 8 else []
    kiem("dong email: khong userId / displayName, cung message — khong phan biet co tai khoan, da la thanh vien hay khong",
         dong_email and all(x["userId"] is None and x["displayName"] is None and x["message"] == dong_email[0]["message"] for x in dong_email), dong_email)
    lm3 = cho(lambda: [x for x in requests.get(G + "/projects/invitations/mine", headers=L["l3"]["h"]).json() if x["projectId"] == P1], 10)
    requests.post(G + f"/projects/invitations/{lm3[0]['invitationId']}/accept", headers=L["l3"]["h"]).raise_for_status()
    tv = thanh_vien(biz, P1)
    kiem("danh sach thanh vien co l1, l2, l3 (l3 sau khi tu chap nhan)", all(L[k]["id"] in tv for k in ("l1", "l2", "l3")), list(tv))
    lb = cho(lambda: [x for x in requests.get(G + "/projects/invitations/mine", headers=biz2["h"]).json() if x["projectId"] == P1], 10)
    r = requests.post(G + f"/projects/invitations/{lb[0]['invitationId']}/accept", headers=biz2["h"]) if lb else None
    kiem("doanh nghiep (khong phai labeler) chap nhan loi moi labeler → 409 khong_phai_labeler",
         r is not None and r.status_code == 409 and code(r) == "khong_phai_labeler", r.text if r is not None else lb)

    r = requests.post(G + f"/projects/{P1}/members/batch", headers=biz["h"], json={"role": "reviewer", "userIds": [biz2["id"]]})
    kiem("doanh nghiep khac lam reviewer (khong can vai tro labeler)",
         r.status_code == 200 and r.json()["results"][0]["status"] == "added" and tv.get(biz2["id"]) is None, r.text)
    kiem("reviewer thay du an rieng tu", requests.get(G + f"/projects/{P1}", headers=biz2["h"]).status_code == 200)

    dat_va_cho("project.member_batch_max", 2, ("project",))
    r = requests.post(G + f"/projects/{P1}/members/batch", headers=biz["h"], json={"role": "labeler", "emails": ["a@x.vn", "b@x.vn", "c@x.vn"]})
    kiem("qua project.member_batch_max → 400 qua_nhieu_nguoi", r.status_code == 400 and code(r) == "qua_nhieu_nguoi", r.text)
    dat_va_cho("project.member_batch_max", 100, ("project",))
    r = requests.post(G + f"/projects/{P1}/members/batch", headers=biz["h"], json={"role": "owner", "emails": [L["l4"]["email"]]})
    kiem("role owner → 400", r.status_code == 400 and code(r) == "khong_the_them_owner", r.text)
    r = requests.post(G + f"/projects/{P1}/members/batch", headers=L["l1"]["h"], json={"role": "labeler", "emails": [L["l4"]["email"]]})
    kiem("labeler (thanh vien) goi moi → 403", r.status_code == 403, r.text)

    kiem("nguoi duoc moi nhan thong bao co ten du an", cho(lambda: co_tb(L["l1"], f"RIENG {TAG}"), 20), tb(L["l1"]))
    kiem("reviewer cung nhan thong bao", cho(lambda: co_tb(biz2, "Bạn được mời vào dự án"), 20), tb(biz2))
    kiem("labeler duoc moi thay du an, nguoi ngoai → 404",
         requests.get(G + f"/projects/{P1}", headers=L["l2"]["h"]).status_code == 200
         and requests.get(G + f"/projects/{P1}", headers=L["l5"]["h"]).status_code == 404)
    r = requests.post(G + f"/projects/{P1}/join", headers=L["l5"]["h"])
    kiem("nguoi ngoai join du an rieng tu → 404 (khong lo du an)", r.status_code == 404, r.text)
    t = nhan_task(L["l3"], P1, 20)
    kiem("nguoi duoc moi theo lo nhan duoc task (member.added toi task-svc)", t is not None, t)

    # =================================================================================
    print("\n[2] Ma moi: tao, vao bang ma, het luot, thu hoi, dong thoi")
    r = tao_ma(biz, P1, maxUses=2, expiresInHours=24, note="nhom A")
    kiem("tao ma → 201, 16 ky tu, active", r.status_code == 201 and len(r.json()["code"]) == 16 and r.json()["status"] == "active", r.text)
    ma = r.json()["code"]
    go_tay = ma[:4].lower() + "-" + ma[4:8] + " " + ma[8:]
    r = vao_ma(L["l4"], go_tay)
    kiem("vao bang ma (chu thuong, gach, khoang trang) → 200", r.status_code == 200 and r.json()["projectId"] == P1 and f"RIENG {TAG}" in r.json()["projectName"], r.text)
    r = vao_ma(L["l4"], ma)
    kiem("vao lai → 409 da_la_thanh_vien, khong ton luot", r.status_code == 409 and code(r) == "da_la_thanh_vien", r.text)
    ds_ma = requests.get(G + f"/projects/{P1}/invite-codes", headers=biz["h"]).json()
    kiem("usedCount = 1", ds_ma[0]["usedCount"] == 1 and ds_ma[0]["note"] == "nhom A", ds_ma)
    kiem("labeler vao bang ma: khong co thong bao 'duoc moi' (tu bam)", not co_tb(L["l4"], "Bạn được mời vào dự án"))
    kiem("vao bang ma → nhan duoc task", nhan_task(L["l4"], P1, 20) is not None)
    r = vao_ma(L["thap"], ma)
    kiem("du an khong dat dieu kien → labeler cap 1 vao bang ma duoc", r.status_code == 200, r.text)
    r = vao_ma(L["l5"], ma)
    kiem("het 2 luot → 409 ma_moi_het_luot", r.status_code == 409 and code(r) == "ma_moi_het_luot", r.text)
    kiem("trang thai exhausted", requests.get(G + f"/projects/{P1}/invite-codes", headers=biz["h"]).json()[0]["status"] == "exhausted")
    kiem("ma khong ton tai → 404", vao_ma(L["l5"], "ABCDEFGHJKLMNPQR").status_code == 404)
    kiem("ma sai do dai → 404", vao_ma(L["l5"], "abc").status_code == 404)
    kiem("doanh nghiep vao bang ma → 403", vao_ma(biz2, ma).status_code == 403)

    r = tao_ma(biz, P1)
    m2 = r.json()
    kiem("khong dat han → 7 ngay, khong gioi han luot", r.status_code == 201 and m2["maxUses"] is None and m2["expiresAt"] > m2["createdAt"], r.text)
    r = requests.delete(G + f"/projects/{P1}/invite-codes/{m2['id']}", headers=biz["h"])
    kiem("thu hoi → revoked", r.status_code == 200 and r.json()["status"] == "revoked", r.text)
    kiem("thu hoi lan hai → 409", requests.delete(G + f"/projects/{P1}/invite-codes/{m2['id']}", headers=biz["h"]).status_code == 409)
    r = vao_ma(L["l5"], m2["code"])
    kiem("ma da thu hoi → 409 ma_moi_da_thu_hoi", r.status_code == 409 and code(r) == "ma_moi_da_thu_hoi", r.text)
    kiem("han 0 gio → 400", tao_ma(biz, P1, expiresInHours=0).status_code == 400)
    kiem("han vuot project.invite_code_max_ttl → 400", tao_ma(biz, P1, expiresInHours=24 * 400).status_code == 400)
    kiem("maxUses 0 → 400", tao_ma(biz, P1, maxUses=0).status_code == 400)
    kiem("labeler tao ma → 403", tao_ma(L["l1"], P1).status_code == 403)
    kiem("labeler xem ma → 403", requests.get(G + f"/projects/{P1}/invite-codes", headers=L["l1"]["h"]).status_code == 403)

    # 6 nguoi dung cung luc mot ma 3 luot: dung 3 nguoi vao.
    P2, _ = du_an(biz, "DONGTHOI", "private")
    r = tao_ma(biz, P2, maxUses=3)
    kiem("du an chua duoc duyet → tao ma 409 du_an_chua_duoc_duyet", r.status_code == 409 and "du_an_chua_duoc_duyet" in r.text, r.text)
    r = requests.post(G + f"/projects/{P2}/members/batch", headers=biz["h"], json={"role": "labeler", "usernames": [L["l8"]["username"]]})
    kiem("du an chua duoc duyet → them thanh vien 409", r.status_code == 409 and "du_an_chua_duoc_duyet" in r.text, r.text)
    publish_va_duyet(biz, P2)
    ma3 = tao_ma(biz, P2, maxUses=3).json()
    kq = {}

    def thu(k):
        kq[k] = vao_ma(L[k], ma3["code"]).status_code

    th = [threading.Thread(target=thu, args=(k,)) for k in ("l1", "l2", "l3", "l5", "l6", "l7")]
    [x.start() for x in th]
    [x.join() for x in th]
    kiem("6 nguoi tranh 3 luot → dung 3 nguoi vao, con lai 409", sorted(kq.values()) == [200, 200, 200, 409, 409, 409], kq)
    kiem("usedCount = 3, du an co 3 labeler",
         requests.get(G + f"/projects/{P2}/invite-codes", headers=biz["h"]).json()[0]["usedCount"] == 3
         and len([m for m in thanh_vien(biz, P2).values() if m["role"] == "labeler"]) == 3)

    # =================================================================================
    print("\n[3] Kiem cap do / uy tin ngay luc vao du an (cung ma loi task-svc)")
    DK = {"minLevel": 2, "minReputation": 50, "requireEntranceTest": False, "entranceQuestionCount": 1, "entrancePassPercent": 80}
    P3, _ = du_an(biz, "DIEUKIEN", "public", dieu_kien=DK)
    publish_va_duyet(biz, P3)
    r = requests.post(G + f"/projects/{P3}/join", headers=L["thap"]["h"])
    kiem("cap 1 join du an doi cap 2 → 409 chua_du_cap_do, co giai thich",
         r.status_code == 409 and code(r) == "chua_du_cap_do" and "tối thiểu 2, hiện tại là 1" in r.json()["detail"], r.text)
    r = requests.post(G + f"/projects/{P3}/join", headers=L["chuaut"]["h"])
    kiem("chua co diem uy tin → 409 chua_du_uy_tin", r.status_code == 409 and code(r) == "chua_du_uy_tin", r.text)
    dat_cap(L["chuaut"], 3, 40)
    r = requests.post(G + f"/projects/{P3}/join", headers=L["chuaut"]["h"])
    kiem("uy tin 40 < 50 → 409 chua_du_uy_tin", r.status_code == 409 and code(r) == "chua_du_uy_tin" and "hiện tại là 40" in r.json()["detail"], r.text)
    dat_cap(L["chuaut"], 3, 55)
    r = requests.post(G + f"/projects/{P3}/join", headers=L["chuaut"]["h"])
    kiem("du dieu kien → join 200", r.status_code == 200, r.text)
    kiem("khong co dong thanh vien cho nguoi bi tu choi", L["thap"]["id"] not in thanh_vien(biz, P3))
    r = requests.post(G + f"/projects/{P3}/members/batch", headers=biz["h"], json={"role": "labeler", "userIds": [L["thap"]["id"], L["l8"]["id"]]})
    kiem("chu du an moi nguoi chua du cap → tung dong bao chua_du_cap_do, nguoi du thi them",
         r.status_code == 200 and [x["status"] for x in r.json()["results"]] == ["chua_du_cap_do", "added"], r.text)
    ma_dk = tao_ma(biz, P3).json()["code"]
    r = vao_ma(L["thap"], ma_dk)
    kiem("ma moi khong bo qua dieu kien: cap 1 → 409 chua_du_cap_do", r.status_code == 409 and code(r) == "chua_du_cap_do", r.text)
    kiem("bi tu choi thi khong ton luot", requests.get(G + f"/projects/{P3}/invite-codes", headers=biz["h"]).json()[0]["usedCount"] == 0)

    # =================================================================================
    print("\n[4] Che do approval: don xin tham gia, duyet / tu choi / rut, cho xin lai")
    P4, _ = du_an(biz, "DUYET", "approval")
    publish_va_duyet(biz, P4)
    ten_p4 = f"DUYET {TAG}"
    ds_l = requests.get(G + "/projects", headers=L["l1"]["h"], params={"status": "running", "pageSize": 100}).json()["items"]
    kiem("labeler thay du an approval trong danh sach", any(p["id"] == P4 and p["visibility"] == "approval" for p in ds_l), [p["id"] for p in ds_l][:5])
    r = requests.post(G + f"/projects/{P4}/join", headers=L["l1"]["h"])
    kiem("join thang → 409 can_xin_duyet", r.status_code == 409 and code(r) == "can_xin_duyet", r.text)
    r = xin(L["l1"], P4, "Em co 2 nam kinh nghiem gan nhan")
    kiem("gui don → 201 pending", r.status_code == 201 and r.json()["status"] == "pending", r.text)
    d1 = r.json()
    r = xin(L["l1"], P4)
    kiem("gui don lan hai → 409 da_co_don_dang_cho", r.status_code == 409 and code(r) == "da_co_don_dang_cho", r.text)
    r = xin(L["thap"], P4)
    kiem("du an approval khong dat dieu kien → cap 1 gui duoc", r.status_code == 201, r.text)
    kiem("chu du an nhan thong bao don moi", cho(lambda: co_tb(biz, f"{ten_p4}”."), 20), tb(biz)[:3])
    hd = hang_doi(biz, P4)
    kiem("hang doi kem ten, cap do, uy tin, loi nhan",
         any(x["id"] == d1["id"] and x["displayName"] == "l1" and x["level"] == 2 and x["reputation"] == 80 and x["message"].startswith("Em co") for x in hd), hd)
    kiem("labeler xem hang doi → 403", requests.get(G + f"/projects/{P4}/join-requests", headers=L["l1"]["h"]).status_code == 403)
    mine = requests.get(G + "/projects/join-requests/mine", headers=L["l1"]["h"]).json()
    kiem("don cua toi co ten du an", any(x["id"] == d1["id"] and ten_p4 in x["projectName"] for x in mine), mine)
    kiem("chua duoc duyet → bat dau bai 403", requests.post(G + f"/tasks/projects/{P4}/attempt", headers=L["l1"]["h"]).status_code == 403)

    r = requests.post(G + f"/projects/{P4}/join-requests/{d1['id']}/approve", headers=biz["h"])
    kiem("duyet → approved", r.status_code == 200 and r.json()["status"] == "approved", r.text)
    kiem("duyet lai → 409 don_da_xu_ly", requests.post(G + f"/projects/{P4}/join-requests/{d1['id']}/approve", headers=biz["h"]).status_code == 409)
    kiem("duoc duyet → thanh vien, nhan duoc task", L["l1"]["id"] in thanh_vien(biz, P4) and nhan_task(L["l1"], P4, 20) is not None)
    kiem("labeler nhan thong bao duoc duyet", cho(lambda: co_tb(L["l1"], "Đơn xin tham gia được duyệt"), 20), tb(L["l1"])[:3])

    d2 = xin(L["l2"], P4).json()
    r = requests.post(G + f"/projects/{P4}/join-requests/{d2['id']}/reject", headers=biz["h"], json={"reason": "Du an da du nguoi"})
    kiem("tu choi kem ly do", r.status_code == 200 and r.json()["status"] == "rejected" and r.json()["rejectReason"] == "Du an da du nguoi"
         and r.json()["canRequestAgainAt"], r.text)
    kiem("labeler nhan thong bao tu choi + ly do", cho(lambda: co_tb(L["l2"], "Lý do: Du an da du nguoi"), 20), tb(L["l2"])[:3])
    r = xin(L["l2"], P4)
    kiem("xin lai ngay → 409 chua_duoc_xin_lai", r.status_code == 409 and code(r) == "chua_duoc_xin_lai", r.text)
    dat_va_cho("project.join_request_cooldown", 0, ("project",))
    r = xin(L["l2"], P4)
    kiem("cooldown = 0 → xin lai duoc", r.status_code == 201, r.text)
    kiem("hang doi 'rejected' co don cu", any(x["id"] == d2["id"] for x in hang_doi(biz, P4, "rejected")))

    d3 = xin(L["l3"], P4).json()
    r = requests.delete(G + f"/projects/{P4}/join-requests/mine", headers=L["l3"]["h"])
    kiem("labeler rut don → cancelled", r.status_code == 200 and r.json()["status"] == "cancelled" and r.json()["id"] == d3["id"], r.text)
    kiem("rut lan hai → 404", requests.delete(G + f"/projects/{P4}/join-requests/mine", headers=L["l3"]["h"]).status_code == 404)

    d5 = xin(L["l5"], P4).json()
    r = requests.post(G + f"/projects/{P4}/members", headers=biz["h"], json={"userId": L["l5"]["id"], "role": "labeler"})
    kiem("chu du an moi thang nguoi dang co don → don tu chuyen approved",
         r.status_code == 200 and any(x["id"] == d5["id"] for x in hang_doi(biz, P4, "approved")), r.text)
    d6 = xin(L["l6"], P4).json()
    r = vao_ma(L["l6"], tao_ma(biz, P4).json()["code"])
    kiem("vao bang ma khi dang co don → don tu chuyen cancelled",
         r.status_code == 200 and any(x["id"] == d6["id"] for x in hang_doi(biz, P4, "cancelled")), r.text)

    r = requests.post(G + f"/projects/{P3}/join-requests", headers=L["l7"]["h"], json={})
    kiem("du an public → 409 khong_can_duyet", r.status_code == 409 and code(r) == "khong_can_duyet", r.text)
    r = requests.post(G + f"/projects/{P1}/join-requests", headers=L["l7"]["h"], json={})
    kiem("du an rieng tu → 404", r.status_code == 404, r.text)

    # Du an approval doi cap 2: cap 1 khong gui don duoc; uy tin tut sau khi gui → duyet bi chan.
    P5, _ = du_an(biz, "DUYETDK", "approval", dieu_kien=DK)
    publish_va_duyet(biz, P5)
    r = xin(L["thap"], P5)
    kiem("approval + cap 1 → 409 chua_du_cap_do (khong lam phien chu du an)", r.status_code == 409 and code(r) == "chua_du_cap_do", r.text)
    d7 = xin(L["l7"], P5).json()
    dat_cap(L["l7"], 2, 30)
    r = requests.post(G + f"/projects/{P5}/join-requests/{d7['id']}/approve", headers=biz["h"])
    kiem("uy tin tut duoi nguong sau khi gui don → duyet 409 chua_du_uy_tin", r.status_code == 409 and code(r) == "chua_du_uy_tin", r.text)
    kiem("don van pending de chu du an tu choi", any(x["id"] == d7["id"] for x in hang_doi(biz, P5)))
    dat_cap(L["l7"], 2, 80)

    # =================================================================================
    print("\n[5] Approval + bai test dau vao: dau thi thanh don kem diem")
    DK_TEST = {"minLevel": None, "minReputation": None, "requireEntranceTest": True, "entranceQuestionCount": 2, "entrancePassPercent": 100}
    MAU = {"t1": "Cau test 1", "t2": "Cau test 2", "t3": "Task 3", "t4": "Task 4"}
    P6, mau6 = tao_du_an(biz, "DUYETTEST", MAU, 1, 1, GIA, dieu_kien=DK_TEST,
                         vang=[("t1", "a", "entranceTest"), ("t2", "b", "entranceTest")])
    che_do(biz, P6, f"[e2e-tong] DUYETTEST {TAG}", "approval")
    publish_va_duyet(biz, P6)
    r = xin(L["l8"], P6)
    kiem("approval + bai test: gui don tay → 409 can_lam_test", r.status_code == 409 and code(r) == "can_lam_test", r.text)
    at = requests.post(G + f"/projects/{P6}/entrance-test/attempts", headers=L["l8"]["h"]).json()
    dung = {mau6["t1"]: "a", mau6["t2"]: "b"}
    r = requests.post(G + f"/projects/{P6}/entrance-test/attempts/{at['attemptId']}/submit", headers=L["l8"]["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"nhan": {"labelIds": [dung[q["sampleId"]]]}}} for q in at["questions"]]})
    j = r.json()
    kiem("dau → chua thanh vien, co joinRequestId", r.status_code == 200 and j["passed"] and not j["joinedProject"] and j["joinRequestId"], r.text)
    kiem("don kem diem 100 va attemptId",
         any(x["id"] == j.get("joinRequestId") and x["scorePercent"] == 100 and x["entranceAttemptId"] == at["attemptId"] for x in hang_doi(biz, P6)), hang_doi(biz, P6))
    r = requests.post(G + f"/projects/{P6}/entrance-test/attempts", headers=L["l8"]["h"])
    kiem("dang co don → khong lam test lai (409 da_co_don_dang_cho)", r.status_code == 409 and code(r) == "da_co_don_dang_cho", r.text)
    kiem("thong bao chu du an co diem", cho(lambda: co_tb(biz, "bài test đầu vào đạt 100%"), 20), tb(biz)[:3])
    requests.post(G + f"/projects/{P6}/join-requests/{j['joinRequestId']}/approve", headers=biz["h"]).raise_for_status()
    t = nhan_task(L["l8"], P6, 20)
    kiem("duyet → nhan task THAT", t is not None and t["sampleId"] in (mau6["t3"], mau6["t4"]), t)

    # Du an public + test van nhu cu (dau = thanh vien ngay).
    P7, mau7 = tao_du_an(biz, "TESTCONGKHAI", MAU, 1, 1, GIA, dieu_kien=dict(DK_TEST, minLevel=2),
                         vang=[("t1", "a", "entranceTest"), ("t2", "b", "entranceTest")])
    publish_va_duyet(biz, P7)
    r = requests.post(G + f"/projects/{P7}/entrance-test/attempts", headers=L["thap"]["h"])
    kiem("bai test kiem cap do TRUOC khi lam → 409 chua_du_cap_do", r.status_code == 409 and code(r) == "chua_du_cap_do", r.text)
    at = requests.post(G + f"/projects/{P7}/entrance-test/attempts", headers=L["l3"]["h"]).json()
    dung = {mau7["t1"]: "a", mau7["t2"]: "b"}
    r = requests.post(G + f"/projects/{P7}/entrance-test/attempts/{at['attemptId']}/submit", headers=L["l3"]["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"nhan": {"labelIds": [dung[q["sampleId"]]]}}} for q in at["questions"]]})
    kiem("public + test: dau → thanh vien ngay, khong co don", r.status_code == 200 and r.json()["joinedProject"] and r.json()["joinRequestId"] is None, r.text)

    # =================================================================================
    print("\n[8] VD-S-16: o moi theo email khong do duoc tai khoan; han muc moi email rieng")

    def cua_toi(u, pid):
        return [x for x in requests.get(G + "/projects/invitations/mine", headers=u["h"]).json() if x["projectId"] == pid]

    biz3 = dang_ky(["business"], "s16")
    P8, _ = du_an(biz3, "S16", "private")
    nap(biz3, 2000000)
    publish_va_duyet(biz3, P8)  # chi moi duoc khi du an da duoc duyet
    chan = dang_ky(["labeler"], "s16-chan")
    requests.put(G + f"/projects/labeler-lists/{chan['id']}", headers=biz3["h"], json={"kind": "blocked"}).raise_for_status()
    dat_va_cho("project.email_invite_daily_max", 4, ("project",))
    r = requests.post(G + f"/projects/{P8}/members/batch", headers=biz3["h"], json={
        "role": "labeler", "emails": [L["l4"]["email"], chan["email"], f"s16-khong-co-{TAG}@crowd.local"]})
    kq = r.json()["results"] if r.status_code == 200 else []
    kiem("co tai khoan / bi doanh nghiep chan / chua co tai khoan → 3 dong giong het",
         len(kq) == 3 and all(x["status"] == "invited" and x["userId"] is None and x["displayName"] is None
                              and x["message"] == kq[0]["message"] and x["invitationId"] for x in kq), r.text)
    kiem("l4 nhan loi moi trong ung dung", cho(lambda: cua_toi(L["l4"], P8), 10))
    time.sleep(1)
    kiem("nguoi bi doanh nghiep chan KHONG nhan gi (loi moi cam)", not cua_toi(chan, P8))
    ds = requests.get(G + f"/projects/{P8}/invitations", headers=biz3["h"]).json()
    kiem("chu du an thay 3 loi moi pending nhu nhau, khong userId",
         len(ds) == 3 and all(x["status"] == "pending" and x["userId"] is None and x["byEmail"] for x in ds), ds)

    r = requests.post(G + f"/projects/{P8}/members/batch", headers=biz3["h"], json={"role": "labeler", "emails": ["s16a@x.vn", "s16b@x.vn"]})
    kiem("3 + 2 > project.email_invite_daily_max = 4 → 429 vuot_han_muc_moi_email kem Retry-After",
         r.status_code == 429 and code(r) == "vuot_han_muc_moi_email" and int(r.headers.get("Retry-After", "0")) > 0
         and r.json().get("retryAfterSeconds", 0) > 0, (r.status_code, r.headers.get("Retry-After"), r.text))
    kiem("bi 429 thi khong tao loi moi nao", len(requests.get(G + f"/projects/{P8}/invitations", headers=biz3["h"]).json()) == 3)
    r = requests.post(G + f"/projects/{P8}/members", headers=biz3["h"], json={"role": "labeler", "email": "s16a@x.vn"})
    kiem("con dung 1 suat → 202", r.status_code == 202, r.text)
    r = requests.post(G + f"/projects/{P8}/members", headers=biz3["h"], json={"role": "labeler", "userId": L["l5"]["id"]})
    kiem("moi theo userId khong tinh vao han muc email → 200", r.status_code == 200 and r.json()["userId"] == L["l5"]["id"], r.text)
    dat_va_cho("project.email_invite_daily_max", 200, ("project",))

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e thanh vien: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
