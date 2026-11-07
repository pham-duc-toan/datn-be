"""Dong thuan cho MOT mau — logic thuan, khong DB, de test.

Moi cong cu cua tap nhan duoc GOP theo cach cua loai cong cu do (app/gop.py — noi DUY NHAT cai thuat toan gop;
project-svc goi qua gRPC LabelAggregation khi can ket qua giao cho doanh nghiep, kien truc 3.24):
  classification — moi lop xet doc lap, lop duoc chon khi HON MOT NUA so nguoi tra loi cong cu do chon no;
  pairwise       — lua chon a / b / tie qua ban;
  bbox, polygon, keypoint, polyline, mask, temporalSegment, track — ghep cum cac doi tuong "cung mot
                   vat", giu cum co hon mot nua so nguoi; span, relation — phan tu duoc qua nua so
                   nguoi ghi y het; transcription — ban "o giua"; number — trung vi; ranking — Borda.
                   Voi cac cong cu nay mot nguoi KHOP khi ket qua cua ho du gan ket qua gop (F1 / CER /
                   sai so / Kendall), va cong cu TRANH CHAP khi khong qua mot nua so nguoi khop.
  freeText       — cau tra loi mo: CHI gop khi noi goi dua ThamSoGop.mo (cham ca du an, 3.19 — app/cau_mo.py:
                   cau "o giua" theo do giong nghia / chu); khong co thi khong gop, khong tinh vao dong thuan.

Ket qua:
  agreed        — moi cong cu gop duoc deu co ket qua duoc qua nua so nguoi ung ho;
  disputed      — co cong cu tranh chap;
  notApplicable — tap nhan khong co cong cu nao gop duoc (chi toan freeText).
"""

from dataclasses import dataclass, field
from typing import Any

from app import cau_mo, gop


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


@dataclass(frozen=True)
class ThamSoMo:
    """Cham cau tra loi mo (freeText) cua MOT cau hoi."""

    phuong_phap: str                             # cau_mo.THEO_NGHIA | cau_mo.THEO_CHU
    nguong: float
    vector: dict[tuple[str, str], list[float]] = field(default_factory=dict)   # (annotation_id, ten cong cu) → vector


@dataclass(frozen=True)
class ThamSoGop:
    """Tham so gop cho cong cu khong roi rac (doc tu setting + metadata cua mau)."""

    mau: dict[str, Any] | None = None           # metadata mau: width / height cho diem moc, duong gap khuc
    nguong: dict[str, float] = field(default_factory=dict)   # loai cong cu → setting labeling.threshold.*
    f1_toi_thieu: float = gop.F1_TOI_THIEU_MAC_DINH          # setting quality.consensus_min_f1
    mo: ThamSoMo | None = None                              # None = khong cham freeText


def danh_gia(label_schema: dict[str, Any], nhan: list[Nhan], tham_so: ThamSoGop | None = None) -> KetQua:
    """Tinh dong thuan tren cac nhan da nop cua mot mau."""
    ts = tham_so if tham_so is not None else ThamSoGop()
    cong_cu = [t for t in label_schema.get("tools", [])
               if gop.gop_duoc(t) or (t.get("kind") == "freeText" and ts.mo is not None)]

    if not cong_cu:
        return KetQua(status="notApplicable", final=None, agrees={n.annotation_id: None for n in nhan})

    final: dict[str, Any] = {}
    # ten cong cu → {annotation_id → khop ket qua gop cua cong cu do?}; chi cong cu KHONG tranh chap.
    khop_theo_cong_cu: dict[str, dict[str, bool]] = {}
    tranh_chap = False

    for t in cong_cu:
        ten = t["name"]
        co_lam = [n for n in nhan if ten in n.data]
        if not co_lam:
            # Cong cu tuy chon khong ai lam — khong co gi de gop, khong phai tranh chap.
            continue

        if t["kind"] == "freeText":
            mo = ts.mo
            vec = None if mo.phuong_phap != cau_mo.THEO_NGHIA else [mo.vector.get((n.annotation_id, ten)) for n in co_lam]
            kq = cau_mo.gop_mo([cau_mo.van_ban(n.data[ten]) for n in co_lam], vec, mo.nguong)
        else:
            kq = gop.gop_cong_cu(t, [n.data[ten] for n in co_lam], ts.mau, ts.nguong.get(t["kind"]), ts.f1_toi_thieu)
        final[ten] = kq["final"]
        if kq["disputed"]:
            tranh_chap = True
        else:
            khop_theo_cong_cu[ten] = {n.annotation_id: dong_y for n, dong_y in zip(co_lam, kq["agrees"])}

    # Mot nhan khop khi khop MOI cong cu da co ket qua ma nguoi do co lam; cong cu tranh chap khong cham.
    agrees: dict[str, bool | None] = {}
    for n in nhan:
        cham = [bang[n.annotation_id] for bang in khop_theo_cong_cu.values() if n.annotation_id in bang]
        agrees[n.annotation_id] = all(cham) if cham else None

    return KetQua(status="disputed" if tranh_chap else "agreed", final=final, agrees=agrees)
