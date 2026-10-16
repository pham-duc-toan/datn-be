# E2E THONG BAO: ngon ngu thong bao / email (FC-08); day realtime qua WebSocket (ve dung mot lan, qua gateway);
# 2FA → thong bao + email BAT BUOC; loi moi email chua co tai khoan → email kem link dang ky;
# canh bao DLQ → email van hanh; han giu thong bao.
# Chay: python e2e_thong_bao_realtime.py   (stack demo: E2E_PROFILE=demo)
import base64, hashlib, hmac, json, os, struct, sys, time, uuid
import requests
import websocket

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER  # noqa: E402
from tai_khoan_e2e import thu_moi_nhat  # noqa: E402
from moi_truong import MAILPIT as MAILPIT_URL  # noqa: E402

WS = G.replace("http://", "ws://").replace("https://", "wss://")


def totp(secret_b32):
    key = base64.b32decode(secret_b32 + "=" * (-len(secret_b32) % 8))
    h = hmac.new(key, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    o = h[-1] & 0x0F
    return "%06d" % ((struct.unpack(">I", h[o:o + 4])[0] & 0x7FFFFFFF) % 1000000)


def ve(u):
    r = requests.post(G + "/notifications/me/realtime-ticket", headers=u["h"])
    assert r.status_code == 200, r.text
    return r.json()["ticket"]


def ma_dong(ws):
    """Doc khung dau tien: khung DONG → ma dong (2 byte dau); khong phai → None."""
    op, data = ws.recv_data(control_frame=True)
    ws.close()
    return struct.unpack(">H", data[:2])[0] if op == websocket.ABNF.OPCODE_CLOSE and len(data) >= 2 else None


def mo_ws(ticket):
    return websocket.create_connection(f"{WS}/notifications/realtime?ticket={ticket}", timeout=20)


try:
    u = dang_ky(["labeler"], "tbrt")

    # =================================================================================
    print("\n[1] Ngon ngu thong bao (FC-08)")
    kiem("mac dinh tieng Viet", requests.get(G + "/notifications/me/language", headers=u["h"]).json()["language"] == "vi")
    kiem("ngon ngu la → 400", requests.put(G + "/notifications/me/language", headers=u["h"], json={"language": "fr"}).status_code == 400)
    r = requests.put(G + "/notifications/me/language", headers=u["h"], json={"language": "EN"})
    kiem("chon tieng Anh", r.status_code == 200 and r.json()["language"] == "en", r.text)

    # =================================================================================
    print("\n[2] WebSocket realtime qua gateway")
    kiem("ve sai → dong ngay, ma 4401", ma_dong(mo_ws("khong-hop-le")) == 4401)
    t = ve(u)
    ws = mo_ws(t)
    chao = json.loads(ws.recv())
    kiem("mo WebSocket bang ve → khung connected", chao.get("kind") == "connected", chao)
    kiem("dung lai ve (dung mot lan) → dong ngay, ma 4401", ma_dong(mo_ws(t)) == 4401)

    # Bat 2FA → user.two_factor_changed → thong bao BaoMat (tieng Anh) day qua WebSocket.
    secret = requests.post(G + "/me/2fa/setup", headers=u["h"]).json()["secret"]
    requests.post(G + "/me/2fa/enable", headers=u["h"], json={"code": totp(secret)}).raise_for_status()
    ws.settimeout(30)
    khung = json.loads(ws.recv())
    tb = khung.get("notification") or {}
    kiem("nhan thong bao realtime ngay (khong can hoi lai)", khung.get("kind") == "notification" and tb.get("type") == "bao_mat", khung)
    kiem("noi dung theo ngon ngu da chon (tieng Anh)", tb.get("title") == "Two-factor authentication enabled", tb.get("title"))
    ws.close()
    kiem("cung thong bao co trong danh sach", any(x["id"] == tb.get("id") for x in requests.get(G + "/notifications/me", headers=u["h"]).json()["items"]))
    kiem("email bao mat BAT BUOC (tieng Anh)", thu_moi_nhat(u["email"], "Two-factor authentication enabled") is not None)

    # =================================================================================
    print("\n[3] Loi moi email chua co tai khoan → email kem link dang ky")
    biz = dang_ky(["business"], "tbrt-biz")
    P, _ = tao_du_an(biz, "loi moi email", {"c0": "cau 0"}, 1, 1, 1000)
    moi = f"e2e-{TAG}-moi-moi@crowd.local"
    r = requests.post(G + f"/projects/{P}/members", headers=biz["h"], json={"email": moi, "role": "labeler"})
    kiem("moi email chua co tai khoan → 202", r.status_code == 202, r.text)
    thu = cho(lambda: (lambda m: m if m else None)(
        requests.get(f"{MAILPIT_URL}/api/v1/search", params={"query": f'to:"{moi}"', "limit": 5}).json().get("messages")), 45)
    noi = requests.get(f"{MAILPIT_URL}/api/v1/message/{thu[0]['ID']}").json()["Text"] if thu else ""
    kiem("email loi moi co link dang ky kem email + ma loi moi", "/dang-ky?email=" in noi and "invitation=" in noi, noi[:300])

    # =================================================================================
    print("\n[4] Canh bao DLQ → email van hanh")
    ops = f"ops-{TAG}@crowd.local"
    dat_va_cho("ops.alert_email", ops, ["notification"])
    dat_va_cho("ops.dlq_check_interval", 10, [])
    dat_va_cho("ops.alert_repeat", 60, [])
    q = "notification-svc.ops-dlq-alert.dlq"
    requests.post(f"{RABBIT_MGMT}/api/exchanges/%2F/datn.dlx/publish", auth=(RABBIT_USER, RABBIT_PASSWORD), json={
        "properties": {}, "routing_key": "notification-svc.ops-dlq-alert", "payload": "{}", "payload_encoding": "string"}).raise_for_status()
    thu = cho(lambda: (lambda m: m if m else None)(
        requests.get(f"{MAILPIT_URL}/api/v1/search", params={"query": f'to:"{ops}"', "limit": 5}).json().get("messages")), 60)
    kiem("email canh bao DLQ toi dia chi van hanh", bool(thu) and q in thu[0]["Subject"], thu and thu[0]["Subject"])
    requests.delete(f"{RABBIT_MGMT}/api/queues/%2F/{q}/contents", auth=(RABBIT_USER, RABBIT_PASSWORD))

    # =================================================================================
    print("\n[5] Han giu thong bao")
    cu = str(uuid.uuid4())
    sql("notification", f"insert into notifications (id, user_id, type, title, body, created_at, email_state, email_attempts, merged_count) "
                        f"values ('{cu}', '{u['id']}', 'he_thong', 'cu', 'cu', now() - interval '400 days', 'Skipped', 0, 1)")
    dat_va_cho("notification.cleanup_interval", 60, ["notification"])
    kiem("thong bao qua han giu (400 ngay > 180) bi xoa", cho(lambda: sql("notification", f"select count(*) from notifications where id = '{cu}'") == "0", 90))
    kiem("thong bao moi van con", sql("notification", f"select count(*) from notifications where id = '{tb.get('id')}'") == "1")
finally:
    for k, v in GOC.items():
        requests.put(G + f"/admin/settings/{k}", headers=admin, json={"value": v, "reason": "tra lai sau e2e"})
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
