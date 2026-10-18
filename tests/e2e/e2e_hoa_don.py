# E2E HOA DON GTGT (FB-05): tinh phi nen tang cua thang tu so cai, xuat hoa don (so tang dan, chup ho so KYB),
# tam tinh, HTML / XML, phan quyen, bat bien o tang database.
# Chay: python e2e_hoa_don.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time, uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta, timezone
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403

VN = timezone(timedelta(hours=7))
THANG_NAY = datetime.now(VN).strftime("%Y-%m")
dau = datetime.now(VN).replace(day=1)
THANG_TRUOC = (dau - timedelta(days=1)).strftime("%Y-%m")

try:
    print("\n[1] Tao phi nen tang: nap tien, du an 3 cau, thuong 60.000d / suat (phi 30%), labeler lam bai, ket thuc som → cham → tra thuong")
    biz = dang_ky(["business"], "hoadon")
    lab = dang_ky(["labeler"], "hoadon-lab")
    intent = requests.post(G + "/payments/deposits", headers={**biz["h"], "Idempotency-Key": uuid.uuid4().hex},
                           json={"amountVnd": 1000000}).json()["intentId"]
    requests.post(G + f"/payments/sandbox/checkout/{intent}/pay?amount=1000000").raise_for_status()
    cho(lambda: vi(biz)["businessAvailableVnd"] == 1000000, 20)
    pid, _ = tao_du_an(biz, "Hoa don", {"h1": "mot", "h2": "hai", "h3": "ba"}, 1, 1, 20000)
    ten_du_an = requests.get(G + f"/projects/{pid}", headers=biz["h"]).json()["name"]
    publish_va_duyet(biz, pid)
    tham_gia(lab, pid)
    kiem("labeler lam ca bai 3 cau", lam_het(lab, pid, lambda t: "a") == 3)
    ket_thuc_som(biz, pid)
    kiem("bai dat → ledger tra thuong", cho(lambda: requests.get(G + f"/ledger/projects/{pid}/cost", headers=biz["h"]).json().get("paidAttempts") == 1, 60))
    time.sleep(2)

    print("\n[2] Tam tinh thang nay (chua xuat)")
    r = requests.get(G + f"/ledger/invoices/preview?period={THANG_NAY}", headers=biz["h"])
    j = r.json()
    kiem("tam tinh: phi 30% x 60.000 = 18.000 (da gom VAT)", r.status_code == 200 and j["totalVnd"] == 18000, j)
    kiem("tach VAT 10%: 16.364 + 1.636", j["totalNetVnd"] == 16364 and j["totalVatVnd"] == 1636, j)
    kiem("bang ke chi ho labeler 60.000", j["passThroughVnd"] == 60000, j)
    kiem("so luot tinh phi = 1 bai dat, dong ghi ten du an", j["items"][0]["quantity"] == 1 and ten_du_an in j["items"][0]["description"], j)
    kiem("chua xuat", j["issued"] is False)

    print("\n[3] Xuat hoa don")
    r = requests.post(G + "/ledger/invoices", headers=biz["h"], json={"period": THANG_NAY})
    kiem("thang dang chay → 409 ky_chua_ket_thuc", r.status_code == 409 and "ky_chua_ket_thuc" in r.text, r.text)
    r = requests.post(G + "/ledger/invoices", headers=biz["h"], json={"period": THANG_TRUOC})
    kiem("thang truoc khong co phi → 409 khong_co_phi", r.status_code == 409 and "khong_co_phi" in r.text, r.text)
    r = requests.post(G + "/ledger/invoices", headers=biz["h"], json={"period": "2026/09"})
    kiem("ky sai dinh dang → 400", r.status_code == 400, r.text)
    kiem("labeler khong xuat duoc → 403", requests.post(G + "/ledger/invoices", headers=lab["h"], json={"period": THANG_TRUOC}).status_code == 403)

    dat_va_cho("invoice.allow_open_period", True, ["ledger"])
    r = requests.post(G + "/ledger/invoices", headers=biz["h"], json={"period": THANG_NAY})
    h = r.json()
    kiem("xuat (setting kiem thu cho thang dang chay) → 200", r.status_code == 200, r.text)
    kiem("so lieu khop tam tinh", h["totalVnd"] == 18000 and h["totalNetVnd"] == 16364 and h["totalVatVnd"] == 1636, h)
    kiem("chu so tien: Mười tám nghìn đồng", h["amountInWords"] == "Mười tám nghìn đồng", h["amountInWords"])
    kiem("nguoi mua = ho so KYB (ten, MST)", h["buyerName"] == "Cong ty E2E" and h["buyerTaxCode"] == "0312345678", h)
    yy = THANG_NAY[2:4]
    kiem(f"ky hieu 1C{yy}TDN, so 7 chu so", h["serial"] == f"1C{yy}TDN" and len(h["number"]) == 7, h)
    r2 = requests.post(G + "/ledger/invoices", headers=biz["h"], json={"period": THANG_NAY})
    kiem("xuat lai cung ky → tra lai DUNG hoa don cu (idempotent)", r2.json()["invoiceId"] == h["invoiceId"] and r2.json()["number"] == h["number"], r2.text)
    kiem("tam tinh bao issued = true", requests.get(G + f"/ledger/invoices/preview?period={THANG_NAY}", headers=biz["h"]).json()["issued"] is True)

    biz2 = dang_ky(["business"], "hoadon2")
    intent = requests.post(G + "/payments/deposits", headers={**biz2["h"], "Idempotency-Key": uuid.uuid4().hex}, json={"amountVnd": 500000}).json()["intentId"]
    requests.post(G + f"/payments/sandbox/checkout/{intent}/pay?amount=500000").raise_for_status()
    cho(lambda: vi(biz2)["businessAvailableVnd"] == 500000, 20)
    pid2, _ = tao_du_an(biz2, "Hoa don 2", {"k1": "mot"}, 1, 1, 10000)
    publish_va_duyet(biz2, pid2)
    tham_gia(lab, pid2)
    lam_het(lab, pid2, lambda t: "b")
    ket_thuc_som(biz2, pid2)
    cho(lambda: requests.get(G + f"/ledger/projects/{pid2}/cost", headers=biz2["h"]).json().get("paidAttempts") == 1, 60)
    time.sleep(2)
    h2 = requests.post(G + "/ledger/invoices", headers=biz2["h"], json={"period": THANG_NAY}).json()
    kiem("hoa don doanh nghiep khac: so ke tiep (tang dan)", int(h2["number"]) > int(h["number"]) and h2["totalVnd"] == 3000, h2)

    print("\n[4] Xem / tai: HTML, XML; phan quyen")
    ds_hd = requests.get(G + "/ledger/invoices", headers=biz["h"]).json()
    kiem("danh sach cua doanh nghiep co hoa don", any(x["invoiceId"] == h["invoiceId"] for x in ds_hd), ds_hd)
    r = requests.get(G + f"/ledger/invoices/{h['invoiceId']}/html", headers=biz["h"])
    kiem("HTML in duoc: tieu de + ten cong ty + so tien bang chu",
         r.status_code == 200 and "HÓA ĐƠN GIÁ TRỊ GIA TĂNG" in r.text and "Cong ty E2E" in r.text and "Mười tám nghìn đồng" in r.text, r.status_code)
    r = requests.get(G + f"/ledger/invoices/{h['invoiceId']}/xml", headers=biz["h"])
    ok = r.status_code == 200
    if ok:
        x = ET.fromstring(r.content)
        ok = x.find("DLHDon/NDHDon/TToan/TgTTTBSo").text == "18000" and x.find("DLHDon/NDHDon/NMua/MST").text == "0312345678"
    kiem("XML (TT78): TgTTTBSo = 18000, MST nguoi mua", ok, r.text[:300])
    kiem("doanh nghiep khac xem → 404 (BOLA)", requests.get(G + f"/ledger/invoices/{h['invoiceId']}", headers=biz2["h"]).status_code == 404)
    kiem("labeler xem → 403", requests.get(G + f"/ledger/invoices/{h['invoiceId']}", headers=lab["h"]).status_code == 403)
    kiem("admin xem duoc", requests.get(G + f"/ledger/invoices/{h['invoiceId']}", headers=admin).status_code == 200)
    a = requests.get(G + f"/ledger/invoices/admin?period={THANG_NAY}&pageSize=100", headers=admin).json()
    kiem("admin loc theo ky", any(x["invoiceId"] == h["invoiceId"] for x in a["items"]), a.get("total"))

    print("\n[5] Bat bien o tang database")
    out = sql("ledger", f"update invoices set total_vnd = 1 where id = '{h['invoiceId']}'")
    kiem("UPDATE hoa don bi trigger tu choi", sql("ledger", f"select total_vnd from invoices where id = '{h['invoiceId']}'") == "18000", out)
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
