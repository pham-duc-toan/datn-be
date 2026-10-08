"""Dong thuan cho MOT task — logic thuan, khong DB, de test.

Chi cac cong cu chon lua roi rac moi gop tu dong duoc:
  classification — moi lop xet doc lap, lop duoc chon khi HON MOT NUA so nguoi
                   tra loi cong cu do chon no (cung luat ResultAggregator ben C#);
  pairwise       — lua chon a / b / tie qua ban.
Cac cong cu khac (bbox, polygon, span, transcription, temporalSegment) chua gop
tu dong: khong tinh vao dong thuan.

Ket qua:
  agreed        — moi cong cu gop duoc deu co ket qua qua ban;
  disputed      — co cong cu khong lua chon nao qua ban;
  notApplicable — tap nhan khong co cong cu nao gop duoc.
Tranh chap ma chua cham tran redundancy → xin them mot nguoi (vong lap thich ung).
"""

from dataclasses import dataclass, field
from typing import Any

CONG_CU_GOP_DUOC = ("classification", "pairwise")


@dataclass(frozen=True)
class Nhan:
    annotation_id: str
    labeler_id: str
    data: dict[str, Any]


@dataclass
class KetQua:
    status: str                                  # agreed | disputed | notApplicable
    final: dict[str, Any] | None                 # theo tung cong cu gop duoc; cong cu tranh chap = None
    agrees: dict[str, bool | None] = field(default_factory=dict)   # annotation_id → khop?
    xin_them: int | None = None                  # redundancy moi neu can xin them nguoi


def _lop(ket_qua: Any) -> frozenset[str]:
    return frozenset(ket_qua.get("labelIds", [])) if isinstance(ket_qua, dict) else frozenset()


def _chon(ket_qua: Any) -> str | None:
    return ket_qua.get("choice") if isinstance(ket_qua, dict) else None


def _gop_classification(ket_qua: list[Any]) -> dict[str, Any] | None:
    n = len(ket_qua)
    phieu: dict[str, int] = {}
    for k in ket_qua:
        for lop in _lop(k):
            phieu[lop] = phieu.get(lop, 0) + 1
    chot = sorted(l for l, v in phieu.items() if v * 2 > n)
    return {"labelIds": chot} if chot else None


def _gop_pairwise(ket_qua: list[Any]) -> dict[str, Any] | None:
    n = len(ket_qua)
    phieu: dict[str, int] = {}
    for k in ket_qua:
        c = _chon(k)
        if c is not None:
            phieu[c] = phieu.get(c, 0) + 1
    for c, v in phieu.items():
        if v * 2 > n:
            return {"choice": c}
    return None


def _khop(kind: str, ket_qua: Any, final: dict[str, Any]) -> bool:
    if kind == "classification":
        return _lop(ket_qua) == frozenset(final["labelIds"])
    return _chon(ket_qua) == final["choice"]


def danh_gia(label_schema: dict[str, Any], nhan: list[Nhan], target: int, max_redundancy: int) -> KetQua:
    """Tinh dong thuan tren cac nhan da nop cua mot task (da du `target` nguoi)."""
    cong_cu = [t for t in label_schema.get("tools", []) if t.get("kind") in CONG_CU_GOP_DUOC]

    if not cong_cu:
        return KetQua(status="notApplicable", final=None, agrees={n.annotation_id: None for n in nhan})

    final: dict[str, Any] = {}
    tranh_chap = False

    for t in cong_cu:
        ten = t["name"]
        ket_qua = [n.data[ten] for n in nhan if ten in n.data]
        if not ket_qua:
            # Cong cu tuy chon khong ai lam — khong co gi de gop, khong phai tranh chap.
            continue

        gop = _gop_classification(ket_qua) if t["kind"] == "classification" else _gop_pairwise(ket_qua)
        final[ten] = gop
        if gop is None:
            tranh_chap = True

    if tranh_chap and target < max_redundancy:
        return KetQua(status="disputed", final=final, xin_them=target + 1)

    agrees: dict[str, bool | None] = {}
    kind_theo_ten = {t["name"]: t["kind"] for t in cong_cu}
    for n in nhan:
        da_xet = False
        khop_het = True
        for ten, gop in final.items():
            if gop is None or ten not in n.data:
                continue
            da_xet = True
            if not _khop(kind_theo_ten[ten], n.data[ten], gop):
                khop_het = False
        agrees[n.annotation_id] = khop_het if da_xet else None

    return KetQua(status="disputed" if tranh_chap else "agreed", final=final, agrees=agrees)
