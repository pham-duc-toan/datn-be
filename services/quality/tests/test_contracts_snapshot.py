"""Payload khai o app/contracts.py phai KHOP TUNG TRUONG voi record C# (anh chup contracts/payloads.snapshot.txt).

Hop dong strict hai chieu: ben C# them truong ma Python chua khai → message vao DLQ; Python khai thua truong
bat buoc → cung vao DLQ. Test nay bat lech ngay luc sua, khong doi toi luc chay that.
"""

import inspect
import re
from pathlib import Path

from app import contracts
from app.messaging.envelope import Payload

SNAPSHOT = Path(__file__).resolve().parents[3] / "contracts" / "payloads.snapshot.txt"


def _doc_snapshot() -> dict[str, set[str]]:
    """{eventType: {ten truong PascalCase}} — chi cac dong truong cap mot cua moi event."""
    kq: dict[str, set[str]] = {}
    hien = None
    for dong in SNAPSHOT.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^(\S+) v\d+\s+\(\w+\)\s*$", dong)
        if m:
            hien = m.group(1)
            kq[hien] = set()
            continue
        m = re.match(r"^    (\w+)\s+\S", dong)
        if m and hien is not None:
            kq[hien].add(m.group(1))
        elif not dong.strip():
            hien = None
    return kq


def _pascal(ten: str) -> str:
    return "".join(p[:1].upper() + p[1:] for p in ten.split("_"))


def _cac_payload() -> list[type[Payload]]:
    return [c for _, c in inspect.getmembers(contracts, inspect.isclass)
            if issubclass(c, Payload) and c is not Payload and getattr(c, "EVENT_TYPE", None)]


def test_moi_payload_python_khop_truong_voi_csharp():
    anh_chup = _doc_snapshot()
    lech = {}
    for c in _cac_payload():
        assert c.EVENT_TYPE in anh_chup, f"{c.EVENT_TYPE} khong co trong anh chup hop dong"
        py = {_pascal(t) for t in c.model_fields}
        if py != anh_chup[c.EVENT_TYPE]:
            lech[c.EVENT_TYPE] = {"python_thua": sorted(py - anh_chup[c.EVENT_TYPE]), "python_thieu": sorted(anh_chup[c.EVENT_TYPE] - py)}
    assert not lech, lech


def test_co_du_event_dong_du_an():
    loai = {c.EVENT_TYPE for c in _cac_payload()}
    assert {"project.completed", "project.cancelled"} <= loai
