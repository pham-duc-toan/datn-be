# E2E HOAN THIEN: tai khoan (xac thuc email, quen / doi mat khau, ho so, anh, ky nang), cap do / xep hang,
# thu hoi phien tuc thi + gioi han tan suat (gateway), luu nhap + bao loi task, thu nhap / chi phi / tien do,
# phan bo nhan + xuat, tranh chap, chong gian lan, audit / thong ke / thong bao toan he thong.
# Tu tao tai khoan + du an moi; email kiem THAT qua Mailpit.
# Chay: python e2e_hoan_thien.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import MAILPIT  # noqa: E402
from tai_khoan_e2e import TIEU_DE_DAT_LAI, TIEU_DE_XAC_THUC, thu_moi_nhat, token_trong_email, xac_thuc_email  # noqa: E402

GIA = 20000
ANH_PNG = bytes.fromhex("89504e470d0a1a0a0000000d4948445200000001000000010806000000"
                        "1f15c4890000000d49444154789c6360000002000154a24f5d0000000049454e44ae426082")


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def thu_cua(email):
    r = requests.get(f"{MAILPIT}/api/v1/search", params={"query": f'to:"{email}"', "limit": 200})
    return [m["Subject"] for m in r.json().get("messages", [])]


def tb(u):
    return [x["title"] for x in requests.get(G + "/notifications/me?pageSize=100", headers=u["h"]).json()["items"]]


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


def duyet(chu, aid, viec="approve", body=None):
    """Duyet / tu choi nhan; 409 xung_dot_dong_thoi (quality-svc vua cap nhat nhan) → thu lai."""
    for _ in range(6):
        r = requests.post(G + f"/annotations/{aid}/{viec}", headers=chu["h"], json=body)
        if r.status_code != 409 or code(r) != "xung_dot_dong_thoi":
            break
        time.sleep(0.5)
    assert r.status_code == 200, r.text


def duyet_het(chu, pid, so):
    nhan = cho(lambda: (lambda x: x if len(x) >= so else None)([a for a in ds_nhan(chu, pid) if a["status"] == "pendingReview"]), 30)
    assert nhan, ds_nhan(chu, pid)
    for a in nhan:
        duyet(chu, a["id"])
    return nhan


def rut(u, so):
    return requests.post(G + "/ledger/withdrawals", headers=dict(u["h"], **{"Idempotency-Key": uuid.uuid4().hex}),
                         json={"amountVnd": so, "bankAccount": "VCB-0123456789"})


def dang_nhap(email, pw):
    r = login_raw(email, pw)
    assert r.status_code == 200, r.text
    t = r.json()
    return {"h": hdr(t["accessToken"]), "refresh": t["refreshToken"], "email": email}


try:
    dat_va_cho("ledger.hold_release_interval", 5, ("ledger",))
    dat_va_cho("ledger.hold_duration", 3600, ("ledger",))
    biz = dang_ky(["business"], "biz")
    nap(biz, 3_000_000)

    # =================================================================================
    print("\n[1] Xac thuc email (FC-01): chua xac thuc thi chua rut tien / publish")
    chua = dang_ky(["labeler"], "chuaxt", xac_thuc=False)
    kiem("ho so: emailVerified = false", requests.get(G + "/me/profile", headers=chua["h"]).json()["emailVerified"] is False)
    kiem("email xac thuc da toi Mailpit (co link token)", "token=" in (thu_moi_nhat(chua["email"], TIEU_DE_XAC_THUC) or ""))
    r = requests.post(G + "/me/verify-email/resend", headers=chua["h"])
    kiem("gui lai ngay → 409 gui_qua_nhanh", r.status_code == 409 and code(r) == "gui_qua_nhanh", r.text)
    cho(lambda: sql("ledger", f"select count(*) from email_status where user_id='{chua['id']}'") == "1", 30)
    r = rut(chua, 50000)
    kiem("chua xac thuc → rut tien 409 chua_xac_thuc_email", r.status_code == 409 and code(r) == "chua_xac_thuc_email", r.text)
    r = requests.post(G + "/auth/verify-email", json={"token": "khong-dung"})
    kiem("token sai → 400 ma_khong_hop_le", r.status_code == 400 and code(r) == "ma_khong_hop_le", r.text)
    token = token_trong_email(chua["email"], TIEU_DE_XAC_THUC)
    r = requests.post(G + "/auth/verify-email", json={"token": token})
    kiem("bam link → 200 emailVerified", r.status_code == 200 and r.json()["emailVerified"] is True, r.text)
    kiem("dung lai link → 400", requests.post(G + "/auth/verify-email", json={"token": token}).status_code == 400)
    kiem("ho so: emailVerified = true", requests.get(G + "/me/profile", headers=chua["h"]).json()["emailVerified"] is True)
    kiem("ledger nhan user.email_verified", cho(lambda: sql("ledger", f"select verified from email_status where user_id='{chua['id']}'") == "t", 30))
    r = rut(chua, 50000)
    kiem("da xac thuc → het bi chan (con loi so du)", r.status_code != 409 or code(r) != "chua_xac_thuc_email", r.text)

    biz2 = dang_ky(["business"], "biz2", xac_thuc=False)
    nap(biz2, 500000)
    P0, _ = tao_du_an(biz2, "XT", {"x1": "Mau x"}, 1, 1, GIA)
    r = requests.post(G + f"/projects/{P0}/publish", headers=biz2["h"])
    kiem("doanh nghiep da KYB nhung chua xac thuc email → publish 409 chua_xac_thuc_email",
         r.status_code == 409 and code(r) == "chua_xac_thuc_email", r.text)
    xac_thuc_email(biz2["email"])
    kiem("xac thuc xong → publish duoc", requests.post(G + f"/projects/{P0}/publish", headers=biz2["h"]).status_code == 200)

    # =================================================================================
    print("\n[2] Quen / dat lai / doi mat khau, dang xuat moi thiet bi, thu hoi phien tuc thi (FC-03, VD-S-11)")
    mk = dang_ky(["labeler"], "matkhau")
    kiem("quen mat khau email khong ton tai → 202 (khong tiet lo)",
         requests.post(G + "/auth/forgot-password", json={"email": f"khong-co-{TAG}@crowd.local"}).status_code == 202)
    kiem("quen mat khau → 202", requests.post(G + "/auth/forgot-password", json={"email": mk["email"]}).status_code == 202)
    tk = token_trong_email(mk["email"], TIEU_DE_DAT_LAI)
    kiem("email dat lai mat khau co link", tk is not None)
    r = requests.post(G + "/auth/reset-password", json={"token": tk, "newPassword": "ngan"})
    kiem("mat khau moi qua ngan → 400 mat_khau_khong_hop_le", r.status_code == 400 and code(r) == "mat_khau_khong_hop_le", r.text)
    r = requests.post(G + "/auth/reset-password", json={"token": "sai", "newPassword": "MatKhauMoi@1"})
    kiem("token sai → 400 ma_khong_hop_le", r.status_code == 400 and code(r) == "ma_khong_hop_le", r.text)
    time.sleep(1.2)  # moc thu hoi lam tron GIAY: token phat cung giay voi moc van con dung
    r = requests.post(G + "/auth/reset-password", json={"token": tk, "newPassword": "MatKhauMoi@1"})
    kiem("dat lai → 204", r.status_code == 204, r.text)
    r = login_raw(mk["email"])
    kiem("mat khau cu → 401 sai_thong_tin (co code)", r.status_code == 401 and code(r) == "sai_thong_tin", r.text)
    kiem("mat khau moi dang nhap duoc", login_raw(mk["email"], "MatKhauMoi@1").status_code == 200)
    r = requests.post(G + "/auth/refresh", json={"refreshToken": mk["refresh"]})
    kiem("refresh token cu → 401 phien_khong_hop_le", r.status_code == 401 and code(r) == "phien_khong_hop_le", r.text)
    kiem("access token cu bi gateway chan NGAY (401 phien_da_bi_thu_hoi)",
         cho(lambda: code(requests.get(G + "/me", headers=mk["h"])) == "phien_da_bi_thu_hoi", 10))
    kiem("dung lai link dat lai → 400", requests.post(G + "/auth/reset-password", json={"token": tk, "newPassword": "MatKhauMoi@2"}).status_code == 400)

    s1 = dang_nhap(mk["email"], "MatKhauMoi@1")
    time.sleep(1.2)
    r = requests.post(G + "/me/password", headers=s1["h"], json={"currentPassword": "sai-roi", "newPassword": "MatKhauMoi@2"})
    kiem("doi mat khau sai mat khau hien tai → 400", r.status_code == 400 and code(r) == "mat_khau_hien_tai_sai", r.text)
    r = requests.post(G + "/me/password", headers=s1["h"], json={"currentPassword": "MatKhauMoi@1", "newPassword": "MatKhauMoi@2"})
    kiem("doi mat khau → 200 kem cap token moi", r.status_code == 200 and r.json().get("accessToken"), r.text)
    moi = hdr(r.json()["accessToken"])
    kiem("token cu bi thu hoi", cho(lambda: requests.get(G + "/me", headers=s1["h"]).status_code == 401, 10))
    kiem("token moi van dung duoc (cung giay voi moc)", requests.get(G + "/me", headers=moi).status_code == 200)

    a = dang_nhap(mk["email"], "MatKhauMoi@2")
    b = dang_nhap(mk["email"], "MatKhauMoi@2")
    time.sleep(1.2)
    kiem("dang xuat moi thiet bi → 204", requests.post(G + "/me/logout-all", headers=a["h"]).status_code == 204)
    kiem("thiet bi khac: access token bi chan", cho(lambda: requests.get(G + "/me", headers=b["h"]).status_code == 401, 10))
    kiem("thiet bi khac: refresh 401", requests.post(G + "/auth/refresh", json={"refreshToken": b["refresh"]}).status_code == 401)

    # =================================================================================
    print("\n[3] Ho so, ky nang, anh dai dien, ho so cong khai (FC-05, FL-01)")
    hs = dang_ky(["labeler"], "hoso")
    r = requests.put(G + "/me/profile", headers=hs["h"], json={"displayName": "Nguyen Van Ho So", "bio": "Gan nhan anh y te"})
    kiem("sua ho so", r.status_code == 200 and r.json()["displayName"] == "Nguyen Van Ho So" and r.json()["bio"], r.text)
    r = requests.put(G + "/me/profile", headers=hs["h"], json={"displayName": " "})
    kiem("ten rong → 400 ten_khong_hop_le", r.status_code == 400 and code(r) == "ten_khong_hop_le", r.text)
    r = requests.put(G + "/me/skills", headers=hs["h"], json={"domains": ["Y te", "y te"], "languages": ["vi"], "modalities": ["IMAGE", "text"]})
    kiem("ky nang: bo trung, chuan hoa loai du lieu",
         r.status_code == 200 and r.json()["skills"]["domains"] == ["Y te"] and r.json()["skills"]["modalities"] == ["image", "text"], r.text)
    r = requests.put(G + "/me/skills", headers=hs["h"], json={"modalities": ["3d"]})
    kiem("loai du lieu la → 400", r.status_code == 400 and code(r) == "loai_du_lieu_khong_hop_le", r.text)
    r = requests.post(G + "/me/avatar/upload-url", headers=hs["h"], json={"fileName": "a.exe", "sizeBytes": 10})
    kiem("anh .exe → 400 file_khong_hop_le", r.status_code == 400 and code(r) == "file_khong_hop_le", r.text)
    r = requests.post(G + "/me/avatar/upload-url", headers=hs["h"], json={"fileName": "a.png", "sizeBytes": 100 * 1024 * 1024})
    kiem("anh 100 MB → 400 file_qua_lon", r.status_code == 400 and code(r) == "file_qua_lon", r.text)
    up = requests.post(G + "/me/avatar/upload-url", headers=hs["h"], json={"fileName": "a.png", "sizeBytes": len(ANH_PNG)}).json()
    r = requests.put(G + "/me/avatar", headers=hs["h"], json={"key": f"{uuid.uuid4()}/x.png"})
    kiem("key cua nguoi khac → 400 key_khong_hop_le", r.status_code == 400 and code(r) == "key_khong_hop_le", r.text)
    r = requests.put(G + "/me/avatar", headers=hs["h"], json={"key": up["key"]})
    kiem("chua tai anh len → 409 chua_tai_anh", r.status_code == 409 and code(r) == "chua_tai_anh", r.text)
    requests.put(up["uploadUrl"], data=ANH_PNG).raise_for_status()
    r = requests.put(G + "/me/avatar", headers=hs["h"], json={"key": up["key"]})
    kiem("gan anh dai dien → co avatarUrl", r.status_code == 200 and r.json()["avatarUrl"], r.text)
    kiem("link anh tai dung file", requests.get(r.json()["avatarUrl"]).content == ANH_PNG)
    pub = requests.get(G + "/users/public", headers=biz["h"], params={"ids": f"{hs['id']},{uuid.uuid4()}"}).json()
    kiem("ho so cong khai: ten + anh + cap, KHONG lo email",
         len(pub) == 1 and pub[0]["displayName"] == "Nguyen Van Ho So" and pub[0]["avatarUrl"] and "email" not in pub[0], pub)
    kiem("ids sai → 400", requests.get(G + "/users/public", headers=biz["h"], params={"ids": "abc"}).status_code == 400)

    # =================================================================================
    print("\n[4] Cap do, xep hang (FL-12) → task-svc loc minLevel; thong bao cap moi")
    dat_va_cho("identity.level_rules", "2=1/0;3=2/0", ("identity",))
    l1 = dang_ky(["labeler"], "cap1")
    l2 = dang_ky(["labeler"], "cap2")
    P1, _ = tao_du_an(biz, "CAP", {"c1": "Cap 1", "c2": "Cap 2"}, 2, 2, GIA)
    publish_va_duyet(biz, P1)
    for u in (l1, l2):
        tham_gia(u, P1)
        lam_het(u, P1, lambda _: "a")
    duyet_het(biz, P1, 4)
    lv = cho(lambda: (lambda j: j if j["level"] >= 3 else None)(requests.get(G + "/me/level", headers=l1["h"]).json()), 40)
    kiem("2 nhan duoc duyet + co uy tin → cap 3", lv is not None, requests.get(G + "/me/level", headers=l1["h"]).json())
    kiem("cap cao nhat theo luat → nextLevel null, approvedCount 2", lv and lv["nextLevel"] is None and lv["approvedCount"] == 2, lv)
    kiem("thong bao 'Cap do moi: 3'", cho(lambda: "Cấp độ mới: 3" in tb(l1), 20), tb(l1))
    bxh = requests.get(G + "/users/leaderboard", headers=l2["h"], params={"period": "week", "limit": 100}).json()
    kiem("bang xep hang tuan co labeler (2 nhan)", any(x["userId"] == l1["id"] and x["approvedCount"] == 2 for x in bxh), bxh[:3])
    kiem("period sai → 400", requests.get(G + "/users/leaderboard", headers=l2["h"], params={"period": "nam"}).status_code == 400)
    moi_vao = dang_ky(["labeler"], "capthap")
    PL, _ = tao_du_an(biz, "MINLV", {"m1": "Min 1"}, 1, 1, GIA, dieu_kien={"minLevel": 3, "minReputation": None,
              "requireEntranceTest": False, "entranceQuestionCount": 10, "entrancePassPercent": 80})
    publish_va_duyet(biz, PL)
    tham_gia(moi_vao, PL)
    tham_gia(l1, PL)
    # Ngay sau khi duyet, task-svc co the chua nhan project.published (du_an_chua_san_sang) → cho.
    # (Response 4xx co bool False → tra tuple de cho() dung lai.)
    r = cho(lambda: (lambda x: (x.status_code, code(x), x.text) if code(x) != "du_an_chua_san_sang" else None)(
        requests.post(G + f"/tasks/projects/{PL}/next", headers=moi_vao["h"])), 20)
    kiem("labeler cap 1 → 403 chua_du_cap_do", r is not None and r[0] == 403 and r[1] == "chua_du_cap_do", r)
    kiem("labeler cap 3 nhan duoc task (level.changed toi task-svc)",
         cho(lambda: requests.post(G + f"/tasks/projects/{PL}/next", headers=l1["h"]).status_code == 200, 20))

    # =================================================================================
    print("\n[5] Luu nhap, bao loi task (FL-05, FL-07); tien do + uoc tinh (FB-20)")
    dat_va_cho("task.report_auto_exclude", 2, ("task",))
    PN, _ = tao_du_an(biz, "NHAP", {"n1": "Nhap 1", "n2": "Nhap 2", "n3": "Nhap 3"}, 1, 1, GIA)
    publish_va_duyet(biz, PN)
    tham_gia(l2, PN)
    t = nhan_task(l2, PN, 20)
    r = requests.put(G + f"/tasks/assignments/{t['assignmentId']}/draft", headers=l2["h"], json={"payload": "chuoi"})
    kiem("nhap khong phai object → 400", r.status_code == 400 and code(r) == "nhap_khong_hop_le", r.text)
    r = requests.put(G + f"/tasks/assignments/{t['assignmentId']}/draft", headers=l2["h"], json={"payload": {"nhan": {"labelIds": ["b"]}}})
    kiem("luu nhap → 200", r.status_code == 200 and r.json()["draftSavedAt"], r.text)
    lai = nhan_task(l2, PN)
    kiem("nhan lai → cung task, kem ban nhap", lai["assignmentId"] == t["assignmentId"] and lai["draft"] == {"nhan": {"labelIds": ["b"]}}, lai)
    r = requests.post(G + f"/tasks/assignments/{t['assignmentId']}/report", headers=l2["h"], json={"reason": "other"})
    kiem("bao loi 'other' khong ghi chu → 400", r.status_code == 400 and code(r) == "thieu_ghi_chu", r.text)
    r = requests.post(G + f"/tasks/assignments/{t['assignmentId']}/report", headers=l2["h"], json={"reason": "broken", "note": "khong doc duoc"})
    kiem("bao loi → 200 (tra task)", r.status_code == 200 and r.json()["reportsOnTask"] == 1, r.text)
    sau = nhan_task(l2, PN)
    kiem("nguoi bao khong nhan lai task da bao", sau is not None and sau["taskId"] != t["taskId"], sau)
    nop(l2, sau["assignmentId"], "a").raise_for_status()
    rp = requests.get(G + f"/tasks/projects/{PN}/reports", headers=biz["h"]).json()
    kiem("chu du an thay bao loi dang mo", len(rp) == 1 and rp[0]["reason"] == "broken" and rp[0]["note"] == "khong doc duoc", rp)
    kiem("labeler xem bao loi cua du an → 404", requests.get(G + f"/tasks/projects/{PN}/reports", headers=l2["h"]).status_code == 404)
    r = requests.post(G + f"/tasks/reports/{rp[0]['id']}/resolve", headers=biz["h"], json={"action": "xoa"})
    kiem("hanh dong la → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/tasks/reports/{rp[0]['id']}/resolve", headers=biz["h"], json={"action": "dismiss"})
    kiem("bo qua bao loi → dismissed", r.status_code == 200 and r.json()["state"] == "dismissed", r.text)

    P1M, _ = tao_du_an(biz, "BAOLOI", {"b1": "Bao loi 1"}, 1, 1, GIA)
    publish_va_duyet(biz, P1M)
    for u in (l1, l2):
        tham_gia(u, P1M)
        x = nhan_task(u, P1M, 20)
        requests.post(G + f"/tasks/assignments/{x['assignmentId']}/report", headers=u["h"], json={"reason": "inappropriate"}).raise_for_status()
    td = requests.get(G + f"/tasks/projects/{P1M}/progress", headers=biz["h"]).json()
    kiem("2 nguoi bao cung mau → tu loai (task huy)", td["cancelledTasks"] == 1 and td["openTasks"] == 0, td)
    kiem("moi bao loi cua mau → excluded",
         all(x["state"] == "excluded" for x in requests.get(G + f"/tasks/projects/{P1M}/reports?status=all", headers=biz["h"]).json()))
    td = requests.get(G + f"/tasks/projects/{P1}/progress", headers=biz["h"]).json()
    kiem("tien do: so luot nop 24 gio + uoc tinh xong", td["submissionsLast24h"] >= 4 and td["estimatedCompletionAt"], td)

    # =================================================================================
    print("\n[6] Thu nhap (FL-11), chi phi du an (FB-20), phan bo nhan + xuat (FB-24, FB-25)")
    tn = requests.get(G + "/ledger/me/earnings", headers=l1["h"]).json()
    kiem("thu nhap theo ngay = 2 nhan x don gia", tn["totalVnd"] == 2 * GIA and tn["byKindVnd"]["annotation"] == 2 * GIA and len(tn["items"]) == 1, tn)
    tn = requests.get(G + "/ledger/me/earnings?groupBy=project", headers=l1["h"]).json()
    kiem("thu nhap theo du an", any(x["key"] == P1 and x["amountVnd"] == 2 * GIA for x in tn["items"]), tn)
    kiem("groupBy la → 400", requests.get(G + "/ledger/me/earnings?groupBy=thang", headers=l1["h"]).status_code == 400)
    kiem("khoang > 366 ngay → 400", requests.get(G + "/ledger/me/earnings?from=2024-01-01&to=2026-01-01", headers=l1["h"]).status_code == 400)
    cp = requests.get(G + f"/ledger/projects/{P1}/cost", headers=biz["h"]).json()
    kiem("chi phi du an: da ky quy, da chi 4 nhan, con treo", cp["reservedVnd"] > 0 and cp["paidAnnotations"] == 4
         and cp["spentAnnotationsVnd"] >= 4 * GIA and cp["heldVnd"] == 4 * GIA, cp)
    kiem("doanh nghiep khac xem chi phi → 404", requests.get(G + f"/ledger/projects/{P1}/cost", headers=biz2["h"]).status_code == 404)
    dat_va_cho("annotation.imbalance_min_labels", 2, ("annotation",))
    pb = requests.get(G + f"/annotations/projects/{P1}/distribution", headers=biz["h"]).json()
    kiem("canh bao lech: 'a' ap dao, 'b' chua co nhan",
         any(w["code"] == "lop_ap_dao" and w["label"] == "a" for w in pb["imbalanceWarnings"])
         and any(w["code"] == "lop_chua_co_nhan" and w["label"] == "b" for w in pb["imbalanceWarnings"]), pb)
    r = requests.get(G + f"/annotations/projects/{P1}/export?format=yolo", headers=biz["h"])
    kiem("xuat yolo du an van ban → 400 khung_chi_cho_anh", r.status_code == 400 and code(r) == "khung_chi_cho_anh", r.text)
    kiem("dinh dang la → 400", requests.get(G + f"/annotations/projects/{P1}/export?format=xml", headers=biz["h"]).status_code == 400)

    # =================================================================================
    print("\n[7] Tranh chap nhan da duyet (FM-06)")
    lt = dang_ky(["labeler"], "tranhchap")
    PD, _ = tao_du_an(biz, "TC", {"t1": "TC 1", "t2": "TC 2", "t3": "TC 3"}, 1, 1, GIA)
    publish_va_duyet(biz, PD)
    tham_gia(lt, PD)
    lam_het(lt, PD, lambda _: "a")
    nhan = duyet_het(biz, PD, 3)
    cho(lambda: vi(lt)["pendingVnd"] == 3 * GIA, 30)
    a1, a2 = nhan[0]["id"], nhan[1]["id"]
    r = requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": a1, "reason": ""})
    kiem("tranh chap khong ly do → 400", r.status_code == 400 and code(r) == "thieu_ly_do", r.text)
    kiem("doanh nghiep khac → 404", requests.post(G + "/ledger/disputes", headers=biz2["h"], json={"annotationId": a1, "reason": "x"}).status_code == 404)
    kiem("labeler mo tranh chap → 403", requests.post(G + "/ledger/disputes", headers=lt["h"], json={"annotationId": a1, "reason": "x"}).status_code == 403)
    r = requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": a1, "reason": "Gan sai lop"})
    kiem("mo tranh chap → 201 open", r.status_code == 201 and r.json()["state"] == "open", r.text)
    d1 = r.json()["id"]
    r = requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": a1, "reason": "lan hai"})
    kiem("tranh chap lan hai → 409 da_tranh_chap", r.status_code == 409 and code(r) == "da_tranh_chap", r.text)
    kiem("labeler nhan thong bao + email (bat buoc)",
         cho(lambda: "Nhãn đã duyệt bị tranh chấp" in tb(lt) and "Nhãn đã duyệt bị tranh chấp" in thu_cua(lt["email"]), 45), tb(lt))
    kiem("cost: dang tranh chap 1 nhan", requests.get(G + f"/ledger/projects/{PD}/cost", headers=biz["h"]).json()["disputedVnd"] == GIA)
    kiem("doanh nghiep thay tranh chap cua minh", any(x["id"] == d1 for x in requests.get(G + "/ledger/disputes/mine", headers=biz["h"]).json()["items"]))
    kiem("hang doi admin co tranh chap", any(x["id"] == d1 for x in requests.get(G + "/ledger/admin/disputes", headers=admin).json()["items"]))
    r = requests.post(G + f"/ledger/admin/disputes/{d1}/resolve", headers=admin, json={"outcome": "refundBusiness", "note": ""})
    kiem("phan xu khong ghi chu → 400", r.status_code == 400 and code(r) == "thieu_ghi_chu", r.text)
    kiem("ket cuc la → 400", requests.post(G + f"/ledger/admin/disputes/{d1}/resolve", headers=admin,
                                           json={"outcome": "chia_doi", "note": "x"}).status_code == 400)
    truoc = requests.get(G + f"/ledger/projects/{PD}/cost", headers=biz["h"]).json()["balanceVnd"]
    r = requests.post(G + f"/ledger/admin/disputes/{d1}/resolve", headers=admin, json={"outcome": "refundBusiness", "note": "Dung la sai"})
    kiem("xu doanh nghiep thang → refundedBusiness", r.status_code == 200 and r.json()["state"] == "refundedBusiness", r.text)
    kiem("tien treo labeler giam 1 nhan", cho(lambda: vi(lt)["pendingVnd"] == 2 * GIA, 20), vi(lt))
    cp = requests.get(G + f"/ledger/projects/{PD}/cost", headers=biz["h"]).json()
    kiem("thu lao + phi ve lai ky quy du an", cp["disputeRefundsVnd"] > GIA and cp["balanceVnd"] == truoc + cp["disputeRefundsVnd"], cp)
    kiem("nhan bi loai, khong khieu nai duoc",
         cho(lambda: (lambda a: a["status"] == "rejected" and a["canAppeal"] is False)(
             next(x for x in ds_nhan(biz, PD) if x["id"] == a1)), 20))
    kiem("identity tru so nhan duoc duyet",
         cho(lambda: requests.get(G + "/me/level", headers=lt["h"]).json()["approvedCount"] == 2, 20))
    kiem("hai ben nhan 'Ket qua tranh chap'", cho(lambda: "Kết quả tranh chấp" in tb(lt) and "Kết quả tranh chấp" in tb(biz), 20))
    kiem("phan xu lan hai → 409", requests.post(G + f"/ledger/admin/disputes/{d1}/resolve", headers=admin,
                                                json={"outcome": "payLabeler", "note": "x"}).status_code == 409)
    d2 = requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": a2, "reason": "Nghi sai"}).json()["id"]
    r = requests.post(G + f"/ledger/admin/disputes/{d2}/resolve", headers=admin, json={"outcome": "payLabeler", "note": "Nhan dung"})
    kiem("xu labeler thang → paidLabeler, tien van treo", r.status_code == 200 and r.json()["state"] == "paidLabeler"
         and requests.get(G + f"/ledger/projects/{PD}/cost", headers=biz["h"]).json()["heldVnd"] == 2 * GIA, r.text)
    dat_va_cho("ledger.hold_duration", 0, ("ledger",))
    PR, _ = tao_du_an(biz, "TCH", {"h1": "Het treo"}, 1, 1, GIA)
    publish_va_duyet(biz, PR)
    tham_gia(lt, PR)
    lam_het(lt, PR, lambda _: "a")
    a3 = duyet_het(biz, PR, 1)[0]["id"]
    cho(lambda: requests.get(G + f"/ledger/projects/{PR}/cost", headers=biz["h"]).json()["heldVnd"] == 0
        and requests.get(G + f"/ledger/projects/{PR}/cost", headers=biz["h"]).json()["paidAnnotations"] == 1, 30)
    r = requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": a3, "reason": "Muon"})
    kiem("tien da het treo → 409 khong_con_treo", r.status_code == 409 and code(r) == "khong_con_treo", r.text)
    kiem("doi soat so cai lanh sau tranh chap", requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()["healthy"])

    # =================================================================================
    print("\n[8] Chong gian lan (FM-07) → admin xac nhan → identity khoa")
    for k, v in (("quality.fraud_fast_window", 3), ("quality.fraud_fast_fraction", 0.5), ("quality.fraud_min_seconds", 30),
                 ("quality.fraud_same_label_streak", 3), ("quality.fraud_shared_accounts", 2),
                 ("quality.fraud_ignore_private_ips", False), ("quality.fraud_reopen_cooldown", 0)):
        dat_va_cho(k, v, ("quality",))
    lf = dang_ky(["labeler"], "gianlan")
    lx = dang_ky(["labeler"], "dadang")
    PF, _ = tao_du_an(biz, "GL", {f"g{i}": f"Gian lan {i}" for i in range(1, 5)}, 2, 2, GIA)
    publish_va_duyet(biz, PF)
    for u in (lf, lx):
        tham_gia(u, PF)
    thiet_bi = f"thiet-bi-{TAG}"
    dem = {"i": 0}

    def xen_ke(_):
        dem["i"] += 1
        return "a" if dem["i"] % 2 else "b"
    for _ in range(4):
        x = nhan_task(lx, PF, 20)
        requests.post(G + f"/tasks/assignments/{x['assignmentId']}/submit", headers=dict(lx["h"], **{"X-Device-Id": thiet_bi}),
                      json={"payload": {"nhan": {"labelIds": [xen_ke(None)]}}}).raise_for_status()
    for _ in range(3):
        x = nhan_task(lf, PF, 20)
        requests.post(G + f"/tasks/assignments/{x['assignmentId']}/submit", headers=dict(lf["h"], **{"X-Device-Id": thiet_bi}),
                      json={"payload": {"nhan": {"labelIds": ["a"]}}}).raise_for_status()

    def ho_so(loai, uid):
        j = requests.get(G + "/quality/admin/fraud-cases", headers=admin, params={"status": "open", "type": loai, "pageSize": 100}).json()
        return next((c for c in j["items"] if uid in c["userIds"]), None)
    hs_nhanh = cho(lambda: ho_so("qua_nhanh", lf["id"]), 30)
    kiem("3 luot qua nhanh → ho so qua_nhanh", hs_nhanh is not None and hs_nhanh["evidence"]["fastCount"] >= 2, hs_nhanh)
    kiem("chon 'a' 3 lan lien tiep (nguoi khac chon a, b) → ho so mot_nhan_lien_tuc",
         cho(lambda: ho_so("mot_nhan_lien_tuc", lf["id"]), 30) is not None)
    kiem("2 tai khoan chung IP → ho so chung_ip", cho(lambda: ho_so("chung_ip", lf["id"]), 30) is not None)
    hs_tb = cho(lambda: ho_so("chung_thiet_bi", lf["id"]), 30)
    kiem("2 tai khoan chung X-Device-Id → ho so chung_thiet_bi", hs_tb is not None and lx["id"] in hs_tb["userIds"], hs_tb)
    kiem("labeler xem ho so → 403", requests.get(G + "/quality/admin/fraud-cases", headers=lf["h"]).status_code == 403)
    ct = requests.get(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}", headers=admin).json()
    kiem("chi tiet co lich su nguoi dung", any(x["userId"] == lf["id"] and x["submissions"] >= 3 for x in ct["users"]), ct)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin, json={"action": "confirm"})
    kiem("xac nhan khong ghi chu → 400", r.status_code == 400 and code(r) == "thieu_ghi_chu", r.text)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_tb['id']}/resolve", headers=admin, json={"action": "dismiss", "note": "Cung gia dinh"})
    kiem("bo qua ho so chung thiet bi", r.status_code == 200 and r.json()["status"] == "dismissed", r.text)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin,
                      json={"action": "confirm", "note": "Nop 3 cau trong 1 giay", "blockAccounts": True})
    kiem("xac nhan + khoa tai khoan", r.status_code == 200 and r.json()["status"] == "confirmed" and r.json()["blocked"], r.text)
    kiem("identity khoa labeler (dang nhap 403)", cho(lambda: login_raw(lf["email"]).status_code == 403, 30))
    kiem("access token cua labeler bi gateway chan", cho(lambda: code(requests.get(G + "/me", headers=lf["h"])) == "phien_da_bi_thu_hoi", 10))
    kiem("xu ly lan hai → 409", requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin,
                                             json={"action": "dismiss", "note": "x"}).status_code == 409)
    requests.post(G + f"/users/admin/{lf['id']}/unblock", headers=admin).raise_for_status()
    lf2 = dang_nhap(lf["email"], PW)
    kiem("mo khoa → nhan task lai duoc (truoc day task-svc khong nghe user.unblocked)",
         cho(lambda: requests.post(G + f"/tasks/projects/{PF}/next", headers=lf2["h"]).status_code in (200, 204), 20))
    r = rut(lf2, 50000)
    kiem("mo khoa → ledger het chan rut tien (con loi khac)", cho(lambda: code(rut(lf2, 50000)) != "tai_khoan_bi_khoa", 20), r.text)

    # =================================================================================
    print("\n[9] Audit (FP-06), thong ke, thong bao toan he thong (FM-09)")
    nan_nhan = dang_ky(["labeler"], "audit")
    r = requests.post(G + f"/users/admin/{nan_nhan['id']}/block", headers=admin, json={"reason": "Thu audit"})
    corr = r.headers.get("X-Correlation-Id")
    loai = cho(lambda: (lambda s: s if {"user.blocked", "user.sessions_revoked"} <= s else None)(
        {x["eventType"] for x in requests.get(G + "/admin/audit", headers=admin, params={"correlationId": corr}).json()["items"]}), 20)
    kiem("audit theo correlationId: user.blocked + user.sessions_revoked", loai is not None, corr)
    adm_id = requests.get(G + "/me", headers=admin).json()["userId"]
    j = requests.get(G + "/admin/audit", headers=admin, params={"correlationId": corr}).json()["items"]
    kiem("audit ghi nguoi lam = admin", any(x["eventType"] == "user.blocked" and x["actorId"] == adm_id and x["actorRole"] == "admin" for x in j), j)
    j = requests.get(G + "/admin/audit", headers=admin, params={"eventType": "dispute.", "pageSize": 100}).json()
    kiem("loc tien to 'dispute.'", {"dispute.opened", "dispute.resolved"} <= {x["eventType"] for x in j["items"]}, j["total"])
    kiem("labeler xem audit → 403", requests.get(G + "/admin/audit", headers=lt["h"]).status_code == 403)
    tk = requests.get(G + "/admin/stats/overview", headers=admin).json()
    kiem("thong ke: co dang ky, nhan duyet, tranh chap, 30 ngay",
         tk["newUsers"] > 0 and tk["annotationsApproved"] > 0 and tk["disputesOpened"] >= 2 and len(tk["daily"]) == 30, tk)
    kiem("from > to → 400", requests.get(G + "/admin/stats/overview?from=2026-10-10&to=2026-10-01", headers=admin).status_code == 400)
    r = requests.post(G + "/admin/announcements", headers=admin, json={"title": "x", "body": "y", "audience": "nguoi_ngoai_hanh_tinh"})
    kiem("doi tuong la → 400", r.status_code == 400 and code(r) == "doi_tuong_khong_hop_le", r.text)
    tieu_de = f"Bao tri he thong {TAG}"
    r = requests.post(G + "/admin/announcements", headers=admin,
                      json={"title": tieu_de, "body": "He thong bao tri 22h.", "audience": "labeler", "sendEmail": True})
    kiem("gui thong bao cho labeler → 201", r.status_code == 201, r.text)
    kiem("labeler nhan thong bao + email", cho(lambda: tieu_de in tb(lt) and tieu_de in thu_cua(lt["email"]), 45), tb(lt))
    kiem("doanh nghiep (khong phai labeler) khong nhan", tieu_de not in tb(biz2))
    kiem("danh sach thong bao da gui", any(a["title"] == tieu_de for a in requests.get(G + "/admin/announcements", headers=admin).json()))

    # =================================================================================
    print("\n[10] Gioi han tan suat o gateway (VD-S-12)")
    dat("gateway.rate_limit_auth_per_minute", 3)
    time.sleep(1.5)
    ma = [login_raw(f"khong-co-{TAG}@crowd.local", "x").status_code for _ in range(5)]
    r = login_raw(f"khong-co-{TAG}@crowd.local", "x")
    kiem("dang nhap qua 3 lan/phut/IP → 429 qua_nhieu_yeu_cau + Retry-After",
         429 in ma and r.status_code == 429 and code(r) == "qua_nhieu_yeu_cau" and r.headers.get("Retry-After"), (ma, r.status_code))
    dat("gateway.rate_limit_auth_per_minute", 1000000)
    time.sleep(1.5)
    kiem("nang han muc → dang nhap lai binh thuong", cho(lambda: login_raw("admin@crowd.local").status_code == 200, 10))

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e hoan thien: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
