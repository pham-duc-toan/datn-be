"""Chong gian lan (FM-07): phat hien tu attempt.submitted (nop CA BAI), mo HO SO NGHI VAN cho admin.

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

from app import settings_store
from app.contracts import AttemptSubmitted

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
# Ghi luot + xet (chay trong transaction consumer, khong commit)
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


async def xet_bai(s: AsyncSession, p: AttemptSubmitted) -> None:
    """Ghi dau vet bai nop roi xet (goi tu processors.attempt_submitted, trong transaction consumer)."""
    du_an = (await s.execute(text("SELECT label_schema FROM projects WHERE project_id = :p"), {"p": p.project_id})).first()
    schema = {} if du_an is None else (du_an.label_schema if isinstance(du_an.label_schema, dict) else json.loads(du_an.label_schema))
    dap_an = [None if a.label_payload is None else dap_an_phan_loai(schema, a.label_payload.data) for a in p.answers]
    so_cau = sum(1 for a in p.answers if a.label_payload is not None)
    giay_moi_cau = max(0.0, (p.submitted_at - p.started_at).total_seconds()) / max(so_cau, 1)
    lop, dai = chuoi_dai_nhat(dap_an)

    moi = (await s.execute(
        text(
            "INSERT INTO fraud_submissions (assignment_id, project_id, task_id, labeler_id, work_seconds, answer, client_ip, device_id, submitted_at) "
            "VALUES (:a, :p, NULL, :l, :g, :ans, :ip, :dev, :at) ON CONFLICT (assignment_id) DO NOTHING RETURNING assignment_id"),
        {"a": p.attempt_id, "p": p.project_id, "l": p.labeler_id, "g": giay_moi_cau,
         "ans": lop, "ip": p.client_ip, "dev": p.device_id, "at": p.submitted_at},
    )).first()
    if moi is None or not settings_store.dung_sai(settings_store.FRAUD_ENABLED):
        return

    await _xet_qua_nhanh(s, p, giay_moi_cau, so_cau)
    await _xet_mot_nhan(s, p, schema, lop, dai)
    ip = ip_de_xet(p.client_ip, settings_store.dung_sai(settings_store.FRAUD_IGNORE_PRIVATE_IPS))
    if ip is not None:
        await _xet_chung(s, "chung_ip", "client_ip", ip, p.submitted_at)
    if p.device_id:
        await _xet_chung(s, "chung_thiet_bi", "device_id", p.device_id, p.submitted_at)


async def _xet_qua_nhanh(s: AsyncSession, p: AttemptSubmitted, giay_moi_cau: float, so_cau: int) -> None:
    toi_thieu_cau = settings_store.so_nguyen(settings_store.FRAUD_FAST_WINDOW)
    if so_cau < max(toi_thieu_cau, 1):
        return

    khac = [r.work_seconds for r in (await s.execute(
        text("SELECT work_seconds FROM fraud_submissions WHERE project_id = :p AND assignment_id <> :a "
             "ORDER BY submitted_at DESC LIMIT :n"),
        {"p": p.project_id, "a": p.attempt_id, "n": SO_LUOT_TINH_TRUNG_VI})).all()]
    trung_vi = median(khac) if khac else None
    nguong = nguong_qua_nhanh(trung_vi, settings_store.so_thuc(settings_store.FRAUD_MIN_SECONDS),
                              settings_store.so_thuc(settings_store.FRAUD_FAST_RATIO))
    if giay_moi_cau >= nguong:
        return

    await mo_ho_so(s, "qua_nhanh", f"qua_nhanh:{p.labeler_id}:{p.project_id}", p.project_id, [p.labeler_id], {
        "attemptId": str(p.attempt_id),
        "answeredCount": so_cau,
        "secondsPerQuestion": round(giay_moi_cau, 3),
        "thresholdSeconds": round(nguong, 3),
        "projectMedianSeconds": None if trung_vi is None else round(trung_vi, 3),
    }, p.submitted_at)


async def _xet_mot_nhan(s: AsyncSession, p: AttemptSubmitted, schema: dict[str, Any], lop: str | None, dai: int) -> None:
    do_dai = settings_store.so_nguyen(settings_store.FRAUD_SAME_LABEL_STREAK)
    if lop is None or do_dai <= 0 or dai < do_dai:
        return

    # Nguoi khac trong du an chon bao nhieu dap an khac nhau (tu nhan da luu cua cac bai khac).
    du_lieu = [r.data for r in (await s.execute(
        text("SELECT data FROM labels WHERE project_id = :p AND labeler_id <> :l LIMIT :n"),
        {"p": p.project_id, "l": p.labeler_id, "n": SO_LUOT_TINH_TRUNG_VI})).all()]
    khac = len({a for a in (dap_an_phan_loai(schema, d if isinstance(d, dict) else json.loads(d)) for d in du_lieu) if a is not None})
    if chuoi_mot_nhan([lop] * dai, do_dai, khac) is None:
        return

    await mo_ho_so(s, "mot_nhan_lien_tuc", f"mot_nhan:{p.labeler_id}:{p.project_id}", p.project_id, [p.labeler_id], {
        "attemptId": str(p.attempt_id),
        "streak": dai,
        "answer": lop,
        "otherLabelersDistinctAnswers": khac,
    }, p.submitted_at)


async def _xet_chung(s: AsyncSession, loai: str, cot: str, gia_tri: str, luc: datetime) -> None:
    tu = luc - timedelta(seconds=settings_store.giay(settings_store.FRAUD_SHARED_WINDOW))
    # cot chi la "client_ip" / "device_id" (hang so trong code), khong tu nguoi dung.
    nguoi = [r.labeler_id for r in (await s.execute(
        text(f"SELECT DISTINCT labeler_id FROM fraud_submissions WHERE {cot} = :v AND submitted_at > :tu"),
        {"v": gia_tri, "tu": tu})).all()]
    if len(nguoi) < settings_store.so_nguyen(settings_store.FRAUD_SHARED_ACCOUNTS):
        return

    await mo_ho_so(s, loai, f"{loai}:{gia_tri}", None, nguoi, {
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
