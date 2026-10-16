# E2E CONG THANH TOAN (FB-03): VNPay + MoMo qua cong GIA LAP (dong vai cong that, ky / kiem dung chuan),
# IPN co chu ky + hoi nguoc cong (VD-S-02), dong bo khi IPN lac, doi soat lenh treo (VD-M-09).
# Chay: python e2e_cong_thanh_toan.py   (stack demo: E2E_PROFILE=demo)
import os, sys, time, uuid
from urllib.parse import parse_qsl, urlsplit
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403


def nap(u, tien, cong, khoa=None):
    return requests.post(G + "/payments/deposits", headers={**u["h"], "Idempotency-Key": khoa or uuid.uuid4().hex},
                         json={"amountVnd": tien, "provider": cong})


def lenh(u, iid):
    return requests.get(G + f"/payments/deposits/{iid}", headers=u["h"]).json()


def kha_dung(u):
    return vi(u).get("businessAvailableVnd", 0)


try:
    biz = dang_ky(["business"], "congtt")
    lab = dang_ky(["labeler"], "congtt-lab")

    # =================================================================================
    print("\n[1] Danh sach cong; chon cong khong mo")
    r = requests.get(G + "/payments/deposits/providers", headers=biz["h"])
    kiem("co sandbox / vnpay / momo / manual_transfer", r.status_code == 200 and set(r.json()["providers"]) >= {"sandbox", "vnpay", "momo", "manual_transfer"}, r.text)
    r = nap(biz, 50000, "paypal")
    kiem("cong la → 400 cong_khong_ho_tro", r.status_code == 400 and "cong_khong_ho_tro" in r.text, r.text)
    kiem("labeler khong nap duoc", nap(lab, 50000, "vnpay").status_code == 403)

    # =================================================================================
    print("\n[2] VNPay: tao lenh → trang thanh toan → IPN co chu ky → hoi nguoc querydr → cong vi")
    truoc = kha_dung(biz)
    r = nap(biz, 50000, "vnpay")
    kiem("tao lenh VNPay → checkoutUrl co vnp_SecureHash", r.status_code == 200 and "vnp_SecureHash=" in (r.json()["checkoutUrl"] or ""), r.text)
    d = r.json()
    url = d["checkoutUrl"]
    q = dict(parse_qsl(urlsplit(url).query))
    kiem("so tien gui VNPay = x100, ma don = intentId", q["vnp_Amount"] == "5000000" and q["vnp_TxnRef"] == d["intentId"].replace("-", ""), q)
    kiem("vnp_IpAddr la IP nguoi dung (khong rong)", len(q.get("vnp_IpAddr", "")) >= 7, q)
    kiem("sua vnp_Amount tren URL → cong bao sai chu ky",
         requests.get(url.replace("vnp_Amount=5000000", "vnp_Amount=100")).status_code == 400)
    r = requests.get(url)
    kiem("mo trang thanh toan (cong kiem chu ky dung)", r.status_code == 200 and r.json()["amountVnd"] == 50000, r.text)
    r = requests.post(G + f"/payments/sandbox/vnpay/pay/{q['vnp_TxnRef']}?result=success")
    kiem("tra tien → IPN tra RspCode 00", r.status_code == 200 and '"RspCode":"00"' in (r.json()["ipnResponse"] or ""), r.text)
    redirect = r.json()["redirect"]
    l = cho(lambda: lenh(biz, d["intentId"]) if lenh(biz, d["intentId"])["status"] == "succeeded" else None, 10)
    kiem("lenh VNPay → succeeded", l is not None, lenh(biz, d["intentId"]))
    kiem("vi doanh nghiep +50.000 (ledger nhan deposit.confirmed)", cho(lambda: kha_dung(biz) == truoc + 50000, 20), (truoc, vi(biz)))

    ipn = dict(parse_qsl(urlsplit(redirect).query))
    r = requests.get(G + "/payments/webhooks/vnpay", params=ipn)
    kiem("IPN lap lai → RspCode 02, khong cong them", r.json()["RspCode"] == "02", r.text)
    time.sleep(2)
    kiem("vi khong doi sau IPN lap", kha_dung(biz) == truoc + 50000)
    ipn_gia = dict(ipn)
    ipn_gia["vnp_Amount"] = "999900000"
    r = requests.get(G + "/payments/webhooks/vnpay", params=ipn_gia)
    kiem("IPN bi sua so tien (chu ky cu) → RspCode 97", r.json()["RspCode"] == "97", r.text)
    ipn_la = dict(ipn)
    ipn_la["vnp_TxnRef"] = uuid.uuid4().hex
    r = requests.get(G + "/payments/webhooks/vnpay", params=ipn_la)
    kiem("IPN cua lenh khong ton tai (chu ky sai vi doi TxnRef) → 97", r.json()["RspCode"] == "97", r.text)

    # =================================================================================
    print("\n[3] VNPay: IPN bi lac → doanh nghiep bam kiem tra (sync) → hoi nguoc cong")
    truoc = kha_dung(biz)
    d = nap(biz, 30000, "vnpay").json()
    url = d["checkoutUrl"]
    ref = dict(parse_qsl(urlsplit(url).query))["vnp_TxnRef"]
    requests.get(url)
    r = requests.post(G + f"/payments/deposits/{d['intentId']}/sync", headers=biz["h"])
    kiem("chua tra ma bam kiem tra → van pending", r.status_code == 200 and r.json()["status"] == "pending", r.text)
    requests.post(G + f"/payments/sandbox/vnpay/pay/{ref}?result=success&sendIpn=false").raise_for_status()
    time.sleep(1)
    kiem("IPN lac → lenh chua doi", lenh(biz, d["intentId"])["status"] == "pending")
    r = requests.post(G + f"/payments/deposits/{d['intentId']}/sync", headers=biz["h"])
    kiem("sync sau khi tra → succeeded", r.status_code == 200 and r.json()["status"] == "succeeded", r.text)
    kiem("vi +30.000", cho(lambda: kha_dung(biz) == truoc + 30000, 20), vi(biz))
    khac = dang_ky(["business"], "congtt-khac")
    kiem("doanh nghiep khac sync lenh nay → 404 (BOLA)",
         requests.post(G + f"/payments/deposits/{d['intentId']}/sync", headers=khac["h"]).status_code == 404)

    print("\n[4] VNPay: nguoi dung huy tren cong → IPN that bai")
    d = nap(biz, 20000, "vnpay").json()
    requests.get(d["checkoutUrl"])
    ref = dict(parse_qsl(urlsplit(d["checkoutUrl"]).query))["vnp_TxnRef"]
    r = requests.post(G + f"/payments/sandbox/vnpay/pay/{ref}?result=cancel")
    l = lenh(biz, d["intentId"])
    kiem("huy → failed, failureCode cong_24", l["status"] == "failed" and l["failureCode"] == "cong_24", l)

    # =================================================================================
    print("\n[5] MoMo: tao lenh qua API (co chu ky) → IPN POST → cong vi")
    truoc = kha_dung(biz)
    r = nap(biz, 75000, "momo")
    kiem("tao lenh MoMo → payUrl tu cong", r.status_code == 200 and "/payments/sandbox/momo/pay/" in (r.json()["checkoutUrl"] or ""), r.text)
    d = r.json()
    order = d["checkoutUrl"].rsplit("/", 1)[1]
    r = requests.post(G + f"/payments/sandbox/momo/pay/{order}?result=success")
    kiem("tra tien → IPN 204", r.status_code == 200 and r.json()["ipnStatus"] == 204, r.text)
    kiem("lenh MoMo → succeeded", cho(lambda: lenh(biz, d["intentId"])["status"] == "succeeded", 10), lenh(biz, d["intentId"]))
    kiem("vi +75.000", cho(lambda: kha_dung(biz) == truoc + 75000, 20), vi(biz))
    r = requests.post(G + "/payments/webhooks/momo", json={"partnerCode": "MOMODATN", "orderId": order, "amount": 75000000, "resultCode": 0, "signature": "ab"})
    kiem("IPN MoMo gia (chu ky sai) → 400", r.status_code == 400, r.status_code)

    # =================================================================================
    print("\n[6] Doi soat (VD-M-09): IPN MoMo lac → worker hoi nguoc cong tu chot")
    dat_va_cho("payment.deposit_lookup_after", 60, ["payment"])
    dat_va_cho("payment.deposit_reconcile_interval", 5, ["payment"])
    truoc = kha_dung(biz)
    d = nap(biz, 40000, "momo").json()
    order = d["checkoutUrl"].rsplit("/", 1)[1]
    requests.post(G + f"/payments/sandbox/momo/pay/{order}?result=success&sendIpn=false").raise_for_status()
    kiem("ngay sau khi tra: van pending (IPN lac)", lenh(biz, d["intentId"])["status"] == "pending")
    ok = cho(lambda: lenh(biz, d["intentId"])["status"] == "succeeded", 100, 2)
    kiem("~60 giay sau: worker doi soat chot succeeded", ok, lenh(biz, d["intentId"]))
    kiem("vi +40.000", cho(lambda: kha_dung(biz) == truoc + 40000, 20), vi(biz))

    print("\n[7] Doi soat: lenh qua han chua tra → het_han")
    d = nap(biz, 15000, "sandbox").json()
    sql("payment", f"update payment_intents set created_at = now() - interval '3 days' where id = '{d['intentId']}'")
    ok = cho(lambda: lenh(biz, d["intentId"])["status"] == "failed", 30, 1)
    l = lenh(biz, d["intentId"])
    kiem("lenh sandbox qua payment.deposit_expire_after → failed het_han", ok and l["failureCode"] == "het_han", l)

    print("\n[8] Admin tat cong MoMo")
    dat_va_cho("payment.enabled_providers", "sandbox,vnpay", ["payment"])
    r = nap(biz, 50000, "momo")
    kiem("momo bi tat → 400", r.status_code == 400 and "cong_khong_ho_tro" in r.text, r.text)
    kiem("danh sach cong khong con momo", "momo" not in requests.get(G + "/payments/deposits/providers", headers=biz["h"]).json()["providers"])
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
