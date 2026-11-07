"""Chong gian lan (FM-07): xet tung bai nop (nop CA BAI), mo HO SO NGHI VAN cho admin.

Worker viec_cho.py goi xet_bai cho moi dong pending_attempts. Dau vet bai nop (thoi gian, IP, thiet bi, dap an)
KHONG chep ve quality (kien truc 3.22): doc thang tu project-svc qua gRPC — bai nop + nhan cua bai, bai khac
trong du an (trung vi thoi gian, dap an nguoi khac), ai nop tu cung IP / thiet bi.

Ba tin hieu (nguong la setting he thong quality.fraud_*):
  - qua_nhanh: so giay trung binh moi cau cua bai nhanh hon max(so giay toi thieu, ti le x trung vi
    cua cac bai khac trong du an) — bai co it nhat quality.fraud_fast_window cau da tra loi moi xet;
  - mot_nhan_lien_tuc: K cau phan loai lien tiep (theo thu tu de) cung mot lop, trong khi nguoi khac
    trong du an chon da dang (du lieu that su dong deu thi khong bao);
  - chung_ip / chung_thiet_bi: nhieu tai khoan khac nhau nop tu cung IP / ma thiet bi trong mot
    khoang thoi gian.

He thong KHONG tu khoa ai: mo / cap nhat ho so, admin xem bang chung roi bo qua hoac xac nhan
(xac nhan kem khoa → fraud.case_confirmed → identity khoa). Ho so gop trung theo case_key (moi khoa
toi da mot ho so dang mo); ho so vua xu ly thi khong mo lai cung khoa trong thoi gian cho.

Cac ham la_* / chuoi_* / dap_an_* la ham THUAN (test khong can DB).
"""

import ipaddress
import json
import uuid
from datetime import datetime, timedelta
from statistics import median
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app import project_client, settings_store
from app.project_client import Bai, DuAn

SO_LUOT_TINH_TRUNG_VI = 500


# =====================================================================
# Ham thuan
# =====================================================================


def nguong_qua_nhanh(trung_vi_du_an: float | None, toi_thieu_giay: float, ti_le: float) -> float:
    """Lam nhanh hon nguong nay la qua nhanh: max(toi thieu, ti le x trung vi du an)."""
    if trung_vi_du_an is None:
        return toi_thieu_giay
    return max(toi_thieu_giay, ti_le * trung_vi_du_an)


def la_qua_nhanh(thoi_gian_lam: list[float], cua_so: int, nguong_giay: float, ti_le_toi_thieu: float) -> bool:
    """Du cua so N luot va ti le luot nhanh hon nguong >= ti_le_toi_thieu."""
    if len(thoi_gian_lam) < cua_so or cua_so <= 0:
        return False
    gan_nhat = thoi_gian_lam[:cua_so]
    so_nhanh = sum(1 for t in gan_nhat if t < nguong_giay)
    return so_nhanh / cua_so >= ti_le_toi_thieu


def chuoi_mot_nhan(dap_an_gan_nhat: list[str | None], do_dai: int, so_dap_an_khac_cua_nguoi_khac: int) -> str | None:
    """K dap an gan nhat deu giong nhau (va nguoi khac chon da dang) → tra lop do; khong thi None."""
    if do_dai <= 0 or len(dap_an_gan_nhat) < do_dai or so_dap_an_khac_cua_nguoi_khac < 2:
        return None
    cua_so = dap_an_gan_nhat[:do_dai]
    dau = cua_so[0]
    if dau is None or any(a != dau for a in cua_so):
        return None
    return dau


def ip_de_xet(ip: str | None, bo_qua_noi_bo: bool) -> str | None:
    """IP dung de xet chung IP; None neu khong co / khong hop le / noi bo (khi bo qua noi bo)."""
    if not ip:
        return None
    try:
        dia_chi = ipaddress.ip_address(ip)
    except ValueError:
        return None
    if bo_qua_noi_bo and (dia_chi.is_private or dia_chi.is_loopback or dia_chi.is_link_local):
        return None
    return str(dia_chi)


def dap_an_phan_loai(label_schema: dict[str, Any], du_lieu: dict[str, Any]) -> str | None:
    """Dap an cua cong cu PHAN LOAI dau tien trong tap nhan (cac lop sap xep, noi bang ',')."""
    for cong_cu in label_schema.get("tools", []):
        if cong_cu.get("kind") != "classification":
            continue
        gia_tri = du_lieu.get(cong_cu.get("name"))
        if not isinstance(gia_tri, dict) or not gia_tri.get("labelIds"):
            return None
        return ",".join(sorted(str(x) for x in gia_tri["labelIds"]))
    return None


# =====================================================================
# Xet mot bai (chay trong transaction cua worker, khong commit)
# =====================================================================


def chuoi_dai_nhat(dap_an: list[str | None]) -> tuple[str | None, int]:
    """(dap an, do dai) cua chuoi lien tiep dai nhat cung mot dap an (bo qua None)."""
    tot, dai = None, 0
    hien, dem = None, 0
    for a in dap_an:
        if a is not None and a == hien:
            dem += 1
        else:
            hien, dem = a, (1 if a is not None else 0)
        if dem > dai:
            tot, dai = hien, dem
    return tot, dai


async def xet_bai(s: AsyncSession, attempt_id: uuid.UUID, project_id: uuid.UUID) -> None:
    """Xet mot bai da nop (trong transaction cua worker, khong commit). Loi gRPC → nem ra, vong sau xet lai."""
    if not settings_store.dung_sai(settings_store.FRAUD_ENABLED):
        return

    du_an = await project_client.du_an(project_id)
    bai = await project_client.bai_da_nop(project_id)
    b = next((x for x in bai if x.attempt_id == attempt_id), None)
    if b is None:
        return  # khong con la bai da nop (khong xay ra binh thuong) — khong co gi de xet

    nhan = {str(n.sample_id): n.data for n in await project_client.nhan(project_id, [attempt_id])}
    dap_an = [None if sm not in nhan else dap_an_phan_loai(du_an.label_schema, nhan[sm]) for sm in b.sample_ids]
    lop, dai = chuoi_dai_nhat(dap_an)

    await _xet_qua_nhanh(s, b, [x for x in bai if x.attempt_id != attempt_id], project_id)
    await _xet_mot_nhan(s, b, du_an, lop, dai)
    ip = ip_de_xet(b.client_ip, settings_store.dung_sai(settings_store.FRAUD_IGNORE_PRIVATE_IPS))
    if ip is not None:
        await _xet_chung(s, "chung_ip", "client_ip", ip, b.submitted_at)
    if b.device_id:
        await _xet_chung(s, "chung_thiet_bi", "device_id", b.device_id, b.submitted_at)


async def _xet_qua_nhanh(s: AsyncSession, b: Bai, khac: list[Bai], project_id: uuid.UUID) -> None:
    toi_thieu_cau = settings_store.so_nguyen(settings_store.FRAUD_FAST_WINDOW)
    if b.label_count < max(toi_thieu_cau, 1):
        return

    gan_day = sorted(khac, key=lambda x: x.submitted_at, reverse=True)[:SO_LUOT_TINH_TRUNG_VI]
    trung_vi = median([x.giay_moi_cau for x in gan_day]) if gan_day else None
    nguong = nguong_qua_nhanh(trung_vi, settings_store.so_thuc(settings_store.FRAUD_MIN_SECONDS),
                              settings_store.so_thuc(settings_store.FRAUD_FAST_RATIO))
    if b.giay_moi_cau >= nguong:
        return

    await mo_ho_so(s, "qua_nhanh", f"qua_nhanh:{b.labeler_id}:{project_id}", project_id, [b.labeler_id], {
        "attemptId": str(b.attempt_id),
        "answeredCount": b.label_count,
        "secondsPerQuestion": round(b.giay_moi_cau, 3),
        "thresholdSeconds": round(nguong, 3),
        "projectMedianSeconds": None if trung_vi is None else round(trung_vi, 3),
    }, b.submitted_at)


async def _xet_mot_nhan(s: AsyncSession, b: Bai, du_an: DuAn, lop: str | None, dai: int) -> None:
    do_dai = settings_store.so_nguyen(settings_store.FRAUD_SAME_LABEL_STREAK)
    if lop is None or do_dai <= 0 or dai < do_dai:
        return

    # Nguoi khac trong du an chon bao nhieu dap an khac nhau — doc dan nhan cua du an, du mau thi dung stream.
    khac: set[str] = set()
    da_doc = 0
    async for n in project_client.tung_nhan(du_an.project_id):
        if n.labeler_id == b.labeler_id:
            continue
        a = dap_an_phan_loai(du_an.label_schema, n.data)
        if a is not None:
            khac.add(a)
        da_doc += 1
        if da_doc >= SO_LUOT_TINH_TRUNG_VI:
            break
    if chuoi_mot_nhan([lop] * dai, do_dai, len(khac)) is None:
        return

    await mo_ho_so(s, "mot_nhan_lien_tuc", f"mot_nhan:{b.labeler_id}:{du_an.project_id}", du_an.project_id, [b.labeler_id], {
        "attemptId": str(b.attempt_id),
        "streak": dai,
        "answer": lop,
        "otherLabelersDistinctAnswers": len(khac),
    }, b.submitted_at)


async def _xet_chung(s: AsyncSession, loai: str, cot: str, gia_tri: str, luc: datetime) -> None:
    tu = luc - timedelta(seconds=settings_store.giay(settings_store.FRAUD_SHARED_WINDOW))
    if cot == "client_ip":
        nguoi = await project_client.nguoi_nop_chung(client_ip=gia_tri, tu=tu)
    else:
        nguoi = await project_client.nguoi_nop_chung(device_id=gia_tri, tu=tu)
    if len(nguoi) < settings_store.so_nguyen(settings_store.FRAUD_SHARED_ACCOUNTS):
        return

    await mo_ho_so(s, loai, f"{loai}:{gia_tri}", None, sorted(nguoi, key=str), {
        cot: gia_tri,
        "accounts": len(nguoi),
        "windowSeconds": settings_store.giay(settings_store.FRAUD_SHARED_WINDOW),
    }, luc)


async def mo_ho_so(s: AsyncSession, loai: str, khoa: str, project_id: uuid.UUID | None,
                   user_ids: list[uuid.UUID], bang_chung: dict[str, Any], luc: datetime) -> None:
    """Mo ho so moi, hoac cap nhat ho so DANG MO cung khoa (gop nguoi dung, thay bang chung).
    Ho so cung khoa vua xu ly trong thoi gian cho → khong mo lai."""
    cho = settings_store.giay(settings_store.FRAUD_REOPEN_COOLDOWN)
    vua_xu_ly = (await s.execute(
        text("SELECT 1 FROM fraud_cases WHERE case_key = :k AND status <> 'open' AND resolved_at > :tu LIMIT 1"),
        {"k": khoa, "tu": luc - timedelta(seconds=cho)})).first()
    if vua_xu_ly is not None:
        return

    await s.execute(
        text(
            "INSERT INTO fraud_cases (id, type, case_key, project_id, user_ids, evidence, status, created_at, updated_at) "
            "VALUES (:id, :type, :k, :p, :u, CAST(:e AS jsonb), 'open', :at, :at) "
            "ON CONFLICT (case_key) WHERE status = 'open' DO UPDATE SET "
            "  user_ids = ARRAY(SELECT DISTINCT x FROM unnest(fraud_cases.user_ids || EXCLUDED.user_ids) AS x), "
            "  evidence = EXCLUDED.evidence, updated_at = EXCLUDED.updated_at"),
        {"id": uuid.uuid4(), "type": loai, "k": khoa, "p": project_id, "u": list(user_ids),
         "e": json.dumps(bang_chung), "at": luc},
    )
