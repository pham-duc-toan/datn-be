"""Cau hinh chung cho cac bo E2E — doc tu bien moi truong, mac dinh = moi truong DEV.

Dev (service chay tren may + docker-compose.infra.yml):   khong can dat gi.
Stack demo / CI (docker-compose.demo.yml):                 E2E_PROFILE=demo
Tung gia tri van ghi de duoc rieng: E2E_GATEWAY, E2E_TASK_URL, E2E_ANNOTATION_URL, E2E_LINK_URL,
E2E_GATE_URL, E2E_RABBIT_MGMT, E2E_CONTAINER_PREFIX.
"""

import os

GOC_REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

_PROFILE = os.environ.get("E2E_PROFILE", "dev").lower()
# Stack demo lech cong +10000 va dat ten container "datn-demo-*" (xem docker-compose.demo.yml).
_LECH = 10000 if _PROFILE == "demo" else 0
_TIEN_TO = "datn-demo-" if _PROFILE == "demo" else "datn-"


def _url(bien: str, cong: int) -> str:
    return os.environ.get(bien, f"http://localhost:{cong + _LECH}")


G = _url("E2E_GATEWAY", 8080)
URL_TASK = _url("E2E_TASK_URL", 8103)
URL_ANNOTATION = _url("E2E_ANNOTATION_URL", 8104)
URL_LINK = _url("E2E_LINK_URL", 8107)
URL_GATE = _url("E2E_GATE_URL", 8108)
RABBIT_MGMT = _url("E2E_RABBIT_MGMT", 15672)
TIEN_TO_CONTAINER = os.environ.get("E2E_CONTAINER_PREFIX", _TIEN_TO)

# Mat khau chung cua moi tai khoan seed va tai khoan E2E tu tao.
PW = "Matkhau@123"


def sql_container(db: str) -> str:
    """Ten container Postgres cua service `db` (identity, task, ...)."""
    return f"{TIEN_TO_CONTAINER}db-{db}"


def rabbit_container() -> str:
    return f"{TIEN_TO_CONTAINER}rabbitmq"
