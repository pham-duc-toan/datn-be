# E2E KYB + THONG BAO + KHOA TAI KHOAN (FB-01/02, FM-01, FC-06).
# Tu tao tai khoan moi; kiem ca thong bao trong ung dung (notification-svc) lan EMAIL that (Mailpit).
# Chay: python e2e_kyb_thong_bao.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import MAILPIT  # noqa: E402


def thu_cua(email):
    """Tieu de cac email da gui toi `email` (Mailpit)."""
    r = requests.get(f"{MAILPIT}/api/v1/search", params={"query": f'to:"{email}"', "limit": 200})
    return [m["Subject"] for m in r.json().get("messages", [])]


def tb(u):
    return requests.get(G + "/notifications/me?pageSize=100", headers=u["h"]).json()


def co_tb(u, tieu_de, giay=30):
    return cho(lambda: any(x["title"] == tieu_de for x in tb(u)["items"]), giay)


def co_thu(u, tieu_de, giay=45):
    return cho(lambda: tieu_de in thu_cua(u["email"]), giay)


try:
    # =================================================================================
    print("\n[1] KYB: chua xac minh thi khong publish; khai ho so, giay phep, nop, admin duyet / tu choi")
    biz = dang_ky(["business"], "kyb", xac_minh=False)
    lab = dang_ky(["labeler"], "kyblab")
    kiem("ho so moi o trang thai draft", requests.get(G + "/kyb/me", headers=biz["h"]).json()["status"] == "draft")
    P, _ = tao_du_an(biz, "KYB", {f"k{i}": f"Mau kyb {i}" for i in range(1, 5)}, 1, 1, 20000)
    r = requests.post(G + f"/projects/{P}/publish", headers=biz["h"])
    kiem("chua xac minh → publish 409 chua_xac_minh_doanh_nghiep", r.status_code == 409 and "chua_xac_minh_doanh_nghiep" in r.text, r.text)

    r = requests.put(G + "/kyb/me", headers=biz["h"], json={"companyName": "Cong ty KYB", "taxCode": "12345", "address": "Ha Noi"})
    kiem("ma so thue sai → 400", r.status_code == 400 and "ma_so_thue_khong_hop_le" in r.text, r.text)
    r = requests.put(G + "/kyb/me", headers=biz["h"], json={"companyName": "Cong ty KYB", "taxCode": "0312345678", "address": "Ha Noi"})
    kiem("khai ho so hop le", r.status_code == 200, r.text)
    r = requests.post(G + "/kyb/me/submit", headers=biz["h"])
    kiem("nop khi chua co giay phep → 409 thieu_giay_phep", r.status_code == 409 and "thieu_giay_phep" in r.text, r.text)
    r = requests.post(G + "/kyb/me/license", headers=biz["h"], json={"fileName": "virus.exe", "sizeBytes": 100})
    kiem("giay phep .exe → 400", r.status_code == 400 and "file_khong_hop_le" in r.text, r.text)
    r = requests.post(G + "/kyb/me/license", headers=biz["h"], json={"fileName": "gp.pdf", "sizeBytes": 100 * 1024 * 1024})
    kiem("giay phep 100 MB → 400", r.status_code == 400 and "file_qua_lon" in r.text, r.text)
    r = requests.post(G + "/kyb/me/license", headers=biz["h"], json={"fileName": "gp.pdf", "sizeBytes": len(PDF_MAU)})
    r = requests.post(G + "/kyb/me/submit", headers=biz["h"])
    kiem("xin link nhung chua tai file len → nop 409", r.status_code == 409 and "thieu_giay_phep" in r.text, r.text)

    r = nop_kyb(biz, "Cong ty KYB")
    kiem("tai giay phep + nop → submitted", r.status_code == 200 and r.json()["status"] == "submitted", r.text)
    kiem("dang cho duyet → sua 409", requests.put(G + "/kyb/me", headers=biz["h"],
         json={"companyName": "X", "taxCode": "0312345678", "address": "Y"}).status_code == 409)
    kiem("nop lai khi dang cho → 409", requests.post(G + "/kyb/me/submit", headers=biz["h"]).status_code == 409)
    kiem("labeler vao /kyb/me → 403", requests.get(G + "/kyb/me", headers=lab["h"]).status_code == 403)
    kiem("labeler vao hang duyet KYB → 403", requests.get(G + "/kyb/admin", headers=lab["h"]).status_code == 403)
    kiem("doanh nghiep vao hang duyet KYB → 403", requests.get(G + "/kyb/admin", headers=biz["h"]).status_code == 403)
    hang = requests.get(G + "/kyb/admin?status=submitted&pageSize=100", headers=admin).json()
    kiem("admin thay ho so trong hang doi", any(x["profile"]["businessId"] == biz["id"] for x in hang["items"]), hang)
    ct = requests.get(G + f"/kyb/admin/{biz['id']}", headers=admin).json()
    kiem("chi tiet co email + link xem giay phep", ct["email"] == biz["email"] and ct["licenseUrl"], ct)
    gp = requests.get(ct["licenseUrl"])
    kiem("link xem tai dung file giay phep", gp.status_code == 200 and gp.content == PDF_MAU, gp.status_code)

    r = requests.post(G + f"/kyb/admin/{biz['id']}/reject", headers=admin, json={"reason": ""})
    kiem("tu choi khong ly do → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/kyb/admin/{biz['id']}/reject", headers=admin, json={"reason": "Giay phep bi mo"})
    kiem("tu choi → rejected", r.status_code == 200 and r.json()["status"] == "rejected", r.text)
    kiem("thong bao trong ung dung: ho so bi tu choi", co_tb(biz, "Hồ sơ doanh nghiệp bị từ chối"), tb(biz))
    kiem("email: ho so bi tu choi (bat buoc)", co_thu(biz, "Hồ sơ doanh nghiệp bị từ chối"), thu_cua(biz["email"]))
    kiem("noi dung co ly do", any("Giay phep bi mo" in x["body"] for x in tb(biz)["items"]))

    r = nop_kyb(biz, "Cong ty KYB")
    kiem("sua + nop lai sau tu choi → submitted", r.status_code == 200 and r.json()["status"] == "submitted" and r.json()["rejectReason"] is None, r.text)
    r = requests.post(G + f"/kyb/admin/{biz['id']}/approve", headers=admin)
    kiem("admin duyet → verified", r.status_code == 200 and r.json()["status"] == "verified", r.text)
    kiem("duyet lan hai → 409", requests.post(G + f"/kyb/admin/{biz['id']}/approve", headers=admin).status_code == 409)
    kiem("email: doanh nghiep da xac minh", co_thu(biz, "Doanh nghiệp đã được xác minh"), thu_cua(biz["email"]))
    r = requests.post(G + f"/projects/{P}/publish", headers=biz["h"])
    kiem("token cu (cap truoc khi duoc duyet) van bi chan", r.status_code == 409 and "chua_xac_minh_doanh_nghiep" in r.text, r.text)
    lam_moi_token(biz)
    r = requests.post(G + f"/projects/{P}/publish", headers=biz["h"])
    kiem("lam moi token (claim kyb_verified) → publish duoc (sang buoc ky quy)", r.status_code == 200, r.text)
    kiem("chua nap tien → thong bao 'Du an chua publish duoc' (escrow.rejected)",
         co_tb(biz, "Dự án chưa publish được"), [x["title"] for x in tb(biz)["items"]])

    # =================================================================================
    print("\n[2] API thong bao: dem chua doc, doc, cua nguoi khac, tuy chon email")
    n = requests.get(G + "/notifications/me/unread-count", headers=biz["h"]).json()["count"]
    kiem("so chua doc >= 3", n >= 3, n)
    mot = tb(biz)["items"][0]
    kiem("doc mot thong bao → 204", requests.post(G + f"/notifications/me/{mot['id']}/read", headers=biz["h"]).status_code == 204)
    kiem("so chua doc giam 1", requests.get(G + "/notifications/me/unread-count", headers=biz["h"]).json()["count"] == n - 1)
    kiem("doc lai → van 204 (idempotent)", requests.post(G + f"/notifications/me/{mot['id']}/read", headers=biz["h"]).status_code == 204)
    kiem("doc thong bao cua nguoi khac → 404", requests.post(G + f"/notifications/me/{mot['id']}/read", headers=lab["h"]).status_code == 404)
    kiem("chi lay chua doc", all(x["readAt"] is None for x in requests.get(G + "/notifications/me?unreadOnly=true", headers=biz["h"]).json()["items"]))
    r = requests.post(G + "/notifications/me/read-all", headers=biz["h"])
    kiem("doc het", r.status_code == 200 and requests.get(G + "/notifications/me/unread-count", headers=biz["h"]).json()["count"] == 0, r.text)
    kiem("khong token → 401", requests.get(G + "/notifications/me").status_code == 401)

    tc = requests.get(G + "/notifications/me/preferences", headers=biz["h"]).json()
    kiem("14 loai, rut_tien bat buoc, ket_qua_bai co email, tien_kha_dung mac dinh tat email",
         len(tc) == 14 and next(x for x in tc if x["type"] == "rut_tien")["mandatory"]
         and next(x for x in tc if x["type"] == "ket_qua_bai")["emailEnabled"]
         and not next(x for x in tc if x["type"] == "tien_kha_dung")["emailEnabled"], tc)
    r = requests.put(G + "/notifications/me/preferences", headers=biz["h"], json={"items": [{"type": "rut_tien", "emailEnabled": False}]})
    kiem("tat email loai bat buoc → 400", r.status_code == 400 and "loai_bat_buoc" in r.text, r.text)
    r = requests.put(G + "/notifications/me/preferences", headers=biz["h"], json={"items": [{"type": "khong_co", "emailEnabled": False}]})
    kiem("loai khong ton tai → 400", r.status_code == 400, r.text)
    r = requests.put(G + "/notifications/me/preferences", headers=biz["h"], json={"items": [{"type": "du_an", "emailEnabled": False}]})
    kiem("tat email loai du_an", r.status_code == 200 and not next(x for x in r.json() if x["type"] == "du_an")["emailEnabled"], r.text)

    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 1000000}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount=1000000")
    kiem("nap tien → thong bao + email 'Nap tien thanh cong'",
         co_tb(biz, "Nạp tiền thành công") and co_thu(biz, "Nạp tiền thành công"), thu_cua(biz["email"]))
    publish_va_duyet(biz, P)
    kiem("du an duoc duyet → thong bao trong ung dung", co_tb(biz, "Dự án đã được duyệt"))
    time.sleep(12)  # > 2 chu ky EmailWorker
    kiem("da tat email du_an → KHONG co email 'Du an da duoc duyet'", "Dự án đã được duyệt" not in thu_cua(biz["email"]), thu_cua(biz["email"]))

    # =================================================================================
    print("\n[3] Thong bao cho labeler: ket qua cham bai, tien kha dung, rut tien")
    dat_va_cho("ledger.reward_hold", 0, ("ledger",))
    dat_va_cho("ledger.withdraw_min_vnd", 10000, ("ledger",))
    dat_va_cho("ledger.withdraw_auto_approve_max_vnd", 15000, ("ledger",))
    requests.post(G + f"/projects/{P}/join", headers=lab["h"]).raise_for_status()
    b, r = lam_bai(lab, P, lambda t: "a")
    r.raise_for_status()
    ket_thuc_som(biz, P)
    # Mot bai (< 3) → chi xet cau vang; du an khong co cau vang → dat, nhan thuong 80.000.
    kiem("bai dat → thong bao + email 'Bai lam da dat' (loai ket_qua_bai, link /attempts/{id})",
         co_tb(lab, "Bài làm đã đạt", 60) and co_thu(lab, "Bài làm đã đạt")
         and any(x["link"] == f"/attempts/{b['attemptId']}" for x in tb(lab)["items"] if x["title"] == "Bài làm đã đạt"), thu_cua(lab["email"]))
    kiem("chu du an: thong bao 'Du an da cham xong' (1/1 bai dat)",
         co_tb(biz, "Dự án đã chấm xong", 30) and any(x["body"].startswith("1/1 bài đạt") for x in tb(biz)["items"] if x["title"] == "Dự án đã chấm xong"))

    def cung_loai(tieu_de):
        return [x for x in tb(lab)["items"] if x["title"] == tieu_de]

    kiem("thuong vao so du kha dung ngay → thong bao 'Tien da kha dung' 80.000 d",
         cho(lambda: any(x["body"].startswith("80.000 đ") for x in cung_loai("Tiền đã khả dụng")), 40), cung_loai("Tiền đã khả dụng"))
    kiem("tien kha dung: mac dinh KHONG email", "Tiền đã khả dụng" not in thu_cua(lab["email"]), thu_cua(lab["email"]))

    def rut(so):
        return requests.post(G + "/ledger/withdrawals", headers=dict(lab["h"], **{"Idempotency-Key": uuid.uuid4().hex}),
                             json={"amountVnd": so, "bankAccount": "VCB-0123456789"})

    cho(lambda: vi(lab)["availableVnd"] >= 20000, 30)
    w = rut(18000).json()
    kiem("rut 18.000 > nguong → cho duyet", w["state"] == "pendingApproval", w)
    requests.post(G + f"/ledger/admin/withdrawals/{w['id']}/reject", headers=admin, json={"reason": "Sai so tai khoan"}).raise_for_status()
    kiem("admin tu choi lenh rut → thong bao + email (event withdrawal.rejected moi)",
         co_tb(lab, "Lệnh rút bị từ chối") and co_thu(lab, "Lệnh rút bị từ chối"), thu_cua(lab["email"]))
    w = rut(12000).json()
    kiem("rut 12.000 → tu duyet → thong bao + email 'Rut tien thanh cong'",
         co_tb(lab, "Rút tiền thành công", 45) and co_thu(lab, "Rút tiền thành công"), (w, thu_cua(lab["email"])))

    # =================================================================================
    print("\n[4] Khoa / mo khoa tai khoan (FM-01)")
    kiem("doanh nghiep khoa tai khoan → 403", requests.post(G + f"/users/admin/{lab['id']}/block", headers=biz["h"],
                                                            json={"reason": "x"}).status_code == 403)
    admin_id = requests.get(G + "/me", headers=admin).json()["userId"]
    r = requests.post(G + f"/users/admin/{admin_id}/block", headers=admin, json={"reason": "thu"})
    kiem("khoa tai khoan admin → 409", r.status_code == 409 and "khong_khoa_admin" in r.text, r.text)
    kiem("khoa khong ly do → 400", requests.post(G + f"/users/admin/{lab['id']}/block", headers=admin, json={"reason": ""}).status_code == 400)
    r = requests.post(G + f"/users/admin/{lab['id']}/block", headers=admin, json={"reason": "Gian lan nhan"})
    kiem("admin khoa labeler", r.status_code == 200 and r.json()["status"] == "blocked", r.text)
    kiem("dang nhap khi bi khoa → 403", login_raw(lab["email"]).status_code == 403)
    kiem("refresh token cu bi thu hoi → 401", requests.post(G + "/auth/refresh", json={"refreshToken": lab["refresh"]}).status_code == 401)
    kiem("access token con han bi gateway chan ngay (401 phien_da_bi_thu_hoi, VD-S-11)",
         cho(lambda: requests.get(G + "/tasks/me/attempts", headers=lab["h"]).status_code == 401, 20))
    # Token cua lab da bi thu hoi → khong doc thong bao trong ung dung duoc luc nay; kiem email,
    # thong bao trong ung dung kiem sau khi mo khoa + dang nhap lai.
    kiem("email 'Tai khoan bi khoa' (bat buoc)", co_thu(lab, "Tài khoản bị khoá"), thu_cua(lab["email"]))
    ds = requests.get(G + "/users/admin", headers=admin, params={"search": lab["email"], "status": "blocked"}).json()
    kiem("admin tim thay tai khoan bi khoa (kem ly do)",
         ds["total"] == 1 and ds["items"][0]["blockedReason"] == "Gian lan nhan", ds)
    ds = requests.get(G + "/users/admin", headers=admin, params={"search": biz["email"]}).json()
    kiem("danh sach co trang thai KYB cua doanh nghiep", ds["items"][0]["kybStatus"] == "verified", ds)
    kiem("doanh nghiep vao danh sach tai khoan → 403", requests.get(G + "/users/admin", headers=biz["h"]).status_code == 403)
    r = requests.post(G + f"/users/admin/{lab['id']}/unblock", headers=admin)
    kiem("mo khoa", r.status_code == 200 and r.json()["status"] == "active", r.text)
    r = login_raw(lab["email"])
    kiem("dang nhap lai duoc", r.status_code == 200)
    lab["h"] = hdr(r.json()["accessToken"])
    kiem("dang nhap lai thay thong bao 'Tai khoan bi khoa'", co_tb(lab, "Tài khoản bị khoá"))
    kiem("email 'Tai khoan da duoc mo khoa'", co_thu(lab, "Tài khoản đã được mở khoá"), thu_cua(lab["email"]))

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e kyb thong bao: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
