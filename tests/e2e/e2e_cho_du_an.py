# E2E CHO DU AN (GET /projects/market) — chi hien du an labeler LAM DUOC NGAY:
#   chua du cap do / uy tin → an; da nop → an (van hien cho nguoi khac); du nguoi lam → an voi nguoi ngoai, van hien
#   voi nguoi dang giu bai; qua han chot → an ngay (khong cho worker dong du an).
#   Goi y (recommended) cung bo loc. Mo bai bi tu choi → cau bao loi noi ro ly do (khong con cau chung chung).
# Chay: python e2e_cho_du_an.py   (stack demo: E2E_PROFILE=demo)
import os, sys
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402


def cho_du_an(u):
    r = requests.get(G + "/projects/market", headers=u["h"], params={"pageSize": 100})
    assert r.status_code == 200, r.text
    return {p["id"] for p in r.json()["items"]}


def goi_y(u):
    return {p["id"] for p in requests.get(G + "/projects/recommended", headers=u["h"], params={"limit": 50}).json()}


try:
    biz = dang_ky(["business"], "cho")
    h.nap(biz, 3000000)
    L = [dang_ky(["labeler"], f"cho-l{i}") for i in range(4)]
    mau = {f"c{i}": f"cau {i} {TAG}" for i in range(2)}

    P_OK, _ = tao_du_an(biz, "CHO-OK", mau, suat=3)
    P_DAY, _ = tao_du_an(biz, "CHO-DAY", mau, suat=3)
    P_HAN, _ = tao_du_an(biz, "CHO-HAN", mau, suat=3)
    P_CAP, _ = tao_du_an(biz, "CHO-CAP", mau, suat=3, dieu_kien={"minLevel": 3, "minReputation": None,
                         "requireEntranceTest": False, "entranceQuestionCount": 10, "entrancePassPercent": 80})
    P_UT, _ = tao_du_an(biz, "CHO-UYTIN", mau, suat=3, dieu_kien={"minLevel": None, "minReputation": 50,
                        "requireEntranceTest": False, "entranceQuestionCount": 10, "entrancePassPercent": 80})
    for p in (P_OK, P_DAY, P_HAN, P_CAP, P_UT):
        publish_va_duyet(biz, p)

    # =================================================================================
    print("\n[1] Dieu kien ho so: labeler moi (cap 1, chua co uy tin)")
    c0 = cho_du_an(L[0])
    kiem("thay du an khong dieu kien", {P_OK, P_DAY, P_HAN} <= c0, c0)
    kiem("an du an can cap 3", P_CAP not in c0)
    kiem("an du an can uy tin 50 (chua co diem)", P_UT not in c0)
    cu = {p["id"] for p in requests.get(G + "/projects", headers=L[0]["h"], params={"status": "running", "pageSize": 100}).json()["items"]}
    kiem("GET /projects van liet ke ca du an khong du dieu kien (danh sach xem duoc, khong phai cho)", P_CAP in cu)
    g0 = goi_y(L[0])
    kiem("goi y cung khong co du an khong du dieu kien", P_CAP not in g0 and P_UT not in g0, g0)

    # =================================================================================
    print("\n[2] Cau bao loi noi ro ly do")
    r = requests.post(G + f"/tasks/projects/{P_OK}/attempt", headers=L[0]["h"])
    kiem("chua tham gia → 403 khong_phai_thanh_vien, cau bao noi phai tham gia",
         r.status_code == 403 and r.json().get("code") == "khong_phai_thanh_vien" and "thành viên" in r.json().get("detail", ""), r.text)

    # =================================================================================
    print("\n[3] Da nop → an voi minh, van hien voi nguoi khac")
    tham_gia(L[0], P_OK)
    lam_het(L[0], P_OK, lambda _t: "a")
    kiem("nguoi da nop khong con thay du an", P_OK not in cho_du_an(L[0]))
    kiem("nguoi chua lam van thay", P_OK in cho_du_an(L[1]))

    # =================================================================================
    print("\n[4] Du nguoi lam (3 bai dang giu) → an voi nguoi ngoai, nguoi dang giu van lam tiep duoc")
    for u in L[:3]:
        tham_gia(u, P_DAY)
        kiem(f"{u['email'].split('-')[-1].split('@')[0]} giu mot bai", isinstance(bat_dau(u, P_DAY), dict))
    kiem("nguoi thu 4 khong thay du an da du nguoi", P_DAY not in cho_du_an(L[3]))
    kiem("nguoi dang giu bai van thay (lam tiep)", P_DAY in cho_du_an(L[1]))

    # =================================================================================
    print("\n[5] Qua han chot → an ngay")
    tham_gia(L[1], P_HAN)
    kiem("truoc han: thanh vien thay du an", P_HAN in cho_du_an(L[1]))
    sql("project", f"update projects set deadline = now() - interval '1 minute' where id = '{P_HAN}'")
    kiem("du an qua han chot khong con trong cho", P_HAN not in cho_du_an(L[1]))
    kiem("goi y cung an", P_HAN not in goi_y(L[2]))
    r =requests.post(G + f"/tasks/projects/{P_HAN}/attempt", headers=L[1]["h"])
    kiem("mo bai du an qua han → cau bao ve han chot / ngung nhan bai (khong chung chung)",
         r.status_code in (403, 409) and "chưa đủ điều kiện làm bài" not in r.json().get("detail", ""), r.text)

    # Labeler (khong phai business) moi goi duoc cho du an.
    kiem("business goi /projects/market → 403", requests.get(G + "/projects/market", headers=biz["h"]).status_code == 403)
finally:
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
