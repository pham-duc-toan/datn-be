# E2E LUU TRU & DON DEP: nhung thay doi de database khong phinh theo so nguoi dung.
#   [1] Phien dang nhap: MOT dong moi phien du lam moi bao nhieu lan; dung lai token cu → thu hoi ca phien.
#   [2] Claim email_verified / kyb_verified nam trong access token (khong con ban sao o ledger / project).
#   [3] Event tan suat cao: bo task.leased; audit KHONG luu chi tiet annotation.submitted nhung bao cao van dem.
#   [4] Thong tin mau luu MOT lan (annotation_samples), nhan chi tro sample_id.
#   [5] gate-svc chi giu mau cua du an BAT cong link.
#   [6] Don outbox da gui qua han o MOI service.
#   [7] Du an da dong: don task / luot giu (task-svc) va nhan tho (quality-svc) — tien do, bao cao chat luong,
#       nhan da nop va uy tin labeler GIU NGUYEN.
# Tu tao tai khoan + du an moi. Chay: python e2e_luu_tru.py   (stack demo: E2E_PROFILE=demo)
import base64, json, os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403

GIA = 10000
# Moi database co bang outbox (media-svc khong co database).
DB_CO_OUTBOX = ("admin", "annotation", "gate", "identity", "ledger", "link", "notification", "payment", "project", "quality", "task")


def code(r):
    try:
        return r.json().get("code")
    except ValueError:
        return None


def claim(u):
    """Phan than cua access token (JWT) — chi doc, khong kiem chu ky."""
    than = u["h"]["Authorization"].split(" ", 1)[1].split(".")[1]
    return json.loads(base64.urlsafe_b64decode(than + "=" * (-len(than) % 4)))


def la_true(v):
    return v is True or v == "true"


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


def dang_nhap(email):
    r = login_raw(email)
    assert r.status_code == 200, r.text
    t = r.json()
    return {"h": hdr(t["accessToken"]), "refresh": t["refreshToken"], "email": email}


def lam_moi(tok):
    return requests.post(G + "/auth/refresh", json={"refreshToken": tok})


def duyet(chu, aid):
    """Duyet nhan; 409 xung_dot_dong_thoi (quality-svc vua cap nhat nhan) → thu lai."""
    for _ in range(6):
        r = requests.post(G + f"/annotations/{aid}/approve", headers=chu["h"])
        if r.status_code != 409 or code(r) != "xung_dot_dong_thoi":
            break
        time.sleep(0.5)
    assert r.status_code == 200, r.text


def thong_ke():
    return requests.get(G + "/admin/stats/overview", headers=admin).json()


def bo(d, *khoa):
    return {k: v for k, v in d.items() if k not in khoa}


try:
    # =================================================================================
    print("\n[1] Phien dang nhap: mot dong moi phien, xoay tai cho")
    a = dang_ky(["labeler"], "phien")
    uid = a["id"]

    def phien(dk="true"):
        return sql("identity", f"select count(*) from login_sessions where user_id='{uid}' and {dk}")

    kiem("dang nhap → 1 dong trong login_sessions", phien() == "1", phien())
    kiem("refresh token dang <ma phien>.<bi mat>", a["refresh"].count(".") == 1 and len(a["refresh"]) > 40, a["refresh"][:12])
    cu = a["refresh"]
    for _ in range(5):
        lam_moi_token(a)
    kiem("lam moi 5 lan → van MOT dong, the he 5",
         sql("identity", f"select count(*)||'/'||max(generation) from login_sessions where user_id='{uid}'") == "1/5",
         sql("identity", f"select count(*)||'/'||max(generation) from login_sessions where user_id='{uid}'"))
    r = lam_moi(cu)
    kiem("dung lai refresh token CU → 401 phien_khong_hop_le", r.status_code == 401 and code(r) == "phien_khong_hop_le", r.text)
    r = lam_moi(a["refresh"])
    kiem("phat hien dung lai → token HIEN TAI cua phien cung bi thu hoi", r.status_code == 401, r.text)
    kiem("phien bi danh dau thu hoi", phien("revoked_at is not null") == "1")

    b1 = dang_nhap(a["email"])
    b2 = dang_nhap(a["email"])
    kiem("hai thiet bi → hai phien con hieu luc", phien("revoked_at is null") == "2", phien("revoked_at is null"))
    kiem("dang xuat mot thiet bi → 204", requests.post(G + "/auth/logout", json={"refreshToken": b1["refresh"]}).status_code == 204)
    kiem("thiet bi da dang xuat: refresh 401", lam_moi(b1["refresh"]).status_code == 401)
    kiem("thiet bi con lai van lam moi duoc", lam_moi(b2["refresh"]).status_code == 200)
    r = lam_moi("khong-phai-token")
    kiem("token sai dinh dang → 401 (khong 500)", r.status_code == 401, r.status_code)
    r = lam_moi("AAAAAAAAAAAAAAAAAAAAAA." + "x" * 43)
    kiem("ma phien khong ton tai → 401", r.status_code == 401, r.status_code)

    # =================================================================================
    print("\n[2] Claim email_verified / kyb_verified trong access token")
    kiem("labeler da xac thuc email: claim email_verified = true", la_true(claim(a).get("email_verified")), claim(a))
    chua = dang_ky(["labeler"], "chuaxt", xac_thuc=False)
    kiem("chua xac thuc: claim email_verified = false", not la_true(claim(chua).get("email_verified")), claim(chua))
    biz = dang_ky(["business"], "biz")
    kiem("doanh nghiep da duyet KYB + lam moi token: kyb_verified = true", la_true(claim(biz).get("kyb_verified")), claim(biz))
    kiem("ledger khong con bang ban sao email", sql("ledger", "select to_regclass('email_statuses') is null and to_regclass('email_status') is null") == "t")
    kiem("project khong con bang ban sao email / KYB",
         sql("project", "select count(*) from information_schema.tables where table_schema='public' "
                        "and (table_name like '%business%' or table_name like '%email%')") == "0")

    # =================================================================================
    print("\n[3] Event tan suat cao: bo task.leased; audit khong luu chi tiet annotation.submitted nhung van dem")
    nap(biz, 1_000_000)
    l1 = dang_ky(["labeler"], "l1")
    l2 = dang_ky(["labeler"], "l2")
    t0 = sql("admin", "select now()")
    tk_truoc = thong_ke()
    P, ids = tao_du_an(biz, "LUUTRU", {"s1": "Mau mot", "s2": "Mau hai", "s3": "Mau ba"}, 2, 2, GIA)
    publish_va_duyet(biz, P)
    for u in (l1, l2):
        tham_gia(u, P)
        lam_het(u, P, lambda _: "a")
    nhan = cho(lambda: (lambda x: x if len(x) == 6 else None)(ds_nhan(biz, P)), 40)
    kiem("2 labeler x 3 mau → 6 nhan", nhan is not None, len(ds_nhan(biz, P)))

    kiem("task-svc khong con phat task.leased",
         sql("task", f"select count(*) from outbox where event_type='task.leased' and occurred_at >= '{t0}'") == "0")
    kiem("task-svc van phat assignment.submitted (6)",
         sql("task", f"select count(*) from outbox where event_type='assignment.submitted' and envelope_json->'payload'->>'projectId'='{P}'") == "6")
    kiem("bao cao van DEM du 6 nhan da nop",
         cho(lambda: thong_ke()["annotationsSubmitted"] >= tk_truoc["annotationsSubmitted"] + 6, 30),
         (tk_truoc["annotationsSubmitted"], thong_ke()["annotationsSubmitted"]))
    kiem("audit KHONG luu chi tiet annotation.submitted",
         sql("admin", f"select count(*) from audit_events where event_type='annotation.submitted' and occurred_at >= '{t0}'") == "0")
    kiem("audit VAN luu thao tac quan trong (project.published cua du an nay)",
         cho(lambda: sql("admin", f"select count(*) from audit_events where event_type='project.published' and payload->>'projectId'='{P}'") == "1", 20))
    j = requests.get(G + "/admin/audit", headers=admin, params={"eventType": "kyb.", "pageSize": 5})
    kiem("API audit van tra cuu duoc", j.status_code == 200 and "items" in j.json(), j.text[:200])

    # =================================================================================
    print("\n[4] Thong tin mau luu MOT lan, nhan tro sample_id")
    kiem("3 mau → 3 dong annotation_samples (6 nhan)",
         sql("annotation", f"select count(*) from annotation_samples where project_id='{P}'") == "3"
         and sql("annotation", f"select count(*) from annotations where project_id='{P}'") == "6",
         sql("annotation", f"select count(*) from annotation_samples where project_id='{P}'"))
    kiem("bang annotations khong con cot noi dung mau",
         sql("annotation", "select count(*) from information_schema.columns where table_name='annotations' "
                           "and column_name in ('storage_key','sample_content','sample_metadata')") == "0")
    noi_dung = {m: t for m, t in (("s1", "Mau mot"), ("s2", "Mau hai"), ("s3", "Mau ba"))}
    dung = all((a.get("sampleContent") or {}).get("text") == noi_dung[next(k for k, v in ids.items() if v == a["sampleId"])] for a in nhan)
    kiem("API nhan van tra dung noi dung mau cua tung nhan", dung, nhan[0])
    cua_toi = requests.get(G + "/annotations/mine?pageSize=50", headers=l1["h"]).json()["annotations"]["items"]
    cua_toi = [x for x in cua_toi if x["projectId"] == P]
    kiem("labeler xem nhan cua minh: van co noi dung mau",
         len(cua_toi) == 3 and all((x.get("sampleContent") or {}).get("text") for x in cua_toi), cua_toi[:1])

    # =================================================================================
    print("\n[5] gate-svc chi giu mau cua du an bat cong link")
    kiem("du an KHONG bat cong link → gate khong giu mau nao",
         cho(lambda: sql("gate", f"select count(*) from gate_samples where project_id='{P}'") == "0", 20),
         sql("gate", f"select count(*) from gate_samples where project_id='{P}'"))

    # =================================================================================
    print("\n[6] Don outbox da gui qua han o moi service")
    dieu_kien = "published_at is not null and published_at < now() - interval '65 minutes'"
    truoc = {db: int(sql(db, f"select count(*) from outbox where {dieu_kien}") or 0) for db in DB_CO_OUTBOX}
    print("     event da gui > 65 phut truoc khi don:", {k: v for k, v in truoc.items() if v})
    # admin-svc la chu bang settings (khong co settings_replica) — cho 10 service con lai nhan gia tri moi.
    co_ban_sao = tuple(db for db in DB_CO_OUTBOX if db != "admin")
    dat_va_cho("outbox.retention", 3600, co_ban_sao)
    dat_va_cho("outbox.cleanup_interval", 10, co_ban_sao)

    def con_lai():
        return {db: n for db, n in ((db, int(sql(db, f"select count(*) from outbox where {dieu_kien}") or 0)) for db in DB_CO_OUTBOX) if n}

    kiem("ca 11 service xoa het event da gui qua han (giu 1 gio)", cho(lambda: not con_lai(), 180, 3), con_lai())
    kiem("dau vet chong trung cua luot vua lam van con (khong xoa som)",
         int(sql("annotation", "select count(*) from processed_events where processed_at > now() - interval '10 minutes'")) > 0)

    # =================================================================================
    print("\n[7] Du an da dong: don du lieu van hanh, giu bao cao + nhan + uy tin")
    r = requests.post(G + f"/tasks/admin/projects/{P}/purge", headers=admin)
    kiem("du an dang chay → don task 409 du_an_chua_dong", r.status_code == 409 and code(r) == "du_an_chua_dong", r.text)
    r = requests.post(G + f"/quality/admin/projects/{P}/archive", headers=admin)
    kiem("du an dang chay → luu tru chat luong 409 du_an_chua_dong", r.status_code == 409 and code(r) == "du_an_chua_dong", r.text)
    kiem("chu du an goi don → 403", requests.post(G + f"/tasks/admin/projects/{P}/purge", headers=biz["h"]).status_code == 403)
    kiem("chu du an goi luu tru → 403", requests.post(G + f"/quality/admin/projects/{P}/archive", headers=biz["h"]).status_code == 403)
    r = requests.post(G + f"/tasks/admin/projects/{uuid.uuid4()}/purge", headers=admin)
    kiem("du an khong ton tai → 404", r.status_code == 404, r.text)

    # Cho dong thuan xong roi duyet het, tam dung, hoan thanh.
    cho(lambda: sql("quality", f"select count(*) from consensus_rounds where project_id='{P}'") == "3", 30)
    for x in nhan:
        duyet(biz, x["id"])
    requests.post(G + f"/projects/{P}/pause", headers=biz["h"]).raise_for_status()
    xong = cho(lambda: (lambda x: x if x.status_code == 200 else None)(requests.post(G + f"/projects/{P}/complete", headers=biz["h"], json={"reason": "e2e"})), 40)
    kiem("hoan thanh du an → 200", xong is not None, requests.post(G + f"/projects/{P}/complete", headers=biz["h"], json={"reason": "e2e"}).text)
    kiem("task-svc + quality-svc da ghi nhan du an dong",
         cho(lambda: sql("task", f"select status from project_snapshots where project_id='{P}'") == "Closed"
             and sql("quality", f"select closed_at is not null from projects where project_id='{P}'") == "t", 30))

    td_truoc = requests.get(G + f"/tasks/projects/{P}/progress", headers=biz["h"]).json()
    # Cho lo Dawid-Skene / uy tin on dinh roi moi chup so lieu "truoc".
    requests.post(G + "/quality/admin/dawid-skene/run", headers=admin)
    time.sleep(2)
    tq_truoc = requests.get(G + f"/quality/projects/{P}/summary", headers=biz["h"]).json()
    lb_truoc = requests.get(G + f"/quality/projects/{P}/labelers", headers=biz["h"]).json()
    uy_tin = "select string_agg(labeler_id||':'||reputation||':'||agree_total||':'||agree_count, ',' order by labeler_id) " \
             f"from reputations where labeler_id in ('{l1['id']}','{l2['id']}')"
    ut_truoc = sql("quality", uy_tin)
    khop_truoc = sql("quality", f"select count(*) from label_agreements where project_id='{P}'")
    kiem("truoc khi don: tien do 3 task xong, bao cao 6 nhan / 3 task dong thuan",
         td_truoc["totalTasks"] == 3 and td_truoc["completedTasks"] == 3 and td_truoc.get("frozenAt") is None
         and tq_truoc["labelCount"] == 6 and tq_truoc["agreed"] == 3 and tq_truoc["archivedAt"] is None and len(lb_truoc) == 2,
         (td_truoc, tq_truoc))

    r = requests.post(G + f"/tasks/admin/projects/{P}/purge", headers=admin)
    j = r.json() if r.status_code == 200 else {}
    kiem("admin don du an da dong → 200, xoa 3 task + 6 luot giu",
         r.status_code == 200 and j["purgedNow"] is True and j["deletedTasks"] == 3 and j["deletedAssignments"] == 6, r.text)
    kiem("task-svc khong con task / luot giu cua du an",
         sql("task", f"select (select count(*) from tasks where project_id='{P}') + (select count(*) from assignments where project_id='{P}')") == "0")
    td_sau = requests.get(G + f"/tasks/projects/{P}/progress", headers=biz["h"]).json()
    kiem("tien do van xem duoc, dung so cu (ban chup, frozenAt co gia tri)",
         td_sau.get("frozenAt") is not None
         and bo(td_sau, "frozenAt", "submissionsLast24h", "estimatedCompletionAt") == bo(td_truoc, "frozenAt", "submissionsLast24h", "estimatedCompletionAt"),
         (td_truoc, td_sau))
    r = requests.post(G + f"/tasks/admin/projects/{P}/purge", headers=admin)
    kiem("don lan hai → 200 purgedNow = false (khong lam lai)", r.status_code == 200 and r.json()["purgedNow"] is False, r.text)
    kiem("labeler khong con thay luot giu cua du an da don",
         all(x["projectId"] != P for x in requests.get(G + "/tasks/assignments/mine", headers=l1["h"]).json()))

    r = requests.post(G + f"/quality/admin/projects/{P}/archive", headers=admin)
    j = r.json() if r.status_code == 200 else {}
    kiem("admin luu tru chat luong → 200, xoa 6 nhan tho + 3 vong dong thuan + 3 target",
         r.status_code == 200 and j["purgedNow"] is True and j["deleted"].get("labels") == 6
         and j["deleted"].get("consensus_rounds") == 3 and j["deleted"].get("task_targets") == 3, r.text)
    kiem("quality-svc khong con nhan tho cua du an",
         sql("quality", f"select (select count(*) from labels where project_id='{P}') + (select count(*) from consensus_rounds where project_id='{P}') "
                        f"+ (select count(*) from task_targets where project_id='{P}')") == "0")
    tq_sau = requests.get(G + f"/quality/projects/{P}/summary", headers=biz["h"]).json()
    lb_sau = requests.get(G + f"/quality/projects/{P}/labelers", headers=biz["h"]).json()
    kiem("bao cao chat luong van xem duoc, dung so cu (archivedAt co gia tri)",
         tq_sau.get("archivedAt") is not None and bo(tq_sau, "archivedAt") == bo(tq_truoc, "archivedAt"), (tq_truoc, tq_sau))
    kiem("bao cao theo labeler giu nguyen", lb_sau == lb_truoc, (lb_truoc, lb_sau))
    r = requests.post(G + f"/quality/admin/projects/{P}/archive", headers=admin)
    kiem("luu tru lan hai → 200 purgedNow = false", r.status_code == 200 and r.json()["purgedNow"] is False, r.text)

    kiem("du lieu tinh uy tin (label_agreements) KHONG bi xoa",
         sql("quality", f"select count(*) from label_agreements where project_id='{P}'") == khop_truoc and int(khop_truoc) == 6, khop_truoc)
    kiem("uy tin 2 labeler KHONG doi ngay sau khi don", sql("quality", uy_tin) == ut_truoc, (ut_truoc, sql("quality", uy_tin)))

    # Ep quality-svc TINH LAI uy tin cua hai labeler bang mot du an moi: so lan khop phai CONG DON
    # (3 cua du an da don + 1 moi), khong tut ve 1 — tuc du lieu tinh uy tin cua du an da don con nguyen.
    khop = "select string_agg(agree_total::text, ',' order by labeler_id) from reputations " \
           f"where labeler_id in ('{l1['id']}','{l2['id']}')"
    kiem("truoc du an moi: moi labeler co 3 lan so khop", sql("quality", khop) == "3,3", sql("quality", khop))
    P2, _ = tao_du_an(biz, "SAUDON", {"k1": "Mau sau khi don"}, 2, 2, GIA)
    publish_va_duyet(biz, P2)
    for u in (l1, l2):
        tham_gia(u, P2)
        lam_het(u, P2, lambda _: "a")
    kiem("sau du an moi: 4 lan so khop (3 cu + 1 moi) — uy tin van tinh tren ca du an da don",
         cho(lambda: sql("quality", khop) == "4,4", 40), sql("quality", khop))

    sau = ds_nhan(biz, P)
    kiem("nhan da duyet van con du 6, kem noi dung mau",
         len(sau) == 6 and all(x["status"] == "approved" and (x.get("sampleContent") or {}).get("text") for x in sau), sau[:1])
    r = requests.get(G + f"/annotations/projects/{P}/results", headers=biz["h"])
    kiem("ket qua du an van lay duoc", r.status_code == 200, r.text[:200])
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai van lanh", rec.get("healthy") is True, rec)

finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e luu tru: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
