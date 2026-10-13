"""Sinh file .env cho stack Docker demo (docker-compose.demo.yml) — S-08: khong con mat khau trong repo.

    python deploy/tao_env.py           # tao .env voi mat khau NGAU NHIEN (da co thi giu nguyen)
    python deploy/tao_env.py --ghi-de  # tao lai (CHI dung khi da xoa volume: DB cu van dung mat khau cu)
    python deploy/tao_env.py --dev     # dung lai mat khau dev cu (dev_<ten>_pw): nang cap stack demo
                                       # da chay truoc S-08 ma KHONG xoa volume (Postgres giu mat khau luc tao)

.env nam trong .gitignore. Danh sach bien: .env.example.
"""

import os
import secrets
import sys

GOC = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DB = ["identity", "project", "task", "annotation", "ledger", "payment", "link", "gate", "quality", "admin", "notification"]


def mat_khau() -> str:
    # Chi chu + so: nam trong connection string / URL ma khong can thoat ky tu.
    return secrets.token_hex(16)


def gia_tri(ten: str, dev: bool) -> str:
    return f"dev_{ten}_pw" if dev else mat_khau()


def main() -> int:
    duong = os.path.join(GOC, ".env")
    if os.path.exists(duong) and "--ghi-de" not in sys.argv:
        print(f".env da co ({duong}) — giu nguyen. Dung --ghi-de de tao lai.")
        return 0

    dev = "--dev" in sys.argv
    dong = ["# Sinh boi deploy/tao_env.py — KHONG commit.", ""]
    dong.append(f"RABBIT_PASSWORD={gia_tri('rabbit', dev)}")
    dong.append(f"MINIO_PASSWORD={gia_tri('minio', dev)}")
    dong.append(f"INTERNAL_API_KEY={'demo_internal_key' if dev else mat_khau()}")
    for d in DB:
        dong.append(f"DB_{d.upper()}_PASSWORD={gia_tri(d, dev)}")

    with open(duong, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(dong) + "\n")
    print(f"Da tao {duong} ({len(DB) + 3} bi mat {'dev cu' if dev else 'ngau nhien'}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
