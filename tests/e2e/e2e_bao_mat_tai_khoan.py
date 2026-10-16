# E2E BAO MAT TAI KHOAN: xac thuc hai lop (FC-04), xac minh danh tinh / du tuoi (muc 2.9, FP-05, VD-T-04),
# gioi han tan suat dung chung qua Redis (VD-S-12).
# Chay: python e2e_bao_mat_tai_khoan.py   (stack demo: E2E_PROFILE=demo)
import base64, hashlib, hmac, json, os, struct, subprocess, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import MAILPIT, TIEN_TO_CONTAINER  # noqa: E402


def totp(secret_b32, buoc=None):
    """TOTP RFC 6238 (SHA-1, 6 so, 30 giay) — viet doc lap voi C# de kiem cheo."""
    key = base64.b32decode(secret_b32 + "=" * (-len(secret_b32) % 8))
    if buoc is None:
        buoc = int(time.time()) // 30
    h = hmac.new(key, struct.pack(">Q", buoc), hashlib.sha1).digest()
    o = h[-1] & 0x0F
    return "%06d" % ((struct.unpack(">I", h[o:o + 4])[0] & 0x7FFFFFFF) % 1000000)


def ma_sau(secret, da_dung):
    """Ma cua buoc dau tien > da_dung ma server con chap nhan (hien tai +-1); chua toi thi cho."""
    while True:
        bay_gio = int(time.time()) // 30
        b = max(bay_gio - 1, da_dung + 1)
        if b <= bay_gio + 1:
            return b, totp(secret, b)
        time.sleep(1)


def claims(u):
    tok = u["h"]["Authorization"].split(" ")[1].split(".")[1]
    return json.loads(base64.urlsafe_b64decode(tok + "=" * (-len(tok) % 4)))


def thu_cua(email):
    r = requests.get(f"{MAILPIT}/api/v1/search", params={"query": f'to:"{email}"', "limit": 200})
    return [m["Subject"] for m in r.json().get("messages", [])]


ANH = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")


def tai_anh(u, mat):
    r = requests.post(G + "/me/identity-verification/upload-url", headers=u["h"],
                      json={"side": mat, "fileName": f"{mat}.png", "sizeBytes": len(ANH)})
    assert r.status_code == 200, r.text
    requests.put(r.json()["uploadUrl"], data=ANH).raise_for_status()


try:
    # =================================================================================
    print("\n[1] 2FA: cai dat, bat bang ma that, dang nhap hai buoc")
    u = dang_ky(["labeler"], "hailop")
    r = requests.get(G + "/me/2fa", headers=u["h"])
    kiem("mac dinh chua bat 2FA", r.status_code == 200 and r.json()["enabled"] is False, r.text)

    r = requests.post(G + "/me/2fa/setup", headers=u["h"])
    kiem("setup tra secret + otpauth", r.status_code == 200 and r.json()["otpauthUri"].startswith("otpauth://totp/"), r.text)
    secret = r.json()["secret"]
    kiem("cai dat xong nhung CHUA bat → dang nhap van ra token ngay", "accessToken" in login_raw(u["email"]).json())

    r = requests.post(G + "/me/2fa/enable", headers=u["h"], json={"code": "000000" if totp(secret) != "000000" else "111111"})
    kiem("bat bang ma sai → 400 ma_2fa_sai", r.status_code == 400 and "ma_2fa_sai" in r.text, r.text)
    r = requests.post(G + "/me/2fa/enable", headers=u["h"], json={"code": totp(secret)})
    kiem("bat bang ma dung → 10 ma du phong", r.status_code == 200 and len(r.json()["recoveryCodes"]) == 10, r.text)
    du_phong = r.json()["recoveryCodes"]
    kiem("bat lai lan nua → 409", requests.post(G + "/me/2fa/enable", headers=u["h"], json={"code": totp(secret)}).status_code == 409)
    kiem("setup khi dang bat → 409 hai_lop_dang_bat", "hai_lop_dang_bat" in requests.post(G + "/me/2fa/setup", headers=u["h"]).text)
    st = requests.get(G + "/me/2fa", headers=u["h"]).json()
    kiem("trang thai: enabled, con 10 ma du phong", st["enabled"] is True and st["recoveryCodesLeft"] == 10, st)

    r = login_raw(u["email"])
    j = r.json()
    kiem("dang nhap dung mat khau → CHUA co token, co challengeToken",
         r.status_code == 200 and j.get("twoFactorRequired") is True and "accessToken" not in j and j.get("challengeToken"), j)
    kiem("sai mat khau van 401 nhu cu (khong lo tai khoan bat 2FA)", login_raw(u["email"], "Sai@12345").status_code == 401)
    ch = j["challengeToken"]
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch, "code": "12345"})
    kiem("ma sai dinh dang → 401 ma_2fa_sai, attemptsLeft = 4", r.status_code == 401 and r.json().get("attemptsLeft") == 4, r.text)
    buoc = int(time.time()) // 30
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch, "code": totp(secret, buoc + 1)})
    kiem("ma dung (lech +1 buoc) → token", r.status_code == 200 and "accessToken" in r.json(), r.text)
    kiem("dung lai thu thach da dung → 401 thu_thach_khong_hop_le",
         "thu_thach_khong_hop_le" in requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch, "code": totp(secret)}).text)
    tok = r.json()
    kiem("token sau 2FA dung duoc", requests.get(G + "/me", headers=hdr(tok["accessToken"])).status_code == 200)

    ch2 = login_raw(u["email"]).json()["challengeToken"]
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch2, "code": totp(secret, buoc + 1)})
    kiem("dung LAI ma vua dung (cung buoc) → bi tu choi (chong replay)", r.status_code == 401 and "ma_2fa_sai" in r.text, r.text)
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch2, "recoveryCode": du_phong[0].lower()})
    kiem("ma du phong (chu thuong van nhan) → token", r.status_code == 200 and "accessToken" in r.json(), r.text)
    ch3 = login_raw(u["email"]).json()["challengeToken"]
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch3, "recoveryCode": du_phong[0]})
    kiem("ma du phong da dung → bi tu choi", r.status_code == 401, r.text)
    kiem("con 9 ma du phong", requests.get(G + "/me/2fa", headers=u["h"]).json()["recoveryCodesLeft"] == 9)

    ch4 = login_raw(u["email"]).json()["challengeToken"]
    con = None
    for i in range(5):
        r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch4, "code": "999999" if totp(secret) != "999999" else "888888"})
        con = r.json()
    kiem("sai 5 lan → het_luot_2fa", con.get("code") == "het_luot_2fa", con)
    r = requests.post(G + "/auth/2fa/verify", json={"challengeToken": ch4, "code": totp(secret)})
    kiem("het luot roi thi ma dung cung khong dung duoc", r.status_code == 401 and "thu_thach_khong_hop_le" in r.text, r.text)

    _, ma = ma_sau(secret, buoc + 1)
    r = requests.post(G + "/me/2fa/recovery-codes", headers=u["h"], json={"code": ma})
    kiem("tao lai bo ma du phong (can ma) → 10 ma moi", r.status_code == 200 and len(r.json()["recoveryCodes"]) == 10, r.text)
    moi = r.json()["recoveryCodes"]

    r = requests.post(G + "/me/2fa/disable", headers=u["h"], json={"password": "Sai@12345", "recoveryCode": moi[0]})
    kiem("tat 2FA sai mat khau → 400", r.status_code == 400 and "mat_khau_hien_tai_sai" in r.text, r.text)
    r = requests.post(G + "/me/2fa/disable", headers=u["h"], json={"password": PW, "recoveryCode": moi[1]})
    kiem("tat 2FA (mat khau + ma du phong) → 204", r.status_code == 204, r.text)
    kiem("tat roi → dang nhap ra token ngay", "accessToken" in login_raw(u["email"]).json())

    # admin go 2FA cho nguoi mat dien thoai
    s2 = requests.post(G + "/me/2fa/setup", headers=u["h"]).json()["secret"]
    requests.post(G + "/me/2fa/enable", headers=u["h"], json={"code": totp(s2)}).raise_for_status()
    kiem("labeler khong goi duoc API go 2FA cua admin",
         requests.post(G + f"/users/admin/{u['id']}/2fa/reset", headers=u["h"]).status_code == 403)
    r = requests.post(G + f"/users/admin/{u['id']}/2fa/reset", headers=admin)
    kiem("admin go 2FA → 204", r.status_code == 204, r.text)
    kiem("go roi → dang nhap thang", "accessToken" in login_raw(u["email"]).json())
    kiem("go lan nua → 409 hai_lop_chua_bat", "hai_lop_chua_bat" in requests.post(G + f"/users/admin/{u['id']}/2fa/reset", headers=admin).text)
    kiem("bi mat 2FA luu dang MA HOA (khong thay secret base32 trong DB)",
         s2 not in sql("identity", "select coalesce(string_agg(secret_cipher, ','), '') from two_factor"))

    # =================================================================================
    print("\n[2] Xac minh danh tinh: khai, tai anh giay to, admin duyet → claim identity_verified / adult")
    a = dang_ky(["labeler"], "idv-a")
    kiem("token moi: identity_verified = false, adult = false",
         claims(a).get("identity_verified") is False and claims(a).get("adult") is False, claims(a))
    r = requests.put(G + "/me/identity-verification", headers=a["h"], json={"fullName": "Nguyen Van A", "dateOfBirth": "2000-01-15", "idNumber": "12345"})
    kiem("so giay to sai → 400 so_giay_to_khong_hop_le", r.status_code == 400 and "so_giay_to_khong_hop_le" in r.text, r.text)
    r = requests.put(G + "/me/identity-verification", headers=a["h"], json={"fullName": "Nguyen Van A", "dateOfBirth": "2099-01-01", "idNumber": "001099012345"})
    kiem("ngay sinh tuong lai → 400", r.status_code == 400 and "ngay_sinh_khong_hop_le" in r.text, r.text)
    so_cccd = "0010990" + TAG[:5].translate(str.maketrans("abcdef", "123456"))
    r = requests.put(G + "/me/identity-verification", headers=a["h"], json={"fullName": "Nguyen Van A", "dateOfBirth": "2000-01-15", "idNumber": so_cccd})
    kiem("khai hop le → chi tra 4 so cuoi", r.status_code == 200 and r.json()["idNumberLast4"] == so_cccd[-4:] and so_cccd not in r.text, r.text)
    kiem("DB khong giu so giay to goc", sql("identity", f"select count(*) from identity_verifications where id_number_hash like '%{so_cccd}%' or full_name like '%{so_cccd}%'") == "0")
    r = requests.post(G + "/me/identity-verification/submit", headers=a["h"])
    kiem("nop khi chua co anh → 409 thieu_anh_giay_to", r.status_code == 409 and "thieu_anh_giay_to" in r.text, r.text)
    r = requests.post(G + "/me/identity-verification/upload-url", headers=a["h"], json={"side": "left", "fileName": "x.png", "sizeBytes": 10})
    kiem("side sai → 400", r.status_code == 400, r.text)
    r = requests.post(G + "/me/identity-verification/upload-url", headers=a["h"], json={"side": "front", "fileName": "x.pdf", "sizeBytes": 10})
    kiem("file .pdf → 400 (chi anh)", r.status_code == 400 and "file_khong_hop_le" in r.text, r.text)
    tai_anh(a, "front")
    tai_anh(a, "back")
    r = requests.post(G + "/me/identity-verification/submit", headers=a["h"])
    kiem("nop → submitted", r.status_code == 200 and r.json()["status"] == "submitted", r.text)
    kiem("dang cho duyet → sua 409", requests.put(G + "/me/identity-verification", headers=a["h"],
         json={"fullName": "X", "dateOfBirth": "2000-01-15", "idNumber": so_cccd}).status_code == 409)

    q = requests.get(G + "/users/admin/identity-verifications?status=submitted&pageSize=100", headers=admin).json()
    kiem("ho so nam trong hang doi admin", any(x["verification"]["userId"] == a["id"] for x in q["items"]), q)
    kiem("labeler khong xem duoc hang doi", requests.get(G + "/users/admin/identity-verifications", headers=a["h"]).status_code == 403)
    ct = requests.get(G + f"/users/admin/identity-verifications/{a['id']}", headers=admin).json()
    kiem("chi tiet co link xem anh + tuoi", ct["frontUrl"] and ct["backUrl"] and ct["age"] >= 25 and ct["duplicateOfUserId"] is None, ct)
    kiem("link xem anh tai duoc", requests.get(ct["frontUrl"]).content == ANH)
    r = requests.post(G + f"/users/admin/identity-verifications/{a['id']}/approve", headers=admin)
    kiem("admin duyet → verified, adult", r.status_code == 200 and r.json()["status"] == "verified" and r.json()["adult"] is True, r.text)
    lam_moi_token(a)
    kiem("lam moi token → identity_verified = true, adult = true",
         claims(a).get("identity_verified") is True and claims(a).get("adult") is True, claims(a))

    print("\n[3] Chua du tuoi; trung giay to (VD-T-04)")
    b = dang_ky(["labeler"], "idv-b")
    requests.put(G + "/me/identity-verification", headers=b["h"], json={"fullName": "Tran B", "dateOfBirth": "2012-06-01", "idNumber": "C" + TAG.upper() + "9"}).raise_for_status()
    tai_anh(b, "front")
    tai_anh(b, "back")
    requests.post(G + "/me/identity-verification/submit", headers=b["h"]).raise_for_status()
    requests.post(G + f"/users/admin/identity-verifications/{b['id']}/approve", headers=admin).raise_for_status()
    lam_moi_token(b)
    kiem("da xac minh nhung 14 tuoi → identity_verified = true, adult = false",
         claims(b).get("identity_verified") is True and claims(b).get("adult") is False, claims(b))

    c = dang_ky(["labeler"], "idv-c")
    requests.put(G + "/me/identity-verification", headers=c["h"], json={"fullName": "Nguyen Van A", "dateOfBirth": "2000-01-15", "idNumber": so_cccd}).raise_for_status()
    tai_anh(c, "front")
    tai_anh(c, "back")
    requests.post(G + "/me/identity-verification/submit", headers=c["h"]).raise_for_status()
    ct = requests.get(G + f"/users/admin/identity-verifications/{c['id']}", headers=admin).json()
    kiem("admin thay ho so trung giay to voi tai khoan khac", ct["duplicateOfUserId"] == a["id"], ct)
    r = requests.post(G + f"/users/admin/identity-verifications/{c['id']}/approve", headers=admin)
    kiem("duyet ho so trung → 409 giay_to_da_dung", r.status_code == 409 and "giay_to_da_dung" in r.text, r.text)
    r = requests.post(G + f"/users/admin/identity-verifications/{c['id']}/reject", headers=admin, json={"reason": "Giay to da dung cho tai khoan khac"})
    kiem("tu choi kem ly do → rejected", r.status_code == 200 and r.json()["status"] == "rejected", r.text)

    # =================================================================================
    print("\n[4] Gioi han tan suat dung chung qua Redis (VD-S-12)")
    ten = f"{TIEN_TO_CONTAINER}redis-gateway"
    out = subprocess.run(["docker", "exec", ten, "redis-cli", "--scan", "--pattern", "rl:*"], capture_output=True, text=True).stdout
    kiem("gateway dem trong Redis (co khoa rl:*)", "rl:chung:" in out and "rl:auth:" in out, out[:300])
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
