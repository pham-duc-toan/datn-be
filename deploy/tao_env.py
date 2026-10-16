"""Sinh file .env cho stack Docker demo (docker-compose.demo.yml) — S-08: khong con mat khau trong repo.

    python deploy/tao_env.py           # tao .env voi bi mat NGAU NHIEN; da co .env thi chi BO SUNG bien con thieu
    python deploy/tao_env.py --ghi-de  # tao lai tu dau (CHI dung khi da xoa volume: DB cu van dung mat khau cu)
    python deploy/tao_env.py --dev     # dung gia tri dev (dev_<ten>_pw, khoa dev): nang cap stack demo
                                       # da chay truoc S-08 ma KHONG xoa volume (Postgres giu mat khau luc tao)

.env nam trong .gitignore. Danh sach bien: .env.example.
"""

import base64
import os
import secrets
import sys

GOC = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DB = ["identity", "project", "task", "annotation", "ledger", "payment", "link", "gate", "quality", "admin", "notification"]

# Khoa dev — trung appsettings.Development.json (MediaLinks:GrantKey) va services/media/app/config.py.
KHOA_DEV = {
    "MEDIA_GRANT_KEY": "pgZwtqPVxGvu3oA5esvFaUio+ltJ2sSfgOWqYQNHwyU=",
    "MEDIA_WATERMARK_KEY": "TUmrnIsOHHdHl7BEEngcDqPfPQW6v956RTfgOEOlILo=",
    # identity-svc: ma hoa bi mat 2FA, bam co khoa so giay to (IdentityData:Key).
    "IDENTITY_DATA_KEY": "q5Ec0b2l2b0mW3Yw2kOQ7mR6x8H1sZ9vT4nJ3cP0aUo=",
}


def mat_khau() -> str:
    # Chi chu + so: nam trong connection string / URL ma khong can thoat ky tu.
    return secrets.token_hex(16)


def khoa_aes() -> str:
    """32 byte ngau nhien, base64 — khoa AES-256 / khoa watermark."""
    return base64.b64encode(secrets.token_bytes(32)).decode()


def gia_tri(ten: str, dev: bool) -> str:
    if ten in KHOA_DEV:
        return KHOA_DEV[ten] if dev else khoa_aes()
    if ten == "INTERNAL_API_KEY":
        return "demo_internal_key" if dev else mat_khau()
    if not dev:
        return mat_khau()
    if ten.startswith("DB_"):
        return f"dev_{ten[3:-len('_PASSWORD')].lower()}_pw"
    return f"dev_{ten.split('_')[0].lower()}_pw"


def danh_sach_bien() -> list[str]:
    return ["RABBIT_PASSWORD", "MINIO_PASSWORD", "INTERNAL_API_KEY", "MEDIA_GRANT_KEY", "MEDIA_WATERMARK_KEY",
            "IDENTITY_DATA_KEY"] + \
        [f"DB_{d.upper()}_PASSWORD" for d in DB]


def doc_env(duong: str) -> dict[str, str]:
    da_co = {}
    with open(duong, encoding="utf-8") as f:
        for dong in f:
            if "=" in dong and not dong.lstrip().startswith("#"):
                ten, _, gt = dong.strip().partition("=")
                da_co[ten] = gt
    return da_co


def main() -> int:
    duong = os.path.join(GOC, ".env")
    dev = "--dev" in sys.argv

    if os.path.exists(duong) and "--ghi-de" not in sys.argv:
        da_co = doc_env(duong)
        thieu = [t for t in danh_sach_bien() if not da_co.get(t)]
        if not thieu:
            print(f".env da du bien ({duong}) — giu nguyen. Dung --ghi-de de tao lai.")
            return 0
        with open(duong, "a", encoding="utf-8", newline="\n") as f:
            for t in thieu:
                f.write(f"{t}={gia_tri(t, dev)}\n")
        print(f"Bo sung {len(thieu)} bien vao {duong}: {', '.join(thieu)} (gia tri cu giu nguyen).")
        return 0

    dong = ["# Sinh boi deploy/tao_env.py — KHONG commit.", ""]
    for t in danh_sach_bien():
        dong.append(f"{t}={gia_tri(t, dev)}")
    with open(duong, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(dong) + "\n")
    print(f"Da tao {duong} ({len(danh_sach_bien())} bi mat {'dev' if dev else 'ngau nhien'}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
