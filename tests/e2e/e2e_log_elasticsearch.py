# E2E LOG TAP TRUNG (Elasticsearch + Kibana + Filebeat), thay cho man hinh "hang doi loi (DLQ)" tren admin:
#   - moi service ghi log JSON ra stdout, Filebeat gom vao data stream crowd-logs → dev tra theo service / muc / noi dung;
#   - message hong roi vao DLQ de lai log muc Error (dev tim duoc nguyen nhan o Kibana, khong can man hinh admin);
#   - Kibana co san data view "Crowd logs".
# Chay: python e2e_log_elasticsearch.py   (stack demo: E2E_PROFILE=demo)
import json, os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import RABBIT_MGMT, RABBIT_PASSWORD, RABBIT_USER, URL_ES, URL_KIBANA  # noqa: E402

MGMT = (RABBIT_USER, RABBIT_PASSWORD)


def tim(query, size=5):
    r = requests.post(URL_ES + "/crowd-logs/_search", json={"size": size, "sort": [{"@timestamp": "desc"}], "query": query}, timeout=20)
    return r.json().get("hits", {}).get("hits", []) if r.status_code == 200 else []


def dem(query):
    r = requests.post(URL_ES + "/crowd-logs/_count", json={"query": query}, timeout=20)
    return r.json().get("count", 0) if r.status_code == 200 else 0


try:
    print("\n[1] Elasticsearch + Kibana san sang")
    es = requests.get(URL_ES + "/_cluster/health", timeout=10).json()
    kiem("Elasticsearch len (green / yellow)", es["status"] in ("green", "yellow"), es)
    kb = cho(lambda: requests.get(URL_KIBANA + "/api/status", timeout=10).status_code == 200, 120)
    kiem("Kibana len", kb)
    dv = requests.get(URL_KIBANA + "/api/data_views/data_view/crowd-logs", headers={"kbn-xsrf": "true"}, timeout=10)
    kiem("Kibana co san data view 'Crowd logs' (crowd-logs*)", dv.status_code == 200 and dv.json()["data_view"]["title"] == "crowd-logs*", dv.text[:200])

    print("\n[2] Moi service day log JSON len Elasticsearch")
    # Sinh log: dang ky tai khoan + goi vai API de cac service ghi dong log moi.
    u = dang_ky(["business"], "log-es")
    requests.get(G + "/projects", headers=u["h"])
    for ct in ("project", "identity", "admin", "gateway", "gate", "ledger", "quality", "media"):
        c = f"datn-demo-{ct}"
        ok = cho(lambda c=c: dem({"term": {"container.name": c}}) > 0, 90)
        kiem(f"co log cua service {ct} trong Elasticsearch", ok, c)
    h = tim({"term": {"container.name": "datn-demo-project"}}, 1)
    app = h[0]["_source"].get("app", {}) if h else {}
    kiem("dong log .NET duoc giai ma JSON: co LogLevel + Category + Message", {"LogLevel", "Category", "Message"} <= set(app), h and h[0]["_source"])
    hp = tim({"term": {"container.name": "datn-demo-quality"}}, 1)
    appp = hp[0]["_source"].get("app", {}) if hp else {}
    kiem("dong log python (quality) cung truong LogLevel / Category / Message", {"LogLevel", "Category", "Message"} <= set(appp), hp and hp[0]["_source"])
    kiem("loc duoc theo muc log (LogLevel: Information)", dem({"term": {"app.LogLevel": "Information"}}) > 0)

    print("\n[3] Message roi vao DLQ → log Error tra duoc tren Kibana (khong can man hinh DLQ)")
    ma = "e2e-dlq-" + uuid.uuid4().hex[:8]
    # Gui message KHONG dung hop dong vao queue audit cua admin: consumer tu choi → vao DLQ + ghi log Error.
    requests.post(f"{RABBIT_MGMT}/api/exchanges/%2F/amq.default/publish", auth=MGMT, json={
        "properties": {}, "routing_key": "admin-svc.audit", "payload": "{\"thu\":\"" + ma + "\"}", "payload_encoding": "string"}).raise_for_status()
    ok = cho(lambda: dem({"bool": {"must": [{"term": {"app.LogLevel": "Error"}}, {"match_phrase": {"app.Message": "DLQ"}}],
                                      "filter": [{"range": {"@timestamp": {"gte": "now-3m"}}}]}}) > 0, 60)
    kiem("log Error 'dua vao DLQ' cua consumer co trong Elasticsearch", ok)
    requests.delete(f"{RABBIT_MGMT}/api/queues/%2F/admin-svc.audit.dlq/contents", auth=MGMT)
finally:
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
