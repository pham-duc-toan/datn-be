"""Cho he thong san sang chay E2E: gateway len, identity cap token, seed xong.

Dung sau `docker compose -f docker-compose.demo.yml up -d` (CI va may dev). Thoat 1 neu qua han.
    python cho_san_sang.py [so_giay_toi_da]
"""

import os
import sys
import time

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from moi_truong import G, PW  # noqa: E402
from tai_khoan_e2e import mo_rong_gioi_han  # noqa: E402

HAN = int(sys.argv[1]) if len(sys.argv) > 1 else 600


TOKEN: dict[str, dict] = {}


def dang_nhap(email: str) -> dict | None:
    """Dang nhap MOT lan roi nho token: gateway gioi han 20 lan dang nhap / phut / IP (truoc khi E2E
    nang han muc) — hoi lai moi 5 giay ma dang nhap lai moi lan thi tu chan minh."""
    if email in TOKEN:
        return TOKEN[email]
    try:
        r = requests.post(G + "/auth/login", json={"email": email, "password": PW}, timeout=5)
        if r.status_code == 200:
            TOKEN[email] = {"Authorization": "Bearer " + r.json()["accessToken"]}
            return TOKEN[email]
    except requests.RequestException:
        pass
    return None


def kiem() -> list[str]:
    """Danh sach dieu kien CHUA dat (rong = san sang)."""
    chua = []
    biz = dang_nhap("doanhnghiep1@crowd.local")
    if biz is None or dang_nhap("admin@crowd.local") is None:
        return ["dang nhap (identity + seed tai khoan)"]
    try:
        # Duyet het cac trang: may dev da chay E2E nhieu lan co hang tram du an, du an seed o trang sau.
        ds = []
        for trang in range(1, 20):
            mot = requests.get(G + f"/projects?mine=true&pageSize=100&page={trang}", headers=biz, timeout=5).json()["items"]
            ds.extend(mot)
            if len(mot) < 100:
                break
        if sum(1 for p in ds if p["name"].startswith("[Seed]")) < 4:
            chua.append("du an seed (project-svc)")
        lab = dang_nhap("labeler1@crowd.local")
        if lab is None or requests.get(G + "/quality/me", headers=lab, timeout=5).status_code != 200:
            chua.append("quality-svc")
        if requests.get(G + "/admin/settings", headers=dang_nhap("admin@crowd.local"), timeout=5).status_code != 200:
            chua.append("admin-svc")
        if requests.get(G + "/ledger/me/balance", headers=biz, timeout=5).status_code != 200:
            chua.append("ledger-svc")
        if requests.get(G + "/notifications/me/unread-count", headers=biz, timeout=5).status_code != 200:
            chua.append("notification-svc")
        kyb = requests.get(G + "/kyb/me", headers=biz, timeout=5)
        if kyb.status_code != 200 or kyb.json().get("status") != "verified":
            chua.append("KYB doanh nghiep seed (identity-svc)")
    except (requests.RequestException, KeyError, ValueError) as ex:
        chua.append(f"loi goi API: {ex}")
    return chua


bat_dau = time.time()
cuoi = None
while time.time() - bat_dau < HAN:
    chua = kiem()
    if not chua:
        # Them vai giay de cac event seed (dataset.ingested → task, project.published → quality...) di het.
        time.sleep(15)
        # Moi truong chay E2E (CI / demo): nang gioi han tan suat cua gateway — E2E dang nhap hang tram lan tu mot IP.
        mo_rong_gioi_han(dang_nhap("admin@crowd.local"))
        print(f"san sang sau {time.time() - bat_dau:.0f} giay", flush=True)
        sys.exit(0)
    if chua != cuoi:
        print(f"[{time.time() - bat_dau:4.0f}s] chua san sang: {', '.join(chua)}", flush=True)
        cuoi = chua
    time.sleep(5)

print(f"QUA HAN {HAN} giay, con: {', '.join(kiem())}", flush=True)
sys.exit(1)
