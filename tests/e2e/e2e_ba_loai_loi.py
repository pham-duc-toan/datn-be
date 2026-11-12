# E2E BA LOAI KET QUA XU LY EVENT (kien truc 3.29):
#   1. Ca binh thuong (doi tuong da xoa / da doi trang thai) → bo qua: khong thu lai, khong vao DLQ, khong lam phien ai.
#   2. Can nguoi xu ly (tien khong chi duoc...) → muc "Viec can xu ly tay" trong hang doi duyet, admin bam "Da xu ly".
#   3. Loi ky thuat → thu lai co gian cach, lan cuoi log Error, vao DLQ → the "Tinh trang he thong" + email doi ky thuat.
# Moi ca gui THANG mot envelope hop le vao DUNG mot queue (amq.default) — khong lam phien consumer khac.
# Chay: python e2e_ba_loai_loi.py   (stack demo: E2E_PROFILE=demo; chay MOT MINH — xem chay_hoi_quy.py)
import json, os, re, sys, time, uuid
from datetime import datetime, timezone
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import MAILPIT, RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER, URL_ES  # noqa: E402

MGMT = (RABBIT_USER, RABBIT_PASSWORD)
TU_DEV = re.compile(r"outbox|rabbitmq|worker|grpc|consumer|dlq|svc\b|event|exception|null", re.I)


def gui(queue, event_type, version, payload):
    """Envelope hop le → amq.default, routing key = ten queue (chi queue do nhan). Tra eventId."""
    eid = str(uuid.uuid4())
    env = {"eventId": eid, "eventType": event_type, "version": version, "producer": "e2e",
           "occurredAt": datetime.now(timezone.utc).isoformat(), "correlationId": str(uuid.uuid4()),
           "causationId": None, "actor": None, "payload": payload}
    requests.post(f"{RABBIT_MGMT}/api/exchanges/%2F/amq.default/publish", auth=MGMT, json={
        "properties": {"message_id": eid, "type": event_type, "content_type": "application/json", "delivery_mode": 2},
        "routing_key": queue, "payload": json.dumps(env), "payload_encoding": "string"}).raise_for_status()
    return eid, env


def gui_lai(queue, env):
    requests.post(f"{RABBIT_MGMT}/api/exchanges/%2F/amq.default/publish", auth=MGMT, json={
        "properties": {"message_id": env["eventId"], "type": env["eventType"], "content_type": "application/json", "delivery_mode": 2},
        "routing_key": queue, "payload": json.dumps(env), "payload_encoding": "string"}).raise_for_status()


def dlq(queue):
    r = requests.get(f"{RABBIT_MGMT}/api/queues/%2F/{queue}.dlq", auth=MGMT)
    return r.json().get("messages", 0) if r.status_code == 200 else -1


def es_dem(eid, muc=None):
    must = [{"match_phrase": {"app.Message": eid}}]
    if muc:
        must.append({"term": {"app.LogLevel": muc}})
    r = requests.post(URL_ES + "/crowd-logs/_count", json={"query": {"bool": {"must": must}}}, timeout=20)
    return r.json().get("count", 0) if r.status_code == 200 else 0


def es_lan(eid, muc):
    r = requests.post(URL_ES + "/crowd-logs/_search", json={"size": 20, "sort": [{"@timestamp": "asc"}], "query": {"bool": {"must": [
        {"match_phrase": {"app.Message": eid}}, {"term": {"app.LogLevel": muc}}]}}}, timeout=20)
    return [h["_source"]["@timestamp"] for h in r.json().get("hits", {}).get("hits", [])] if r.status_code == 200 else []


def viec_tay():
    return requests.get(G + "/admin/manual-actions", headers=admin, params={"status": "all", "pageSize": 200}).json()["items"]


def tim_viec(so_tien):
    return [x for x in viec_tay() if x["amountVnd"] == so_tien]


try:
    TIEN_KY_QUY = 100000 + int(uuid.uuid4().int % 800000)
    TIEN_LINK = 1000 + int(uuid.uuid4().int % 8000)

    # =================================================================================
    print("\n[1] Loai 1 — ca binh thuong: bo qua, khong thu lai, khong vao DLQ, khong tao viec cho admin")
    so_viec = len(viec_tay())
    pid_xoa = str(uuid.uuid4())
    e1, _ = gui("project-svc.video-prepared", "video.prepared", 1,
                {"projectId": pid_xoa, "storageKey": f"{pid_xoa}/video.mp4", "succeeded": True, "error": None})
    e2, _ = gui("project-svc.escrow-rejected", "escrow.rejected", 2,
                {"projectId": pid_xoa, "ownerId": str(uuid.uuid4()), "reason": "e2e"})
    kiem("video xong cho du an da xoa: log 'bo qua' muc Information", cho(lambda: es_dem(e1, "Information") > 0, 60))
    kiem("ky quy bi tu choi cho du an da xoa: log 'bo qua'", cho(lambda: es_dem(e2, "Information") > 0, 60))
    time.sleep(3)
    kiem("khong thu lai (khong co log canh bao / loi nao)", es_dem(e1, "Warning") + es_dem(e1, "Error") + es_dem(e2, "Warning") == 0)
    kiem("khong vao DLQ", dlq("project-svc.video-prepared") == 0 and dlq("project-svc.escrow-rejected") == 0,
         (dlq("project-svc.video-prepared"), dlq("project-svc.escrow-rejected")))
    kiem("khong tao viec cho admin", len(viec_tay()) == so_viec)

    # =================================================================================
    print("\n[2] Loai 2 — can nguoi xu ly: muc trong trang 'Viec can xu ly tay' (bang rieng manual_actions, 3.33)")
    pid_huy = str(uuid.uuid4())
    e3, env3 = gui("project-svc.escrow-reserved", "escrow.reserved", 1, {"projectId": pid_huy, "amountVnd": TIEN_KY_QUY})
    ds = cho(lambda: tim_viec(TIEN_KY_QUY), 60)
    kiem("ky quy cho du an da xoa → mo viec can xu ly tay", ds and len(ds) == 1, ds)
    v = ds[0] if ds else {}
    kiem("tieu de / mo ta bang loi thuong, co so tien", v.get("title") == "Kiểm tra tiền ký quỹ của dự án đã huỷ"
         and f"{TIEN_KY_QUY:,}".replace(",", ".") + " đ" in (v.get("detail") or "") and v.get("status") == "pending", v)
    kiem("mo ta khong co thuat ngu dev", not TU_DEV.search((v.get("title") or "") + " " + (v.get("detail") or "")), v.get("detail"))
    kiem("khong vao DLQ, khong thu lai", dlq("project-svc.escrow-reserved") == 0 and es_dem(e3, "Warning") <= 1 and es_dem(e3, "Error") == 0)
    kiem("danh sach mac dinh (dang cho) co viec nay", any(x["actionId"] == v.get("actionId") for x in
         requests.get(G + "/admin/manual-actions", headers=admin).json()["items"]))
    au = requests.get(G + "/admin/audit", headers=admin, params={"eventType": "manual_action.required", "pageSize": 50}).json()["items"]
    kiem("nhat ky co su kien 'phat sinh viec can xu ly tay' tro ve event goc", any(a["payload"].get("sourceEventId") == e3 for a in au))

    gui_lai("project-svc.escrow-reserved", env3)
    time.sleep(6)
    kiem("giao lai cung event: khong tao viec thu hai", len(tim_viec(TIEN_KY_QUY)) == 1)

    r = requests.post(G + f"/admin/manual-actions/{v.get('actionId')}/resolve", headers=admin, json={"note": ""})
    kiem("danh dau xong ma khong ghi chu → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/admin/manual-actions/{v.get('actionId')}/resolve", headers=admin, json={"note": "Đã kiểm tra ví, đã hoàn tiền"})
    kiem("danh dau da xu ly → 204", r.status_code == 204, r.text)
    v2 = tim_viec(TIEN_KY_QUY)[0]
    kiem("muc chuyen 'resolved', giu mo ta + ghi chu xu ly", v2["status"] == "resolved" and v2["note"] == "Đã kiểm tra ví, đã hoàn tiền"
         and v2["detail"] == v["detail"] and v2["resolvedAt"], v2)
    r = requests.post(G + f"/admin/manual-actions/{v.get('actionId')}/resolve", headers=admin, json={"note": "lan hai"})
    kiem("danh dau lan hai → 409", r.status_code == 409, r.text)
    kiem("nhat ky ghi admin xu ly xong", cho(lambda: any(a["payload"].get("actionId") == v.get("actionId") and a["actorRole"] == "admin"
         for a in requests.get(G + "/admin/audit", headers=admin, params={"eventType": "manual_action.resolved"}).json()["items"]), 20))

    # Ledger: tien luot vuot link cho du an khong co ky quy.
    che_do = requests.get(G + "/admin/settings/ledger.gate_payout_mode", headers=admin).json()["value"]
    if che_do == "perClick":
        e4, _ = gui("ledger-svc.click-validated", "click.validated", 1, {
            "clickId": str(uuid.uuid4()), "labelCount": 2, "linkId": str(uuid.uuid4()), "platformAmountVnd": 100,
            "projectId": str(uuid.uuid4()), "sharerAmountVnd": TIEN_LINK, "sharerId": str(uuid.uuid4()),
            "validatedAt": datetime.now(timezone.utc).isoformat()})
        dl = cho(lambda: tim_viec(TIEN_LINK), 60)
        kiem("tien luot vuot link khong chi duoc → viec can xu ly tay (ledger)", dl and dl[0]["title"] == "Không trả được tiền lượt vượt link", dl)
        kiem("ledger: khong vao DLQ", dlq("ledger-svc.click-validated") == 0)
        if dl:
            requests.post(G + f"/admin/manual-actions/{dl[0]['actionId']}/resolve", headers=admin, json={"note": "e2e"})
    else:
        print("  (bo qua ledger) ledger.gate_payout_mode =", che_do)

    # =================================================================================
    print("\n[3] Loai 3 — loi ky thuat: thu lai co gian cach, lan cuoi log Error, DLQ → the tinh trang + email doi ky thuat")
    ops = f"ops-{TAG}@crowd.local"
    dat_va_cho("consumers.retry_max_delay", 5, ["project"])
    dat_va_cho("ops.alert_email", ops, ["notification"])
    dat_va_cho("ops.dlq_check_interval", 10, [])
    dat_va_cho("ops.alert_repeat", 60, [])
    pid_la = str(uuid.uuid4())
    t0 = time.time()
    e5, _ = gui("project-svc.project-graded", "project.graded", 2, {
        "attempts": [], "gradedAt": datetime.now(timezone.utc).isoformat(), "majorityUsed": True,
        "ownerId": str(uuid.uuid4()), "passThresholdPercent": 60, "projectId": pid_la})
    vao = cho(lambda: dlq("project-svc.project-graded") >= 1, 120, 2)
    kiem("du an chua tung chay ma 'cham xong' (sai bat bien) → vao DLQ sau cac lan thu", vao, dlq("project-svc.project-graded"))
    kiem("khong tao viec cho admin (loi ky thuat khong phai viec cua admin)", not any(e5 in (x.get("detail") or "") for x in viec_tay()))
    cho(lambda: es_dem(e5, "Error") > 0, 60)
    canh_bao, loi = es_lan(e5, "Warning"), es_lan(e5, "Error")
    kiem("thu lai 5 lan (log Warning) roi lan cuoi log Error 'chuyen vao DLQ'", len(canh_bao) == 5 and len(loi) == 1, (len(canh_bao), len(loi)))
    if canh_bao and loi:
        gian = (datetime.fromisoformat(loi[0].replace("Z", "+00:00")) - datetime.fromisoformat(canh_bao[0].replace("Z", "+00:00"))).total_seconds()
        kiem("cac lan thu cach nhau (1 + 3 + 5 + 5 + 5 giay)", gian >= 15, gian)
    print(f"  (vao DLQ sau {time.time() - t0:.0f}s)")
    st = cho(lambda: (lambda h: h if h["status"] == "co_loi" else None)(requests.get(G + "/admin/system-health", headers=admin).json()), 40)
    kiem("the 'Tinh trang he thong': co_loi, dem so viec, da bao doi ky thuat", st and st["failedCount"] >= 1 and st["techTeamNotified"], st)
    kiem("phan hoi tinh trang khong lo ten hang doi", st and "project-svc" not in json.dumps(st), st)
    thu = cho(lambda: (lambda m: m if m else None)(
        requests.get(f"{MAILPIT}/api/v1/search", params={"query": f'to:"{ops}"', "limit": 5}).json().get("messages")), 90)
    kiem("email canh bao loi he thong toi doi ky thuat", bool(thu) and "Lỗi hệ thống" in thu[0]["Subject"], thu and thu[0]["Subject"])
    requests.delete(f"{RABBIT_MGMT}/api/queues/%2F/project-svc.project-graded.dlq/contents", auth=MGMT)
    ok = cho(lambda: requests.get(G + "/admin/system-health", headers=admin).json()["failedCount"] < st["failedCount"] if st else False, 40)
    kiem("don DLQ → the tinh trang giam theo", ok)
finally:
    for k, gt in list(GOC.items()):
        try:
            dat(k, gt)
        except Exception:  # noqa: BLE001
            pass
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
