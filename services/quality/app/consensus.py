"""Dong thuan cho MOT task — logic thuan, khong DB, de test.

Moi cong cu cua tap nhan duoc GOP theo cach cua loai cong cu do (app/gop.py — ban Python cua
Crowd.Labeling ben C#):
  classification — moi lop xet doc lap, lop duoc chon khi HON MOT NUA so nguoi tra loi cong cu do chon no;
  pairwise       — lua chon a / b / tie qua ban;
  bbox, polygon, keypoint, polyline, mask, temporalSegment, track — ghep cum cac doi tuong "cung mot
                   vat", giu cum co hon mot nua so nguoi; span, relation — phan tu duoc qua nua so
                   nguoi ghi y het; transcription — ban "o giua"; number — trung vi; ranking — Borda.
                   Voi cac cong cu nay mot nguoi KHOP khi ket qua cua ho du gan ket qua gop (F1 / CER /
                   sai so / Kendall), va cong cu TRANH CHAP khi khong qua mot nua so nguoi khop.
  freeText       — cau tra loi mo: khong gop, khong tinh vao dong thuan.

Ket qua:
  agreed        — moi cong cu gop duoc deu co ket qua duoc qua nua so nguoi ung ho;
  disputed      — co cong cu tranh chap;
  notApplicable — tap nhan khong co cong cu nao gop duoc (chi toan freeText).
Tranh chap ma chua cham tran redundancy → xin them mot nguoi (vong lap thich ung).

Chinh sach dung (setting quality.redundancy_policy, NC-D-01 — xem redundancy.py):
  majority  (mac dinh) — nhu tren;
  posterior / voi      — voi cong cu CHON MOT (phan loai mot lop, so sanh cap): dap an la
                         lua chon co HAU NGHIEM cao nhat (co trong so theo do chinh xac tung
                         labeler), dung theo nguong / gia tri thong tin. Moi cong cu khac van gop
                         nhu majority.
"""

from dataclasses import dataclass, field
from typing import Any

from app import gop, redundancy


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


@dataclass(frozen=True)
class ChinhSach:
    """Tham so cua chinh sach dung (doc tu setting + do chinh xac labeler)."""

    ten: str = "majority"                       # majority | posterior | voi
    nguong: float = 0.95                        # posterior
    ti_le_gia_tri: float = 20.0                 # voi
    do_chinh_xac: dict[str, float] = field(default_factory=dict)   # labeler_id → do chinh xac uoc luong
    mac_dinh: float = 0.7                       # labeler chua co bang chung; cung la a_tiep cua voi
    tran: int = 0                               # voi: tran redundancy (danh_gia tu dien)


@dataclass(frozen=True)
class ThamSoGop:
    """Tham so gop cho cong cu khong roi rac (doc tu setting + metadata cua mau)."""

    mau: dict[str, Any] | None = None           # metadata mau: width / height cho diem moc, duong gap khuc
    nguong: dict[str, float] = field(default_factory=dict)   # loai cong cu → setting labeling.threshold.*
    f1_toi_thieu: float = gop.F1_TOI_THIEU_MAC_DINH          # setting quality.consensus_min_f1


def _lop(ket_qua: Any) -> frozenset[str]:
    return frozenset(ket_qua.get("labelIds", [])) if isinstance(ket_qua, dict) else frozenset()


def _chon(ket_qua: Any) -> str | None:
    return ket_qua.get("choice") if isinstance(ket_qua, dict) else None


def _khop(kind: str, ket_qua: Any, final: dict[str, Any]) -> bool:
    if kind == "classification":
        return _lop(ket_qua) == frozenset(final["labelIds"])
    return _chon(ket_qua) == final["choice"]


def _lua_chon(t: dict[str, Any]) -> list[str] | None:
    """Tap lua chon cua cong cu CHON MOT; None neu cong cu chon nhieu (giu majority)."""
    if t["kind"] == "classification":
        return None if t.get("allowMultiple") else list(t.get("classes", []))
    return ["a", "b", "tie"] if t.get("allowTie") else ["a", "b"]


def _gop_theo_hau_nghiem(t: dict[str, Any], nhan: list[Nhan], cs: ChinhSach) -> tuple[dict[str, Any] | None, bool]:
    """
    (ket qua, du tin cay) cho mot cong cu chon mot theo chinh sach posterior / voi.
    Du tin cay: posterior — hau nghiem dan dau >= nguong; voi — mua them mot nhan khong con dang
    (gia tri thong tin x ti le gia tri < 1). Chua du thi tra None: noi goi xin them nguoi, hoac
    o tran thi de tranh chap cho nguoi duyet.
    """
    ten = t["name"]
    lua_chon = _lua_chon(t)
    gia_tri: list[str] = []
    do_cx: list[float] = []
    for n in nhan:
        if ten not in n.data:
            continue
        k = n.data[ten]
        l = next(iter(_lop(k)), None) if t["kind"] == "classification" else _chon(k)
        if l is None or l not in lua_chon:
            continue
        gia_tri.append(l)
        do_cx.append(cs.do_chinh_xac.get(n.labeler_id, cs.mac_dinh))

    if not gia_tri:
        return None, False

    hau = redundancy.hau_nghiem(gia_tri, do_cx, lua_chon)
    p_max = max(hau.values())
    dan_dau = [y for y, p in hau.items() if p == p_max]
    if len(dan_dau) != 1:
        return None, False

    if cs.ten == "voi":
        # Nhin truoc toi het so nhan con mua duoc (tran − da co).
        du = not redundancy.dang_mua_them(hau, cs.mac_dinh, cs.ti_le_gia_tri, max(0, cs.tran - len(gia_tri)))
    else:
        du = p_max >= cs.nguong
    if not du:
        return None, False

    y = dan_dau[0]
    return ({"labelIds": [y]} if t["kind"] == "classification" else {"choice": y}), True


def danh_gia(label_schema: dict[str, Any], nhan: list[Nhan], target: int, max_redundancy: int,
             chinh_sach: ChinhSach | None = None, tham_so: ThamSoGop | None = None) -> KetQua:
    """Tinh dong thuan tren cac nhan da nop cua mot task (da du `target` nguoi)."""
    cs = chinh_sach if chinh_sach is not None else ChinhSach()
    if cs.tran != max_redundancy:
        cs = ChinhSach(cs.ten, cs.nguong, cs.ti_le_gia_tri, cs.do_chinh_xac, cs.mac_dinh, max_redundancy)
    ts = tham_so if tham_so is not None else ThamSoGop()
    cong_cu = [t for t in label_schema.get("tools", []) if gop.gop_duoc(t)]

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

        if cs.ten != "majority" and t["kind"] in ("classification", "pairwise") and _lua_chon(t) is not None:
            ket_qua, du = _gop_theo_hau_nghiem(t, nhan, cs)
            final[ten] = ket_qua
            if not du:
                tranh_chap = True
            else:
                khop_theo_cong_cu[ten] = {n.annotation_id: _khop(t["kind"], n.data[ten], ket_qua) for n in co_lam}
            continue

        kq = gop.gop_cong_cu(t, [n.data[ten] for n in co_lam], ts.mau, ts.nguong.get(t["kind"]), ts.f1_toi_thieu)
        final[ten] = kq["final"]
        if kq["disputed"]:
            tranh_chap = True
        else:
            khop_theo_cong_cu[ten] = {n.annotation_id: dong_y for n, dong_y in zip(co_lam, kq["agrees"])}

    if tranh_chap and target < max_redundancy:
        return KetQua(status="disputed", final=final, xin_them=target + 1)

    # Mot nhan khop khi khop MOI cong cu da co ket qua ma nguoi do co lam; cong cu tranh chap khong cham.
    agrees: dict[str, bool | None] = {}
    for n in nhan:
        cham = [bang[n.annotation_id] for bang in khop_theo_cong_cu.values() if n.annotation_id in bang]
        agrees[n.annotation_id] = all(cham) if cham else None

    return KetQua(status="disputed" if tranh_chap else "agreed", final=final, agrees=agrees)
