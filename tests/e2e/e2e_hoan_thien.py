# E2E HOAN THIEN: tai khoan (xac thuc email, quen / doi mat khau, ho so, anh, ky nang), cap do / xep hang,
# thu hoi phien tuc thi + gioi han tan suat (gateway), tu luu + bao loi cau trong bai, thu nhap / chi phi / tien do,
# phan bo nhan + xuat, chong gian lan (theo bai nop), audit / thong ke / thong bao toan he thong.
# Kien truc 3.18: thuong theo CA DU AN, khong con duyet tung nhan / tranh chap nhan da duyet.
# Tu tao tai khoan + du an moi; email kiem THAT qua Mailpit.
# Chay: python e2e_hoan_thien.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import MAILPIT  # noqa: E402
from tai_khoan_e2e import TIEU_DE_DAT_LAI, TIEU_DE_XAC_THUC, thu_moi_nhat, token_trong_email, xac_thuc_email  # noqa: E402

THUONG = 40000
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
    r = rut(chua, 50000)
    kiem("chua xac thuc → rut tien 409 chua_xac_thuc_email", r.status_code == 409 and code(r) == "chua_xac_thuc_email", r.text)
    r = requests.post(G + "/auth/verify-email", json={"token": "khong-dung"})
    kiem("token sai → 400 ma_khong_hop_le", r.status_code == 400 and code(r) == "ma_khong_hop_le", r.text)
    token = token_trong_email(chua["email"], TIEU_DE_XAC_THUC)
    r = requests.post(G + "/auth/verify-email", json={"token": token})
    kiem("bam link → 200 emailVerified", r.status_code == 200 and r.json()["emailVerified"] is True, r.text)
    kiem("dung lai link → 400", requests.post(G + "/auth/verify-email", json={"token": token}).status_code == 400)
    kiem("ho so: emailVerified = true", requests.get(G + "/me/profile", headers=chua["h"]).json()["emailVerified"] is True)
    # Trang thai xac thuc nam trong claim cua access token: token CU (cap truoc khi xac thuc) van bi chan,
    # lam moi token la het — khong service nao giu ban sao.
    r = rut(chua, 50000)
    kiem("token cu (cap truoc khi xac thuc) van bi chan", r.status_code == 409 and code(r) == "chua_xac_thuc_email", r.text)
    lam_moi_token(chua)
    r = rut(chua, 50000)
    kiem("lam moi token → het bi chan (con loi so du)", r.status_code != 409 or code(r) != "chua_xac_thuc_email", r.text)

    biz2 = dang_ky(["business"], "biz2", xac_thuc=False)
    nap(biz2, 500000)
    P0, _ = tao_du_an(biz2, "XT", {"x1": "Mau x"}, thuong=THUONG, suat=3)
    r = requests.post(G + f"/projects/{P0}/publish", headers=biz2["h"])
    kiem("doanh nghiep da KYB nhung chua xac thuc email → publish 409 chua_xac_thuc_email",
         r.status_code == 409 and code(r) == "chua_xac_thuc_email", r.text)
    xac_thuc_email(biz2["email"])
    lam_moi_token(biz2)
    kiem("xac thuc xong + lam moi token → publish duoc", requests.post(G + f"/projects/{P0}/publish", headers=biz2["h"]).status_code == 200)

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
    print("\n[4] Cap do, xep hang (FL-12) theo cau cua BAI DAT → project-svc loc minLevel; thong bao cap moi")
    dat_va_cho("identity.level_rules", "2=1/0;3=2/0", ("identity",))
    l1 = dang_ky(["labeler"], "cap1")
    l2 = dang_ky(["labeler"], "cap2")
    l3 = dang_ky(["labeler"], "cap3")
    P1, _ = tao_du_an(biz, "CAP", {"c1": "Cap 1", "c2": "Cap 2"}, thuong=THUONG, suat=3)
    publish_va_duyet(biz, P1)
    for u in (l1, l2, l3):
        tham_gia(u, P1)
        lam_het(u, P1, lambda _: "a")
    # Bai thu ba lap du suat → dong thu bai → cham: ca ba giong nhau 100% → dat.
    kiem("ba bai giong nhau → dat", cho_cham(l1, P1) and bai_cua_toi(l1, P1)["passed"] is True, bai_cua_toi(l1, P1))
    lv = cho(lambda: (lambda j: j if j["level"] >= 3 else None)(requests.get(G + "/me/level", headers=l1["h"]).json()), 40)
    kiem("bai dat 2 cau + co uy tin → cap 3", lv is not None, requests.get(G + "/me/level", headers=l1["h"]).json())
    kiem("cap cao nhat theo luat → nextLevel null, approvedCount 2 (so cau cua bai dat)",
         lv and lv["nextLevel"] is None and lv["approvedCount"] == 2, lv)
    kiem("thong bao 'Cap do moi: 3'", cho(lambda: "Cấp độ mới: 3" in tb(l1), 20), tb(l1))
    bxh = requests.get(G + "/users/leaderboard", headers=l2["h"], params={"period": "week", "limit": 100}).json()
    # Stack demo co hang tram labeler e2e trong tuan: top 100 co the da day nguoi >= 2 cau.
    kiem("bang xep hang tuan co labeler (2 cau) hoac top da day nguoi nhieu hon",
         any(x["userId"] == l1["id"] and x["approvedCount"] == 2 for x in bxh)
         or (len(bxh) == 100 and min(x["approvedCount"] for x in bxh) >= 2), bxh[:3])
    kiem("bang xep hang sap giam dan theo so cau", [x["approvedCount"] for x in bxh] == sorted([x["approvedCount"] for x in bxh], reverse=True))
    kiem("period sai → 400", requests.get(G + "/users/leaderboard", headers=l2["h"], params={"period": "nam"}).status_code == 400)
    moi_vao = dang_ky(["labeler"], "capthap")
    PL, _ = tao_du_an(biz, "MINLV", {"m1": "Min 1"}, thuong=THUONG, suat=3, dieu_kien={"minLevel": 3, "minReputation": None,
              "requireEntranceTest": False, "entranceQuestionCount": 10, "entrancePassPercent": 80})
    publish_va_duyet(biz, PL)
    # project-svc kiem cap do NGAY LUC VAO du an (cung luat task-svc ap khi mo bai).
    r = requests.post(G + f"/projects/{PL}/join", headers=moi_vao["h"])
    kiem("labeler cap 1 join du an minLevel 3 → 409 chua_du_cap_do", r.status_code == 409 and code(r) == "chua_du_cap_do", r.text)
    tham_gia(l1, PL)
    kiem("labeler cap 3 mo duoc bai (level.changed toi task-svc)", isinstance(bat_dau(l1, PL, 20), dict))

    # =================================================================================
    print("\n[5] Tu luu cau tra loi, bao loi cau (FL-05, FL-07); tien do (FB-20)")
    dat_va_cho("task.report_auto_exclude", 2, ("task",))
    PN, ids_n = tao_du_an(biz, "NHAP", {"n1": "Nhap 1", "n2": "Nhap 2", "n3": "Nhap 3"}, thuong=THUONG, suat=3)
    publish_va_duyet(biz, PN)
    tham_gia(l2, PN)
    b = bat_dau(l2, PN, 20)
    aid = b["attemptId"]
    q0, q1 = b["questions"][0]["sampleId"], b["questions"][1]["sampleId"]
    r = requests.put(G + f"/tasks/attempts/{aid}/answers/{q0}", headers=l2["h"], json={"data": "chuoi"})
    kiem("cau tra loi khong phai object → 400", r.status_code == 400, r.text)
    r = luu(l2, aid, q0, {"nhan": {"labelIds": ["b"]}})
    kiem("luu cau → 200", r.status_code == 200, r.text)
    lai = bat_dau(l2, PN)
    kiem("mo lai → cung bai, giu cau da luu", lai["attemptId"] == aid and next(
        q for q in lai["questions"] if q["sampleId"] == q0)["answer"] == {"nhan": {"labelIds": ["b"]}}, lai)
    r = requests.post(G + f"/tasks/attempts/{aid}/reports", headers=l2["h"], json={"reason": "broken"})
    kiem("bao loi khong kem sampleId → 400 thieu_mau", r.status_code == 400 and code(r) == "thieu_mau", r.text)
    r = requests.post(G + f"/tasks/attempts/{aid}/reports", headers=l2["h"], json={"sampleId": q1, "reason": "other"})
    kiem("bao loi 'other' khong ghi chu → 400", r.status_code == 400 and code(r) == "thieu_ghi_chu", r.text)
    r = requests.post(G + f"/tasks/attempts/{aid}/reports", headers=l2["h"], json={"sampleId": q1, "reason": "broken", "note": "khong doc duoc"})
    kiem("bao loi cau → 200 (bai van mo)", r.status_code == 200 and r.json()["reportsOnTask"] == 1, r.text)
    r = requests.post(G + f"/tasks/attempts/{aid}/reports", headers=l2["h"], json={"sampleId": q1, "reason": "broken", "note": "lan hai"})
    kiem("bao lai cung cau → 409 da_bao_loi", r.status_code == 409 and code(r) == "da_bao_loi", r.text)
    kiem("nop bai (cau bao loi bo trong) → 200", nop_bai(l2, aid).status_code == 200)
    rp = requests.get(G + f"/tasks/projects/{PN}/reports", headers=biz["h"]).json()
    kiem("chu du an thay bao loi dang mo", len(rp) == 1 and rp[0]["reason"] == "broken" and rp[0]["note"] == "khong doc duoc", rp)
    kiem("labeler xem bao loi cua du an → 404", requests.get(G + f"/tasks/projects/{PN}/reports", headers=l2["h"]).status_code == 404)
    r = requests.post(G + f"/tasks/reports/{rp[0]['id']}/resolve", headers=biz["h"], json={"action": "xoa"})
    kiem("hanh dong la → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/tasks/reports/{rp[0]['id']}/resolve", headers=biz["h"], json={"action": "dismiss"})
    kiem("bo qua bao loi → dismissed", r.status_code == 200 and r.json()["state"] == "dismissed", r.text)

    P1M, ids_m = tao_du_an(biz, "BAOLOI", {"b1": "Bao loi 1", "b2": "Bao loi 2"}, thuong=THUONG, suat=3)
    publish_va_duyet(biz, P1M)
    for u in (l1, l2):
        tham_gia(u, P1M)
        x = bat_dau(u, P1M, 20)
        requests.post(G + f"/tasks/attempts/{x['attemptId']}/reports", headers=u["h"],
                      json={"sampleId": ids_m["b1"], "reason": "inappropriate"}).raise_for_status()
    kiem("2 nguoi bao cung cau → tu loai, moi bao loi cua cau → excluded",
         all(x["state"] == "excluded" for x in requests.get(G + f"/tasks/projects/{P1M}/reports?status=all", headers=biz["h"]).json()))
    td = requests.get(G + f"/tasks/projects/{P1}/progress", headers=biz["h"]).json()
    kiem("tien do P1: 3/3 suat da nop, du suat → da cham", td["slots"] == 3 and td["submitted"] == 3
         and td["finishReason"] == "du_suat" and td["phase"] in ("graded", "settled") and td["appealDeadline"], td)
    ds_bai = requests.get(G + f"/tasks/projects/{P1}/attempts", headers=biz["h"]).json()
    kiem("bai cua du an: 3 bai dat", sum(1 for x in ds(ds_bai) if x.get("passed") is True) == 3, ds_bai)

    # =================================================================================
    print("\n[6] Thu nhap (FL-11), chi phi du an (FB-20), phan bo nhan + xuat (FB-24, FB-25)")
    tn = cho(lambda: (lambda j: j if j["totalVnd"] == THUONG else None)(requests.get(G + "/ledger/me/earnings", headers=l1["h"]).json()), 30)
    kiem("thu nhap theo ngay = thuong mot bai dat", tn and tn["byKindVnd"].get("projectReward") == THUONG and len(tn["items"]) == 1, tn)
    tn = requests.get(G + "/ledger/me/earnings?groupBy=project", headers=l1["h"]).json()
    kiem("thu nhap theo du an", any(x["key"] == P1 and x["amountVnd"] == THUONG for x in tn["items"]), tn)
    kiem("groupBy la → 400", requests.get(G + "/ledger/me/earnings?groupBy=thang", headers=l1["h"]).status_code == 400)
    kiem("khoang > 366 ngay → 400", requests.get(G + "/ledger/me/earnings?from=2024-01-01&to=2026-01-01", headers=l1["h"]).status_code == 400)
    cp = requests.get(G + f"/ledger/projects/{P1}/cost", headers=biz["h"]).json()
    kiem("chi phi du an: 3 suat x thuong, da tra 3 bai dat", cp["slots"] == 3 and cp["rewardPerAttemptVnd"] == THUONG
         and cp["paidAttempts"] == 3 and cp["heldVnd"] == 0
         and cp["spentRewardsVnd"] == 3 * (THUONG + cp["platformFeePerAttemptVnd"]) and cp["reservedVnd"] == cp["spentRewardsVnd"], cp)
    kiem("doanh nghiep khac xem chi phi → 404", requests.get(G + f"/ledger/projects/{P1}/cost", headers=biz2["h"]).status_code == 404)
    dat_va_cho("annotation.imbalance_min_labels", 2, ("annotation",))
    pb = cho(lambda: (lambda j: j if j["imbalanceWarnings"] else None)(
        requests.get(G + f"/annotations/projects/{P1}/distribution", headers=biz["h"]).json()), 30) or {}
    kiem("canh bao lech: 'a' ap dao, 'b' chua co nhan",
         any(w["code"] == "lop_ap_dao" and w["label"] == "a" for w in pb.get("imbalanceWarnings", []))
         and any(w["code"] == "lop_chua_co_nhan" and w["label"] == "b" for w in pb.get("imbalanceWarnings", [])), pb)
    r = requests.get(G + f"/annotations/projects/{P1}/export?format=yolo", headers=biz["h"])
    kiem("xuat yolo du an van ban → 400 khung_chi_cho_anh", r.status_code == 400 and code(r) == "khung_chi_cho_anh", r.text)
    kiem("dinh dang la → 400", requests.get(G + f"/annotations/projects/{P1}/export?format=xml", headers=biz["h"]).status_code == 400)

    # =================================================================================
    print("\n[7] Khong con tranh chap tung nhan (3.18): thay bang khieu nai ca bai")
    kiem("API tranh chap cu da go → 404", requests.post(G + "/ledger/disputes", headers=biz["h"], json={"annotationId": str(uuid.uuid4()), "reason": "x"}).status_code == 404)
    kiem("doi soat so cai lanh", requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()["healthy"])

    # =================================================================================
    print("\n[8] Chong gian lan (FM-07) theo bai nop → admin xac nhan → identity khoa")
    for k, v in (("quality.fraud_fast_window", 3), ("quality.fraud_min_seconds", 30),
                 ("quality.fraud_same_label_streak", 3), ("quality.fraud_shared_accounts", 2),
                 ("quality.fraud_ignore_private_ips", False), ("quality.fraud_reopen_cooldown", 0)):
        dat_va_cho(k, v, ("quality",))
    lf = dang_ky(["labeler"], "gianlan")
    lx = dang_ky(["labeler"], "dadang")
    PF, _ = tao_du_an(biz, "GL", {f"g{i}": f"Gian lan {i}" for i in range(1, 5)}, thuong=THUONG, suat=3)
    publish_va_duyet(biz, PF)
    for u in (lf, lx):
        tham_gia(u, PF)
    thiet_bi = f"thiet-bi-{TAG}"
    dem = {"i": 0}

    def xen_ke(_):
        dem["i"] += 1
        return "a" if dem["i"] % 2 else "b"

    def nop_tu_thiet_bi(u, tra_loi):
        bai, _ = lam_bai(u, PF, tra_loi, nop=False)
        requests.post(G + f"/tasks/attempts/{bai['attemptId']}/submit", headers=dict(u["h"], **{"X-Device-Id": thiet_bi})).raise_for_status()
    nop_tu_thiet_bi(lx, xen_ke)
    cho(lambda: sql("quality", f"select count(*) from labels where project_id = '{PF}' and labeler_id = '{lx['id']}'") == "4", 30)
    nop_tu_thiet_bi(lf, lambda _: "a")

    def ho_so(loai, uid):
        j = requests.get(G + "/quality/admin/fraud-cases", headers=admin, params={"status": "open", "type": loai, "pageSize": 100}).json()
        return next((c for c in j["items"] if uid in c["userIds"]), None)
    hs_nhanh = cho(lambda: ho_so("qua_nhanh", lf["id"]), 30)
    kiem("4 cau trong vai giay (< 30 giay/cau) → ho so qua_nhanh",
         hs_nhanh is not None and hs_nhanh["evidence"]["answeredCount"] == 4 and hs_nhanh["evidence"]["secondsPerQuestion"] < 30, hs_nhanh)
    kiem("chon 'a' 4 cau lien tiep (nguoi khac chon a, b) → ho so mot_nhan_lien_tuc",
         cho(lambda: ho_so("mot_nhan_lien_tuc", lf["id"]), 30) is not None)
    kiem("2 tai khoan chung IP → ho so chung_ip", cho(lambda: ho_so("chung_ip", lf["id"]), 30) is not None)
    hs_tb = cho(lambda: ho_so("chung_thiet_bi", lf["id"]), 30)
    kiem("2 tai khoan chung X-Device-Id → ho so chung_thiet_bi", hs_tb is not None and lx["id"] in hs_tb["userIds"], hs_tb)
    kiem("labeler xem ho so → 403", requests.get(G + "/quality/admin/fraud-cases", headers=lf["h"]).status_code == 403)
    ct = requests.get(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}", headers=admin).json()
    kiem("chi tiet co lich su nguoi dung", any(x["userId"] == lf["id"] and x["submissions"] >= 1 for x in ct["users"]), ct)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin, json={"action": "confirm"})
    kiem("xac nhan khong ghi chu → 400", r.status_code == 400 and code(r) == "thieu_ghi_chu", r.text)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_tb['id']}/resolve", headers=admin, json={"action": "dismiss", "note": "Cung gia dinh"})
    kiem("bo qua ho so chung thiet bi", r.status_code == 200 and r.json()["status"] == "dismissed", r.text)
    r = requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin,
                      json={"action": "confirm", "note": "Nop 4 cau trong vai giay", "blockAccounts": True})
    kiem("xac nhan + khoa tai khoan", r.status_code == 200 and r.json()["status"] == "confirmed" and r.json()["blocked"], r.text)
    kiem("identity khoa labeler (dang nhap 403)", cho(lambda: login_raw(lf["email"]).status_code == 403, 30))
    kiem("access token cua labeler bi gateway chan", cho(lambda: code(requests.get(G + "/me", headers=lf["h"])) == "phien_da_bi_thu_hoi", 10))
    kiem("xu ly lan hai → 409", requests.post(G + f"/quality/admin/fraud-cases/{hs_nhanh['id']}/resolve", headers=admin,
                                             json={"action": "dismiss", "note": "x"}).status_code == 409)
    requests.post(G + f"/users/admin/{lf['id']}/unblock", headers=admin).raise_for_status()
    lf2 = dang_nhap(lf["email"], PW)
    PU, _ = tao_du_an(biz, "MOKHOA", {"u1": "Mo khoa"}, thuong=THUONG, suat=3)
    publish_va_duyet(biz, PU)
    tham_gia(lf2, PU)
    kiem("mo khoa → mo bai lai duoc (task-svc nghe user.unblocked)",
         cho(lambda: isinstance(bat_dau(lf2, PU, 1), dict), 20))
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
    j = requests.get(G + "/admin/audit", headers=admin, params={"eventType": "project.", "pageSize": 100}).json()
    kiem("loc tien to 'project.' (published / graded / completed)",
         {"project.published", "project.graded"} <= {x["eventType"] for x in j["items"]}, j["total"])
    kiem("labeler xem audit → 403", requests.get(G + "/admin/audit", headers=l1["h"]).status_code == 403)
    tk = requests.get(G + "/admin/stats/overview", headers=admin).json()
    kiem("thong ke: dang ky, du an da cham, tien tra labeler, 30 ngay",
         tk["newUsers"] > 0 and tk["projectsGraded"] > 0 and tk["labelerPayoutsVnd"] >= 3 * THUONG and len(tk["daily"]) == 30, tk)
    kiem("from > to → 400", requests.get(G + "/admin/stats/overview?from=2026-10-10&to=2026-10-01", headers=admin).status_code == 400)
    r = requests.post(G + "/admin/announcements", headers=admin, json={"title": "x", "body": "y", "audience": "nguoi_ngoai_hanh_tinh"})
    kiem("doi tuong la → 400", r.status_code == 400 and code(r) == "doi_tuong_khong_hop_le", r.text)
    tieu_de = f"Bao tri he thong {TAG}"
    r = requests.post(G + "/admin/announcements", headers=admin,
                      json={"title": tieu_de, "body": "He thong bao tri 22h.", "audience": "labeler", "sendEmail": True})
    kiem("gui thong bao cho labeler → 201", r.status_code == 201, r.text)
    # Gui cho MOI labeler (hang tram tren stack demo): kiem tren tai khoan tao gan nhat (gui truoc).
    kiem("labeler nhan thong bao + email", cho(lambda: tieu_de in tb(lx) and tieu_de in thu_cua(lx["email"]), 60), tb(lx))
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
