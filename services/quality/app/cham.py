"""Cham CA DU AN (docs 3.18) — logic thuan, khong DB, de test.

Sau khi task-svc dong thu bai (project.collection_finished), moi bai da nop duoc cham MOT lan:

  1. Voi tung cau THAT (khong phai cau vang, khong bi loai): gop cau tra loi cua MOI bai da nop theo
     tung cong cu (consensus.danh_gia — cung thuat toan gop voi annotation-svc). Cau co "da so" khi it
     nhat mot cong cu gop ra ket qua duoc qua nua so nguoi tra loi ung ho, va co tu SO_NGUOI_TOI_THIEU_MOT_CAU
     nguoi tra loi tro len. Cau khong co da so (hoa phieu, chi toan freeText) KHONG tinh cho ai.
  2. Moi bai: so cau so duoc = cau co da so ma bai co trong de; so cau giong = cau ma nhan cua bai khop
     ket qua gop o MOI cong cu da co ket qua ma nguoi do lam. Cau bo trong = so duoc nhung khong giong.
     Cau tra loi mo (freeText) so THEO NGHIA khi co ThamSoCham.mo (3.19 — app/cau_mo.py).
     Ti le = floor(100 x giong / so duoc); khong co cau nao so duoc → 100.
  3. Dat = ti le >= nguong t cua du an VA dung het cau vang.
  4. n suat thuong (chon_duoc_thuong): n bai DAT nop som nhat. Du an bat "lap suat trong" thi suat con lai
     trao cho bai chua dat nhung dung het cau vang va giong da so >= san, % cao truoc, cung % thi nop som truoc.

It hon SO_BAI_TOI_THIEU bai nop thi khong co "da so" dang tin: chi xet cau vang (majority_used = False).
"""

import math
from dataclasses import dataclass, field
from typing import Any

from app import consensus, gop

SO_BAI_TOI_THIEU = 3
SO_NGUOI_TOI_THIEU_MOT_CAU = 2


@dataclass(frozen=True)
class Bai:
    attempt_id: str
    labeler_id: str
    cau_hoi: tuple[str, ...]          # sample_id cac cau THAT trong de cua bai (ca cau bo trong)
    gold_total: int
    gold_correct: int


@dataclass(frozen=True)
class KetQuaBai:
    attempt_id: str
    labeler_id: str
    so_duoc: int
    giong: int
    phan_tram: int
    gold_total: int
    gold_correct: int
    dat: bool
    da_tra_loi: int


def chon_duoc_thuong(bai: list[KetQuaBai], so_suat: int, san_lap_suat: int | None = None) -> set[str]:
    """n suat thuong cho n bai DAT nop som nhat. `bai` da xep theo luc nop (som truoc) — thu tu cua cham_du_an.

    san_lap_suat (lap suat trong): it bai dat hon so suat thi lap phan con lai bang bai CHUA dat ma van dung het
    cau vang va giong da so >= san — % cao truoc, cung % thi bai nop som hon. None = khong lap."""
    ra: set[str] = set()
    for b in bai:
        if len(ra) >= so_suat:
            break
        if b.dat:
            ra.add(b.attempt_id)
    if san_lap_suat is None or len(ra) >= so_suat:
        return ra

    # sorted on dinh: cung % giu thu tu nop cua `bai`.
    ung_vien = sorted(
        (b for b in bai if not b.dat and dung_het_vang(b.gold_correct, b.gold_total) and b.phan_tram >= san_lap_suat),
        key=lambda b: -b.phan_tram)
    for b in ung_vien:
        if len(ra) >= so_suat:
            break
        ra.add(b.attempt_id)
    return ra


@dataclass(frozen=True)
class KetQuaCau:
    sample_id: str
    status: str                       # agreed | disputed | notApplicable
    final: dict[str, Any] | None
    so_nhan: int


@dataclass
class KetQuaDuAn:
    dung_da_so: bool
    bai: list[KetQuaBai] = field(default_factory=list)
    cau: list[KetQuaCau] = field(default_factory=list)


def ti_le(giong: int, so_duoc: int) -> int:
    return 100 if so_duoc <= 0 else math.floor(100 * giong / so_duoc)


def dung_het_vang(gold_correct: int, gold_total: int) -> bool:
    return gold_correct >= gold_total


@dataclass(frozen=True)
class ThamSoMoDuAn:
    """Cham freeText ca du an: phuong phap, nguong, vector (attempt_id, sample_id, ten cong cu) → vector."""

    phuong_phap: str
    nguong: float
    vector: dict[tuple[str, str, str], list[float]] = field(default_factory=dict)


def cham_du_an(label_schema: dict[str, Any], bai: list[Bai], nhan: dict[tuple[str, str], dict[str, Any]],
               metadata_mau: dict[str, dict[str, Any] | None], loai_tru: set[str], nguong: int,
               tham_so: consensus.ThamSoGop | None = None, mo: ThamSoMoDuAn | None = None) -> KetQuaDuAn:
    """
    nhan: (attempt_id, sample_id) → du lieu nhan (chi cau DA tra loi).
    metadata_mau: sample_id → metadata (kich thuoc anh...) cho cong cu so do lech theo duong cheo.
    mo: None = khong cham freeText (cau chi co freeText khong tinh cho ai).
    """
    tra_loi: dict[str, int] = {b.attempt_id: sum(1 for c in b.cau_hoi if (b.attempt_id, c) in nhan) for b in bai}

    if len(bai) < SO_BAI_TOI_THIEU:
        return KetQuaDuAn(dung_da_so=False, bai=[
            KetQuaBai(b.attempt_id, b.labeler_id, 0, 0, 100, b.gold_total, b.gold_correct,
                      dung_het_vang(b.gold_correct, b.gold_total), tra_loi[b.attempt_id])
            for b in bai
        ])

    so_duoc = {b.attempt_id: 0 for b in bai}
    giong = {b.attempt_id: 0 for b in bai}
    ket_qua_cau: list[KetQuaCau] = []

    tat_ca_cau = sorted({c for b in bai for c in b.cau_hoi} - loai_tru)
    vector_theo_cau: dict[str, dict[tuple[str, str], list[float]]] = {}
    if mo is not None:
        for (a, sm, ten), v in mo.vector.items():
            vector_theo_cau.setdefault(sm, {})[(a, ten)] = v
    for mau in tat_ca_cau:
        co_cau = [b for b in bai if mau in b.cau_hoi]
        da_lam = [consensus.Nhan(b.attempt_id, b.labeler_id, nhan[(b.attempt_id, mau)])
                  for b in co_cau if (b.attempt_id, mau) in nhan]
        if not da_lam:
            continue

        ts = consensus.ThamSoGop(
            mau=metadata_mau.get(mau),
            nguong=tham_so.nguong if tham_so is not None else {},
            f1_toi_thieu=tham_so.f1_toi_thieu if tham_so is not None else gop.F1_TOI_THIEU_MAC_DINH,
            mo=None if mo is None else consensus.ThamSoMo(mo.phuong_phap, mo.nguong, vector_theo_cau.get(mau, {})))
        # Tran = so nguoi da lam: cham mot lan, khong xin them nguoi (khong con redundancy thich ung).
        kq = consensus.danh_gia(label_schema, da_lam, len(da_lam), len(da_lam), consensus.ChinhSach(), ts)
        co_da_so = len(da_lam) >= SO_NGUOI_TOI_THIEU_MOT_CAU and any(v is not None for v in kq.agrees.values())
        trang_thai = kq.status if co_da_so or kq.status == "notApplicable" else "disputed"
        ket_qua_cau.append(KetQuaCau(mau, trang_thai, kq.final if co_da_so else None, len(da_lam)))
        if not co_da_so:
            continue

        for b in co_cau:
            so_duoc[b.attempt_id] += 1
            if kq.agrees.get(b.attempt_id) is True:
                giong[b.attempt_id] += 1

    kq_bai = []
    for b in bai:
        pt = ti_le(giong[b.attempt_id], so_duoc[b.attempt_id])
        kq_bai.append(KetQuaBai(
            b.attempt_id, b.labeler_id, so_duoc[b.attempt_id], giong[b.attempt_id], pt, b.gold_total, b.gold_correct,
            pt >= nguong and dung_het_vang(b.gold_correct, b.gold_total), tra_loi[b.attempt_id]))
    return KetQuaDuAn(dung_da_so=True, bai=kq_bai, cau=ket_qua_cau)
