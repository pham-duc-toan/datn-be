# E2E — co "can duyet tung nhan" cua du an (requireReview) + loc mau theo ids (ten file goc cho giao dien).
#   requireReview = false: nhan khop dong thuan → he thong tu duyet + tra tien; nhan lech da so / mau tranh
#   chap van cho chu du an; du an chi co cau tra loi mo (notApplicable) → tu duyet.
#   Mac dinh (true): nhu cu, cho duyet tung nhan.
import os
import sys
import uuid

import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402

PHAN_LOAI = {"modality": "text", "tools": [{"name": "nhan", "kind": "classification", "classes": ["a", "b"]}]}


def du_an(ten, tap_nhan, rows, red, tran, can_duyet):
    pid, _, mau = h.du_an(chu, ten, "text", tap_nhan, rows=rows, red=red, tran=tran)
    if can_duyet is not None:
        r = requests.put(G + f"/projects/{pid}/quality-control", headers=chu["h"], json={"goldCheckPercent": 0, "requireReview": can_duyet})
        assert r.status_code == 200 and r.json()["requireReview"] is can_duyet, r.text
    publish_va_duyet(chu, pid)
    return pid, mau


def lam(u, pid, payload):
    tham_gia(u, pid)
    t = nhan_task(u, pid)
    assert t is not None, "khong nhan duoc task"
    r = requests.post(G + f"/tasks/assignments/{t['assignmentId']}/submit", headers=u["h"], json={"payload": payload})
    assert r.status_code == 200, r.text
    return t


def nhan_cua(pid, uid):
    return [a for a in ds_nhan(chu, pid) if a["labelerId"] == uid]


def trang_thai(pid, uid, cho_khac="pendingReview", giay=30):
    """Cho nhan cua uid roi khoi trang thai ban dau (hoac het gio) → tra trang thai cuoi."""
    v = cho(lambda: (lambda x: x[0]["status"] if x and x[0]["status"] != cho_khac else None)(nhan_cua(pid, uid)), giay)
    return v or (nhan_cua(pid, uid) or [{"status": None}])[0]["status"]


chu = dang_ky(["business"], "td-chu")
h.nap(chu, 3000000)
lab = [dang_ky(["labeler"], f"td-lab{i}") for i in range(3)]

print("\n[1] Mac dinh: du an moi CAN duyet tung nhan")
r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-td] mac dinh {TAG}", "description": "e2e", "modality": "text", "visibility": "public"})
kiem("du an moi: requireReview = true", r.status_code in (200, 201) and r.json()["requireReview"] is True, r.text)

print("\n[2] Khong can duyet, 1 nguoi gan / mau: moi nhan duoc tu duyet + tra tien")
A, mauA = du_an("tu duyet", PHAN_LOAI, [{"text": "mot", "name": "anh-mot.jpg"}], 1, 1, False)
truoc = vi(lab[0])["pendingVnd"]
lam(lab[0], A, {"nhan": {"labelIds": ["a"]}})
kiem("nhan tu duyet (approved) khong can chu du an", trang_thai(A, lab[0]["id"]) == "approved", nhan_cua(A, lab[0]["id"]))
kiem("labeler duoc tra tien (pendingVnd tang)", cho(lambda: vi(lab[0])["pendingVnd"] > truoc, 20), (truoc, vi(lab[0])))
r = requests.put(G + f"/projects/{A}/quality-control", headers=chu["h"], json={"goldCheckPercent": 0, "requireReview": True})
kiem("doi co sau publish → bi chan (dieu khoan da chot)", r.status_code in (400, 409), r.text)

print("\n[3] Khong can duyet, 3 nguoi: nhan khop da so tu duyet, nhan LECH van cho chu du an")
C, _ = du_an("lech da so", PHAN_LOAI, [{"text": "hai", "name": "anh-hai.jpg"}], 3, 3, False)
for u, lop in zip(lab, ["a", "a", "b"]):
    lam(u, C, {"nhan": {"labelIds": [lop]}})
kiem("2 nhan khop da so → approved", trang_thai(C, lab[0]["id"]) == "approved" and trang_thai(C, lab[1]["id"]) == "approved", ds_nhan(chu, C))
lech = cho(lambda: (lambda x: x if x and x[0]["consensusAgrees"] is False else None)(nhan_cua(C, lab[2]["id"])), 20)
kiem("nhan lech da so: van pendingReview (consensusAgrees = false)", lech is not None and lech[0]["status"] == "pendingReview", nhan_cua(C, lab[2]["id"]))

print("\n[4] Khong can duyet, mau tranh chap (1 - 1, het tran redundancy) → ca hai cho chu du an")
B, _ = du_an("tranh chap", PHAN_LOAI, [{"text": "ba", "name": "anh-ba.jpg"}], 2, 2, False)
lam(lab[0], B, {"nhan": {"labelIds": ["a"]}})
lam(lab[1], B, {"nhan": {"labelIds": ["b"]}})
# Mau tranh chap: consensusAgrees de trong (khong ai "khop"), trang thai dong thuan cua task = disputed.
tc = cho(lambda: sql("annotation", f"select status from task_consensus where project_id='{B}'") == "disputed", 30)
time.sleep(2)
kiem("tranh chap: dong thuan disputed, khong nhan nao tu duyet",
     tc and len(ds_nhan(chu, B)) == 2 and all(a["status"] == "pendingReview" for a in ds_nhan(chu, B)), (sql("annotation", f"select status from task_consensus where project_id='{B}'"), ds_nhan(chu, B)))

print("\n[5] Khong can duyet, chi cau tra loi mo (dong thuan notApplicable) → tu duyet")
D, _ = du_an("cau tra loi mo", {"modality": "text", "tools": [{"name": "y_kien", "kind": "freeText"}]}, [{"text": "bon", "name": "y-kien.txt"}], 1, 1, False)
lam(lab[0], D, {"y_kien": {"text": "Rat tot"}})
kiem("cau tra loi mo → approved", trang_thai(D, lab[0]["id"]) == "approved", nhan_cua(D, lab[0]["id"]))

print("\n[6] Can duyet (mac dinh): nhan van cho duyet nhu cu")
E, _ = du_an("can duyet", PHAN_LOAI, [{"text": "nam", "name": "anh-nam.jpg"}], 1, 1, None)
lam(lab[0], E, {"nhan": {"labelIds": ["a"]}})
cho(lambda: (lambda x: x and x[0]["consensusAgrees"] is not None)(nhan_cua(E, lab[0]["id"])), 20)
kiem("du an can duyet: nhan khop dong thuan van pendingReview", nhan_cua(E, lab[0]["id"])[0]["status"] == "pendingReview", nhan_cua(E, lab[0]["id"]))

print("\n[7] GET /projects/{id}/samples?ids= — ten file goc cho trang ket qua / duyet")
sid = mauA["anh-mot.jpg"]["id"]
r = requests.get(G + f"/projects/{A}/samples", headers=chu["h"], params={"ids": sid})
kiem("loc theo ids → dung 1 mau, co originalName", r.status_code == 200 and [x["originalName"] for x in r.json()["items"]] == ["anh-mot.jpg"], r.text)
r = requests.get(G + f"/projects/{A}/samples", headers=chu["h"], params={"ids": "khong-phai-guid"})
kiem("ids sai dinh dang → 400", r.status_code == 400 and r.json().get("code") == "ma_mau_khong_hop_le", r.text)
r = requests.get(G + f"/projects/{A}/samples", headers=chu["h"], params={"ids": ",".join(str(uuid.uuid4()) for _ in range(101))})
kiem("qua 100 ids → 400", r.status_code == 400 and r.json().get("code") == "qua_nhieu_mau", r.text)
r = requests.get(G + f"/projects/{A}/samples", headers=lab[0]["h"], params={"ids": sid})
kiem("labeler khong xem duoc ten file / file goc → 403", r.status_code == 403, r.text)

print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
sys.exit(1 if KQ["fail"] else 0)
