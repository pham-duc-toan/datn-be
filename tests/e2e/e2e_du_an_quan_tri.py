# E2E QUAN LY DU AN CHO DOANH NGHIEP VA ADMIN (bo sung):
#   - xoa han du an nhap (DELETE /projects/{id}); du an da publish khong xoa duoc;
#   - don gia nhan khach vang lai (cong link) do ADMIN dat bang setting gate.unit_price_vnd: doanh nghiep gui len
#     bi bo qua, chi duoc thong bao (readiness.gateUnitPriceVnd), chot lai luc publish;
#   - admin xem duoc moi thong tin cua du an dang cho duyet (thong tin, readiness, mau + tien trinh nap, cau vang, thanh vien).
# Chay: python e2e_du_an_quan_tri.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from e2e_cong_cu_helpers import nap  # noqa: E402

try:
    biz = dang_ky(["business"], "qt-xoa")
    other = dang_ky(["business"], "qt-khac")

    # =================================================================================
    print("\n[1] Xoa du an nhap")
    pid, mau = tao_du_an(biz, "Xoa duoc", {"a": "mot", "b": "hai", "c": "ba"}, 1, 1, 1000)
    kiem("du an nhap co 3 mau", sql("project", f"select count(*) from samples where project_id = '{pid}'") == "3")
    r = requests.delete(G + f"/projects/{pid}", headers=other["h"])
    kiem("nguoi khac khong xoa duoc du an cua minh → 403 / 404", r.status_code in (403, 404), r.text)
    r = requests.delete(G + f"/projects/{pid}", headers=biz["h"])
    kiem("chu xoa du an nhap → 204", r.status_code == 204, r.text)
    kiem("du an khong con (GET 404)", requests.get(G + f"/projects/{pid}", headers=biz["h"]).status_code == 404)
    kiem("mau, cau hinh di theo (CASCADE): khong con dong nao",
         sql("project", f"select (select count(*) from samples where project_id = '{pid}') + (select count(*) from projects where id = '{pid}')") == "0")
    kiem("xoa lan 2 → 404", requests.delete(G + f"/projects/{pid}", headers=biz["h"]).status_code == 404)

    nap(biz, 600000)
    P, _ = tao_du_an(biz, "Da publish", {"a": "mot", "b": "hai", "c": "ba"}, 1, 1, 1000)
    requests.post(G + f"/projects/{P}/publish", headers=biz["h"]).raise_for_status()
    cho(lambda: requests.get(G + f"/projects/{P}", headers=biz["h"]).json()["status"] == "pendingApproval", 25)
    r = requests.delete(G + f"/projects/{P}", headers=biz["h"])
    kiem("du an da publish (co ky quy) → 409 khong_the_xoa_du_an", r.status_code == 409 and r.json().get("code") == "khong_the_xoa_du_an", r.text)

    # =================================================================================
    print("\n[2] Admin xem day du du an cho duyet")
    kiem("du an dang cho duyet", requests.get(G + f"/projects/{P}", headers=admin).json()["status"] == "pendingApproval")
    d = requests.get(G + f"/projects/{P}", headers=admin)
    j = d.json()
    kiem("admin doc duoc thong tin day du (chu, ky quy, phi, tap nhan, huong dan, hach toan)",
         d.status_code == 200 and j["ownerId"] == biz["id"] and j["escrowVnd"] and j["platformFeePercent"] is not None and j["labelSchema"] and j["guideline"], d.text[:300])
    kiem("admin doc readiness", requests.get(G + f"/projects/{P}/readiness", headers=admin).status_code == 200)
    st = requests.get(G + f"/projects/{P}/samples/status", headers=admin)
    kiem("admin doc tien trinh nap mau: 3 san sang", st.status_code == 200 and st.json()["ready"] == 3, st.text)
    sm = requests.get(G + f"/projects/{P}/samples?pageSize=20", headers=admin)
    kiem("admin xem mau (ban goc) co noi dung", sm.status_code == 200 and sm.json()["total"] == 3 and all(m["content"] for m in sm.json()["items"]), sm.text[:200])
    kiem("admin doc danh sach cau vang", requests.get(G + f"/projects/{P}/gold-items", headers=admin).status_code == 200)
    kiem("admin doc thanh vien", requests.get(G + f"/projects/{P}/members", headers=admin).status_code == 200)
    kiem("nguoi khong phai chu, khong phai admin khong doc duoc mau (404)", requests.get(G + f"/projects/{P}/samples", headers=other["h"]).status_code in (403, 404))
    requests.post(G + f"/projects/{P}/reject", headers=admin, json={"reason": "e2e"})

    # =================================================================================
    print("\n[3] Don gia nhan khach vang lai do admin dat — doanh nghiep khong dat duoc")
    dat_va_cho("gate.unit_price_vnd", 1500, ["project"])
    Q, _ = tao_du_an(biz, "Gia cong", {"a": "mot", "b": "hai", "c": "ba"}, 1, 1, 1000)
    rd = requests.get(G + f"/projects/{Q}/readiness", headers=biz["h"]).json()
    kiem("readiness thong bao don gia cong link hien hanh 1.500", rd["gateUnitPriceVnd"] == 1500, rd)
    r = requests.patch(G + f"/projects/{Q}", headers=biz["h"], json={"channels": {"allowProfessional": True, "allowLinkGateway": True, "allowCollaborative": False, "gateUnitPriceVnd": 99999, "gateBudgetVnd": 20000}})
    kiem("doanh nghiep gui gateUnitPriceVnd = 99.999 → bi bo qua, du an lay 1.500 cua admin", r.status_code == 200 and r.json()["gateUnitPriceVnd"] == 1500 and r.json()["gateBudgetVnd"] == 20000, r.text)
    kiem("doanh nghiep khong doi duoc setting (403)", requests.put(G + "/admin/settings/gate.unit_price_vnd", headers=biz["h"], json={"value": 1, "reason": "x"}).status_code == 403)
    dat_va_cho("gate.unit_price_vnd", 2500, ["project"])
    nap(biz, 600000)
    requests.patch(G + f"/projects/{Q}", headers=biz["h"], json={"guideline": {"markdown": "e2e", "examples": []}}).raise_for_status()
    # cong link can cau vang kiem tra: them 2 cau vang qualityCheck
    ids = {m["originalName"]: m["id"] for m in requests.get(G + f"/projects/{Q}/samples?pageSize=50", headers=biz["h"]).json()["items"]}
    rv = requests.post(G + f"/projects/{Q}/gold-items", headers=biz["h"], json={"items": [
        {"sampleId": ids["a"], "purpose": "qualityCheck", "expectedPayload": {"nhan": {"labelIds": ["a"]}}},
        {"sampleId": ids["b"], "purpose": "qualityCheck", "expectedPayload": {"nhan": {"labelIds": ["b"]}}}]})
    r = requests.post(G + f"/projects/{Q}/publish", headers=biz["h"])
    kiem("publish → gia cong link chot lai theo setting hien hanh 2.500", r.status_code in (200, 202) and requests.get(G + f"/projects/{Q}", headers=biz["h"]).json()["gateUnitPriceVnd"] == 2500, r.text)
    dat_va_cho("gate.unit_price_vnd", 1000, ["project"])
    kiem("admin doi lai sau publish: du an da publish giu muc da chot 2.500", requests.get(G + f"/projects/{Q}", headers=biz["h"]).json()["gateUnitPriceVnd"] == 2500)
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
