"""Tien ich tai khoan dung chung cho cac bo E2E.

- Tai khoan dang ky moi CHUA xac thuc email → chua rut tien, chua publish duoc (FC-01). E2E bam link
  xac thuc THAT: doc email trong Mailpit, lay token trong link, goi /auth/verify-email.
- Gateway gioi han tan suat (dang nhap 20 lan/phut/IP...). E2E chay tu MOT IP voi hang tram lan
  dang ky / dang nhap → nang han muc khi chay test (setting gateway.rate_limit_*).
"""

import re
import subprocess
import time

import requests

from moi_truong import G, MAILPIT, sql_container

TIEU_DE_XAC_THUC = "Xác thực email Crowd Label"
TIEU_DE_DAT_LAI = "Đặt lại mật khẩu Crowd Label"

# Han muc rong khi chay E2E; cho_san_sang / chay_hoi_quy dat, xong thi tra mac dinh.
HAN_MUC_E2E = {
    "gateway.rate_limit_per_minute": 1000000,
    "gateway.rate_limit_auth_per_minute": 1000000,
    "gateway.rate_limit_money_per_minute": 1000000,
}


def _sql(db, q):
    return subprocess.run(["docker", "exec", sql_container(db), "psql", "-U", f"{db}_user", "-d", f"{db}_db", "-tAc", q],
                          capture_output=True, text=True).stdout.strip()


def _cho(f, giay=30, buoc=0.5):
    het = time.time() + giay
    while time.time() < het:
        v = f()
        if v:
            return v
        time.sleep(buoc)
    return None


def thu_moi_nhat(email, tieu_de, giay=45):
    """Noi dung (text) email moi nhat co tieu de nay gui toi email; None neu het gio."""
    def tim():
        r = requests.get(f"{MAILPIT}/api/v1/search", params={"query": f'to:"{email}" subject:"{tieu_de}"', "limit": 5})
        ds = [m for m in r.json().get("messages", []) if m.get("Subject") == tieu_de]
        if not ds:
            return None
        return requests.get(f"{MAILPIT}/api/v1/message/{ds[0]['ID']}").json().get("Text", "")
    return _cho(tim, giay)


def token_trong_email(email, tieu_de, giay=45):
    noi_dung = thu_moi_nhat(email, tieu_de, giay)
    if noi_dung is None:
        return None
    m = re.search(r"token=([A-Za-z0-9_\-%]+)", noi_dung)
    return None if m is None else requests.utils.unquote(m.group(1))


def xac_thuc_email(email, cho_ban_sao=True):
    """Bam link xac thuc trong email. cho_ban_sao: doi ledger + project nhan user.email_verified."""
    token = token_trong_email(email, TIEU_DE_XAC_THUC)
    assert token, f"khong thay email xac thuc cua {email}"
    r = requests.post(G + "/auth/verify-email", json={"token": token})
    assert r.status_code == 200, r.text
    if cho_ban_sao:
        uid = _sql("identity", f"select id from users where email=lower('{email}')")  # identity luu email chu thuong
        for db in ("ledger", "project"):
            ok = _cho(lambda: _sql(db, f"select count(*) from email_status where user_id='{uid}' and verified") == "1", 30)
            assert ok, f"{db} chua nhan user.email_verified cua {email}"


def mo_rong_gioi_han(admin_h):
    """Nang han muc gateway cho E2E. Tra {key: gia tri cu} de tra lai sau."""
    cu = {}
    for key, v in HAN_MUC_E2E.items():
        cu[key] = requests.get(G + f"/admin/settings/{key}", headers=admin_h).json()["value"]
        if cu[key] != v:
            requests.put(G + f"/admin/settings/{key}", headers=admin_h, json={"value": v, "reason": "e2e: mo rong han muc"}).raise_for_status()
    time.sleep(1)  # gateway nhan setting.changed
    return cu


def tra_gioi_han(admin_h, cu):
    for key, v in cu.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin_h, json={"value": v, "reason": "e2e: tra han muc"})
