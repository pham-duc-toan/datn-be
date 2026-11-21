"""Lan hoc moi dem cua mo hinh goi y (kien truc 3.39 muc 8.2) — phan IO + dieu phoi.

    ClickHouse database reco (user CHI DOC ml_reader) ──► du lieu hoc (dac trung luc tra feed + nhan)
        ──► hoc logistic (+ LightGBM neu du du lieu) ──► tot hon ban dang chay? ──► file mo hinh + manifest (MinIO)
    reco-svc tu doc manifest moi gio; ml-svc KHONG nam tren duong request, khong quyet dinh cham / tien.

Manifest (ml-models/reco/manifest.json):
    { "production": khoa|null, "production_version", "candidate": khoa|null, "candidate_version", "candidate_since" }
Moi lan chay: (1) danh gia A/B cua candidate dang chay (promote / drop / keep); (2) hoc ban moi — chi dat lam candidate khi
CHUA co candidate dang chay thu (khong reset A/B giua chung).
"""

from __future__ import annotations

import io
import json
import logging
from datetime import datetime, timezone

import httpx

from app import reco_hoc as hoc
from app.config import Settings

log = logging.getLogger("ml-svc.reco")

MANIFEST = "reco/manifest.json"

# Hanh vi + dac trung trong 90 ngay (TTL cua bang). Nhan "bat dau" = co attempt.started trong 24 gio sau luot tra the.
SQL_BAT_DAU = """
SELECT s.features AS f, max(st.t > s.event_time AND st.t < s.event_time + INTERVAL 1 DAY) AS y,
       toUnixTimestamp(s.event_time) AS t
FROM feed_served AS s
INNER JOIN (SELECT user_id, project_id, request_id FROM reco_events
            WHERE kind = 'impression' AND event_date >= today() - 90
            GROUP BY user_id, project_id, request_id) AS imp
    ON imp.user_id = s.user_id AND imp.project_id = s.project_id AND imp.request_id = s.request_id
LEFT JOIN (SELECT user_id, project_id, min(event_time) AS t FROM reco_events
           WHERE kind = 'start' AND event_date >= today() - 91 GROUP BY user_id, project_id) AS st
    ON st.user_id = s.user_id AND st.project_id = s.project_id
WHERE s.event_date >= today() - 90
GROUP BY s.event_id, s.features, s.event_time
FORMAT JSONEachRow
"""

# Nhan "dat" = project.graded bao bai dat; dac trung = luot tra the GAN NHAT truoc luc bat dau.
SQL_DAT = """
SELECT argMax(s.features, s.event_time) AS f, max(o.kind = 'pass') AS y, toUnixTimestamp(min(st.t)) AS t
FROM (SELECT user_id, project_id, min(event_time) AS t FROM reco_events
      WHERE kind = 'start' AND event_date >= today() - 90 GROUP BY user_id, project_id) AS st
INNER JOIN (SELECT user_id, project_id, kind FROM reco_events
            WHERE kind IN ('pass', 'fail') AND event_date >= today() - 90) AS o
    ON o.user_id = st.user_id AND o.project_id = st.project_id
INNER JOIN feed_served AS s ON s.user_id = st.user_id AND s.project_id = st.project_id
WHERE s.event_time <= st.t
GROUP BY st.user_id, st.project_id
FORMAT JSONEachRow
"""

SQL_DEM = """
SELECT countIf(kind = 'impression') AS hien, countIf(kind = 'start') AS bat_dau
FROM reco_events WHERE event_date >= today() - 90
FORMAT JSONEachRow
"""

# A/B: moi luot hien dau tien cua (nguoi, du an) theo nhom; co bat dau trong 24 gio khong.
SQL_AB = """
SELECT i.ab_group AS nhom, count() AS hien, countIf(st.t > i.t AND st.t < i.t + INTERVAL 1 DAY) AS bat_dau
FROM (SELECT user_id, project_id, any(ab_group) AS ab_group, min(event_time) AS t FROM reco_events
      WHERE kind = 'impression' AND event_time >= parseDateTimeBestEffort({tu:String})
        AND model_version IN ({ban_chay:String}, {ban_thu:String})
      GROUP BY user_id, project_id) AS i
LEFT JOIN (SELECT user_id, project_id, min(event_time) AS t FROM reco_events
           WHERE kind = 'start' GROUP BY user_id, project_id) AS st
    ON st.user_id = i.user_id AND st.project_id = i.project_id
GROUP BY i.ab_group
FORMAT JSONEachRow
"""


class ClickHouseDoc:
    """Goi ClickHouse qua HTTP (khong can driver) bang user CHI DOC."""

    def __init__(self, url: str, user: str, mat_khau: str, database: str = "reco"):
        self._url = url.rstrip("/")
        self._auth = (user, mat_khau)
        self._db = database

    def truy_van(self, sql: str, tham_so: dict | None = None) -> list[dict]:
        params = {"database": self._db}
        for k, v in (tham_so or {}).items():
            params["param_" + k] = v
        r = httpx.post(self._url + "/", params=params, content=sql.encode("utf-8"), auth=self._auth, timeout=300)
        r.raise_for_status()
        return [json.loads(dong) for dong in r.text.splitlines() if dong.strip()]


class KhoMoHinh:
    """File mo hinh + manifest tren MinIO (bucket ml-models, tien to reco/)."""

    def __init__(self, endpoint: str, access_key: str, secret_key: str, bucket: str = "ml-models"):
        from minio import Minio

        self._mc = Minio(endpoint, access_key=access_key, secret_key=secret_key, secure=False)
        self._bucket = bucket

    def doc_json(self, khoa: str) -> dict | None:
        from minio.error import S3Error

        try:
            r = self._mc.get_object(self._bucket, khoa)
        except S3Error as ex:
            if ex.code in ("NoSuchKey", "NoSuchObject"):
                return None
            raise
        try:
            return json.loads(r.read().decode("utf-8"))
        finally:
            r.close()
            r.release_conn()

    def ghi_json(self, khoa: str, du_lieu: dict) -> None:
        b = json.dumps(du_lieu, ensure_ascii=False).encode("utf-8")
        self._mc.put_object(self._bucket, khoa, io.BytesIO(b), len(b), content_type="application/json")


def _dong(rows: list[dict]) -> list[tuple[list[float], int, float]]:
    return [([float("nan") if v is None else float(v) for v in r["f"]], int(r["y"]), float(r["t"])) for r in rows]


def _bay_gio() -> datetime:
    return datetime.now(timezone.utc)


def danh_gia_ab(ch: ClickHouseDoc, kho: KhoMoHinh, manifest: dict, bay_gio: datetime) -> str | None:
    """Quyet dinh A/B cho candidate dang chay. Tra ve quyet dinh (None = khong co candidate)."""
    if not manifest.get("candidate"):
        return None
    tu = manifest.get("candidate_since") or bay_gio.isoformat()
    so_ngay = (bay_gio - datetime.fromisoformat(tu)).days
    rows = ch.truy_van(SQL_AB, {"tu": tu, "ban_chay": manifest.get("production_version") or "g1",
                                "ban_thu": manifest.get("candidate_version") or ""})
    nhom = {r["nhom"]: hoc.NhomAb(int(r["hien"]), int(r["bat_dau"])) for r in rows}
    quyet = hoc.quyet_dinh_ab(nhom.get("control", hoc.NhomAb(0, 0)), nhom.get("treatment", hoc.NhomAb(0, 0)), so_ngay)
    log.info("A/B mo hinh goi y %s sau %s ngay: %s (%s)", manifest.get("candidate_version"), so_ngay, quyet,
             {k: (v.luot_hien, v.bat_dau) for k, v in nhom.items()})
    if quyet == "promote":
        manifest["production"] = manifest["candidate"]
        manifest["production_version"] = manifest["candidate_version"]
    if quyet in ("promote", "drop"):
        manifest["candidate"] = None
        manifest["candidate_version"] = None
        manifest["candidate_since"] = None
        kho.ghi_json(MANIFEST, manifest)
    return quyet


def chay_mot_lan(cfg: Settings) -> dict:
    """Mot lan hoc (worker moi dem + POST /internal/reco/train). Tra tom tat de log / E2E."""
    if not cfg.reco_clickhouse_url or not cfg.minio_endpoint:
        return {"status": "disabled"}

    ch = ClickHouseDoc(cfg.reco_clickhouse_url, cfg.reco_clickhouse_user, cfg.reco_clickhouse_password)
    kho = KhoMoHinh(cfg.minio_endpoint, cfg.minio_access_key, cfg.minio_secret_key, cfg.model_bucket)
    bay_gio = _bay_gio()
    manifest = kho.doc_json(MANIFEST) or {"production": None, "production_version": "g1", "candidate": None,
                                           "candidate_version": None, "candidate_since": None}

    quyet_ab = danh_gia_ab(ch, kho, manifest, bay_gio)

    dem = ch.truy_van(SQL_DEM)
    so_hien = int(dem[0]["hien"]) if dem else 0
    so_bat_dau = int(dem[0]["bat_dau"]) if dem else 0
    cho_lgbm = hoc.nen_hoc_lightgbm(so_hien, so_bat_dau) or cfg.reco_force_lightgbm

    du_start = hoc.DuLieu.tu_dong(_dong(ch.truy_van(SQL_BAT_DAU)))
    du_pass = hoc.DuLieu.tu_dong(_dong(ch.truy_van(SQL_DAT)))
    kq_start = hoc.hoc_mot_cau_hoi("start", du_start, cho_lgbm)
    kq_pass = hoc.hoc_mot_cau_hoi("pass", du_pass, cho_lgbm)
    tom_tat = {"status": "skipped", "ab": quyet_ab, "impressions": so_hien, "starts": so_bat_dau,
               "start_rows": len(du_start), "pass_rows": len(du_pass), "lightgbm": cho_lgbm}
    if kq_start is None or kq_pass is None:
        tom_tat["reason"] = "chua_du_du_lieu"
        log.info("Hoc mo hinh goi y: chua du du lieu %s", tom_tat)
        return tom_tat

    ung_start, _, kiem_start = kq_start
    ung_pass, _, kiem_pass = kq_pass
    # Cung loai cho ca hai cau hoi; chon theo cau "bat dau" (anh huong xep hang nhieu nhat).
    loai = hoc.chon_tot_nhat(list(ung_start.values())).loai
    moi_start, moi_pass = ung_start[loai], ung_pass[loai]

    file_dang_chay = kho.doc_json(manifest["production"]) if manifest.get("production") else None
    cu_start = hoc.cham_mo_hinh_co_san(file_dang_chay, "start", kiem_start)
    cu_pass = hoc.cham_mo_hinh_co_san(file_dang_chay, "pass", kiem_pass)
    tom_tat.update({"kind": loai, "new_start_auc": moi_start.auc, "current_start_auc": cu_start.auc,
                    "new_pass_auc": moi_pass.auc, "current_pass_auc": cu_pass.auc})

    if not hoc.tot_hon(moi_start, cu_start) or moi_pass.auc + 0.01 < cu_pass.auc:
        tom_tat["reason"] = "khong_tot_hon_ban_dang_chay"
        log.info("Hoc mo hinh goi y: ban moi khong tot hon %s", tom_tat)
        return tom_tat

    if manifest.get("candidate"):
        tom_tat["reason"] = "dang_chay_thu_ab"
        return tom_tat

    phien_ban = loai[:3] + "-" + bay_gio.strftime("%Y%m%d-%H%M%S")
    khoa = "reco/models/" + phien_ban + ".json"
    kho.ghi_json(khoa, hoc.tao_file_mo_hinh(phien_ban, moi_start, moi_pass))
    manifest.update({"candidate": khoa, "candidate_version": phien_ban, "candidate_since": bay_gio.isoformat()})
    kho.ghi_json(MANIFEST, manifest)
    tom_tat.update({"status": "candidate", "version": phien_ban})
    log.info("Mo hinh goi y moi chay thu A/B: %s", tom_tat)
    return tom_tat
