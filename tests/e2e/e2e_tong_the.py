# E2E TONG THE — tu tao tai khoan + du an moi moi lan chay (khong phu thuoc du lieu seed).
# Phu: xac thuc, nap tien, vong doi du an + saga ky quy, bai test dau vao, nhan/nop/bo qua task,
# dong thuan + redundancy thich ung (majority / posterior / voi), duyet + khieu nai, vong tien
# (treo → giai phong → rut, tu duyet / admin duyet / tu choi), cua so ngau nhien khi lay task,
# quan ly du an, phan quyen (RBAC + BOLA), setting, va kiem tra tong (doi soat, DLQ, outbox).
# Chay: python e2e_tong_the.py   (stack demo / CI: dat E2E_PROFILE=demo — xem moi_truong.py)
import os, sys, time, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403

try:
    # =================================================================================
    print("\n[1] Xac thuc: dang ky, dang nhap, refresh mot lan, dang xuat, vai tro")
    biz = dang_ky(["business"], "biz")
    biz2 = dang_ky(["business"], "biz2")
    lab = {f"lab{i}": dang_ky(["labeler"], f"lab{i}") for i in range(1, 5)}
    r = requests.post(G + "/auth/register", json={"email": biz["email"], "password": PW, "displayName": "x", "roles": ["business"]})
    kiem("dang ky trung email → 409", r.status_code == 409, r.text)
    r = requests.post(G + "/auth/register", json={"email": f"e2e-{TAG}-adm@crowd.local", "password": PW, "displayName": "x", "roles": ["admin"]})
    kiem("tu dang ky vai tro admin → 400", r.status_code == 400, r.text)
    r = requests.post(G + "/auth/register", json={"email": f"e2e-{TAG}-ngan@crowd.local", "password": "abc", "displayName": "x", "roles": ["labeler"]})
    kiem("mat khau qua ngan → 400", r.status_code == 400, r.text)
    kiem("sai mat khau → 401", login_raw(biz["email"], "SaiMatKhau@1").status_code == 401)
    kiem("email khong ton tai → 401 (cung thong bao)", login_raw(f"khong-co-{TAG}@crowd.local").status_code == 401)
    kiem("/me khong token → 401", requests.get(G + "/me").status_code == 401)
    kiem("/me token rac → 401", requests.get(G + "/me", headers={"Authorization": "Bearer abc.def.ghi"}).status_code == 401)
    me = requests.get(G + "/me", headers=lab["lab1"]["h"]).json()
    kiem("/me tra dung vai tro labeler", me["roles"] == ["labeler"], me)
    r = requests.post(G + "/auth/refresh", json={"refreshToken": lab["lab4"]["refresh"]})
    kiem("refresh → cap token moi", r.status_code == 200 and r.json()["refreshToken"] != lab["lab4"]["refresh"], r.text)
    moi = r.json()
    r = requests.post(G + "/auth/refresh", json={"refreshToken": lab["lab4"]["refresh"]})
    kiem("dung lai refresh token cu → 401 (dung mot lan)", r.status_code == 401, r.text)
    kiem("token moi dung duoc", requests.get(G + "/me", headers=hdr(moi["accessToken"])).status_code == 200)
    r = requests.post(G + "/auth/logout", json={"refreshToken": moi["refreshToken"]})
    kiem("dang xuat → 204", r.status_code == 204, r.text)
    kiem("refresh sau dang xuat → 401", requests.post(G + "/auth/refresh", json={"refreshToken": moi["refreshToken"]}).status_code == 401)

    # =================================================================================
    print("\n[2] Nap tien qua cong (sandbox), idempotency, phan quyen")
    k = "e2e-" + uuid.uuid4().hex
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 3000000})
    kiem("tao lenh nap 3.000.000", r.status_code == 200, r.text)
    intent = r.json()["intentId"]
    r2 = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": k}), json={"amountVnd": 3000000})
    kiem("cung Idempotency-Key → cung lenh", r2.json()["intentId"] == intent, r2.text)
    kiem("labeler nap tien → 403 (RBAC)", requests.post(G + "/payments/deposits", headers=dict(lab["lab1"]["h"], **{"Idempotency-Key": uuid.uuid4().hex}),
                                                    json={"amountVnd": 100000}).status_code == 403)
    r = requests.post(G + "/payments/deposits", headers=dict(biz["h"], **{"Idempotency-Key": uuid.uuid4().hex}), json={"amountVnd": 0})
    kiem("nap 0 dong → 400", r.status_code == 400, r.text)
    kiem("biz2 xem lenh nap cua biz → 404 (BOLA)", requests.get(G + f"/payments/deposits/{intent}", headers=biz2["h"]).status_code == 404)
    requests.post(G + f"/payments/sandbox/checkout/{intent}/pay?amount=3000000")
    ok = cho(lambda: vi(biz)["businessAvailableVnd"] == 3000000, 20)
    kiem("ledger cong 3.000.000 vao vi doanh nghiep", ok, vi(biz))
    requests.post(G + f"/payments/sandbox/checkout/{intent}/pay?amount=3000000")
    time.sleep(2)
    kiem("thanh toan lai cung lenh → khong cong hai lan", vi(biz)["businessAvailableVnd"] == 3000000, vi(biz))

    # =================================================================================
    print("\n[3] Vong doi du an: tao, kiem gia, ky quy (saga), duyet, thieu tien")
    MAU_A = {"s1": "Hang tot nhung giao cham", "s2": "Tuyet voi", "s3": "Qua te", "s4": "Rat hai long", "s5": "That vong", "s6": "On"}
    DUNG_A = {"Hang tot nhung giao cham": "a", "Tuyet voi": "a", "Qua te": "b", "Rat hai long": "a", "That vong": "b", "On": "a"}
    r = requests.post(G + "/projects", headers=lab["lab1"]["h"], json={"name": "x", "description": "x", "modality": "text", "visibility": "public"})
    kiem("labeler tao du an → 403", r.status_code == 403, r.text)
    A, mau_a = tao_du_an(biz, "Chinh", MAU_A, 2, 3, 20000, ngan_sach=500000)
    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"unitPriceVnd": 20000, "redundancy": 3, "maxRedundancy": 2,
                                                                          "budgetVnd": 500000, "deadline": "2027-12-31T00:00:00Z"})
    kiem("tran < redundancy → 400", r.status_code == 400 and "tran_redundancy_khong_hop_le" in r.text, r.text)
    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"unitPriceVnd": 20000, "redundancy": 2, "maxRedundancy": 3,
                                                                          "budgetVnd": 100000, "deadline": "2027-12-31T00:00:00Z"})
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    kiem("ky quy toi thieu = 6 mau x tran 3 x (20.000 + 30%) = 468.000", rdy["estimatedCostVnd"] == 468000, rdy)
    r = requests.post(G + f"/projects/{A}/publish", headers=biz["h"])
    kiem("ngan sach 100.000 < 468.000 → publish bi chan", r.status_code in (400, 409), r.text)
    requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"unitPriceVnd": 20000, "redundancy": 2, "maxRedundancy": 3,
                                                                      "budgetVnd": 500000, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    kiem("biz2 sua du an cua biz → 404 (BOLA)", requests.put(G + f"/projects/{A}/guideline", headers=biz2["h"],
                                                             json={"markdown": "hack", "examples": []}).status_code == 404)
    kiem("labeler xem du an nhap cua nguoi khac → 404", requests.get(G + f"/projects/{A}", headers=lab["lab1"]["h"]).status_code == 404)
    requests.post(G + f"/projects/{A}/publish", headers=biz["h"]).raise_for_status()
    ok = cho(lambda: requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"] == "pendingApproval", 30)
    kiem("ky quy xong → cho admin duyet", ok, requests.get(G + f"/projects/{A}", headers=biz["h"]).text)
    kiem("vi doanh nghiep tru dung ngan sach 500.000", cho(lambda: vi(biz)["businessAvailableVnd"] == 2500000, 10), vi(biz))
    kiem("admin thay du an trong hang cho duyet", any(p["id"] == A for p in ds(requests.get(G + "/projects/pending-approval?pageSize=100", headers=admin).json())))
    kiem("labeler duyet du an → 403", requests.post(G + f"/projects/{A}/approve", headers=lab["lab1"]["h"]).status_code == 403)
    kiem("chu du an tu duyet → 403", requests.post(G + f"/projects/{A}/approve", headers=biz["h"]).status_code == 403)
    r = requests.post(G + f"/projects/{A}/approve", headers=admin)
    kiem("admin duyet → running", r.status_code == 200 and cho(lambda: requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"] == "running", 10), r.text)
    kiem("duyet lan hai → 409", requests.post(G + f"/projects/{A}/approve", headers=admin).status_code == 409)

    T, _ = tao_du_an(biz2, "Thieu tien", {"t1": "x", "t2": "y"}, 1, 1, 10000)
    requests.post(G + f"/projects/{T}/publish", headers=biz2["h"]).raise_for_status()
    du = cho(lambda: (lambda p: p if p["status"] == "draft" and p.get("statusReason") else None)(requests.get(G + f"/projects/{T}", headers=biz2["h"]).json()), 30)
    kiem("biz2 khong co tien → ledger tu choi ky quy, du an ve nhap kem ly do", du is not None, requests.get(G + f"/projects/{T}", headers=biz2["h"]).text)
    R, _ = tao_du_an(biz, "Bi tu choi", {"r1": "x"}, 1, 1, 10000)
    truoc = vi(biz)["businessAvailableVnd"]
    publish_va_duyet_ok = requests.post(G + f"/projects/{R}/publish", headers=biz["h"]).status_code == 200
    cho(lambda: requests.get(G + f"/projects/{R}", headers=biz["h"]).json()["status"] == "pendingApproval", 30)
    r = requests.post(G + f"/projects/{R}/reject", headers=admin, json={"reason": "Noi dung khong phu hop"})
    kiem("admin tu choi du an", r.status_code == 200, r.text)
    kiem("tu choi → hoan ky quy (compensation)", cho(lambda: vi(biz)["businessAvailableVnd"] == truoc, 20), (truoc, vi(biz)))

    # =================================================================================
    print("\n[4] Bai test dau vao (du an B)")
    MAU_B = {"b1": "Cau hoi test 1", "b2": "Cau hoi test 2", "b3": "Task that 3", "b4": "Task that 4"}
    B, mau_b = tao_du_an(biz, "Test dau vao", MAU_B, 1, 1, 10000, ngan_sach=200000,
                         dieu_kien={"minLevel": None, "minReputation": None, "requireEntranceTest": True, "entranceQuestionCount": 2, "entrancePassPercent": 100},
                         vang=[("b1", "a", "entranceTest"), ("b2", "b", "entranceTest")])
    publish_va_duyet(biz, B)
    l3 = lab["lab3"]
    r = requests.post(G + f"/projects/{B}/join", headers=l3["h"])
    kiem("join du an can test → 409 can_lam_test", r.status_code == 409 and "can_lam_test" in r.text, r.text)
    kiem("chua tham gia → next 403", requests.post(G + f"/tasks/projects/{B}/next", headers=l3["h"]).status_code == 403)
    at = requests.post(G + f"/projects/{B}/entrance-test/attempts", headers=l3["h"]).json()
    kiem("bai test co 2 cau, khong lo dap an", len(at["questions"]) == 2 and "expectedPayload" not in json.dumps(at), at)
    dung_b = {mau_b["b1"]: "a", mau_b["b2"]: "b"}
    r = requests.post(G + f"/projects/{B}/entrance-test/attempts/{at['attemptId']}/submit", headers=l3["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"labelIds": ["a"]}} for q in at["questions"]]})
    kiem("tra loi sai dinh dang → 400, chua tinh la da nop", r.status_code == 400, r.text)
    r = requests.post(G + f"/projects/{B}/entrance-test/attempts/{at['attemptId']}/submit", headers=l3["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"nhan": {"labelIds": ["a"]}}} for q in at["questions"]]})
    kiem("tra loi sai mot cau → truot 50%, con 2 lan", r.status_code == 200 and not r.json()["passed"] and r.json()["scorePercent"] == 50 and r.json()["attemptsLeft"] == 2, r.text)
    r = requests.post(G + f"/projects/{B}/entrance-test/attempts/{at['attemptId']}/submit", headers=l3["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"nhan": {"labelIds": [dung_b[q["sampleId"]]]}}} for q in at["questions"]]})
    kiem("nop lai bai da cham → 409", r.status_code == 409, r.text)
    at = requests.post(G + f"/projects/{B}/entrance-test/attempts", headers=l3["h"]).json()
    r = requests.post(G + f"/projects/{B}/entrance-test/attempts/{at['attemptId']}/submit", headers=l3["h"],
                      json={"answers": [{"sampleId": q["sampleId"], "payload": {"nhan": {"labelIds": [dung_b[q["sampleId"]]]}}} for q in at["questions"]]})
    kiem("lan 2 dung het → dau, tu thanh vien", r.status_code == 200 and r.json()["passed"] and r.json()["joinedProject"], r.text)
    kiem("lich su 2 lan lam", len(requests.get(G + f"/projects/{B}/entrance-test/attempts", headers=l3["h"]).json()) == 2)
    t = nhan_task(l3, B)
    kiem("dau test → nhan duoc task THAT (khong phai cau test)", t is not None and t["sampleId"] in (mau_b["b3"], mau_b["b4"]), t)
    if t:
        nop(l3, t["assignmentId"], "a").raise_for_status()

    # =================================================================================
    print("\n[5] Nhan / nop / bo qua task (du an A, redundancy 2)")
    l1, l2 = lab["lab1"], lab["lab2"]
    r = requests.post(G + f"/tasks/projects/{A}/next", headers=l1["h"])
    kiem("chua tham gia → 403 khong_phai_thanh_vien", r.status_code == 403 and "khong_phai_thanh_vien" in r.text, r.text)
    for u in (l1, l2):
        r = requests.post(G + f"/projects/{A}/join", headers=u["h"])
        assert r.status_code == 200, r.text
    kiem("join lan hai → khong loi (idempotent hoac 409)", requests.post(G + f"/projects/{A}/join", headers=l1["h"]).status_code in (200, 409))
    t1 = nhan_task(l1, A)
    kiem("nhan task: co assignmentId, content, labelSchema", t1 and t1["assignmentId"] and t1["content"]["text"] and t1["labelSchema"], t1)
    t1b = nhan_task(l1, A)
    kiem("bam nhan task lan hai → CHINH task dang giu", t1b["assignmentId"] == t1["assignmentId"], (t1["assignmentId"], t1b["assignmentId"]))
    aid = t1["assignmentId"]
    r = requests.post(G + f"/tasks/assignments/{aid}/submit", headers=l1["h"], json={})
    kiem("thieu payload → 400 thieu_nhan", r.status_code == 400 and "thieu_nhan" in r.text, r.text)
    r = requests.post(G + f"/tasks/assignments/{aid}/submit", headers=l1["h"], json={"payload": {"labelIds": ["a"]}})
    kiem("payload thieu ten cong cu → 400 nhan_sai_dinh_dang", r.status_code == 400 and "nhan_sai_dinh_dang" in r.text, r.text)
    r = nop(l1, aid, "khong_co")
    kiem("lop khong co trong tap nhan → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/tasks/assignments/{aid}/submit", headers=l1["h"], json={"payload": {"nhan": {"labelIds": ["a", "b"]}}})
    kiem("chon hai lop o cong cu chon mot → 400", r.status_code == 400, r.text)
    kiem("lab2 nop luot cua lab1 → 404 (BOLA)", nop(l2, aid, "a").status_code == 404)
    kiem("lab2 bo qua luot cua lab1 → 404", requests.post(G + f"/tasks/assignments/{aid}/release", headers=l2["h"]).status_code == 404)
    r = requests.post(G + f"/tasks/assignments/{aid}/release", headers=l1["h"])
    kiem("bo qua → task tra ve pool", r.status_code in (200, 204), r.text)
    kiem("nop luot da bo qua → bi tu choi", nop(l1, aid, "a").status_code in (404, 409))
    mine = requests.get(G + "/tasks/assignments/mine", headers=l1["h"]).json()
    kiem("/tasks/assignments/mine khong con luot da bo qua", all(x["assignmentId"] != aid for x in mine), mine)

    def tra_loi_l2(text):
        return "b" if text == MAU_A["s1"] else DUNG_A[text]   # s1: lab2 chon khac → tranh chap

    n1 = lam_het(l1, A, lambda x: DUNG_A[x])
    n2 = lam_het(l2, A, tra_loi_l2)
    kiem("lab1, lab2 moi nguoi nop 6 task", n1 == 6 and n2 == 6, (n1, n2))
    kiem("lam het → 204", nhan_task(l1, A) is None)
    t = requests.get(G + f"/tasks/projects/{A}/progress", headers=biz["h"]).json()
    kiem("tien do: 12 luot nop", t["submissions"] == 12, t)
    kiem("labeler xem tien do → 404", requests.get(G + f"/tasks/projects/{A}/progress", headers=l1["h"]).status_code == 404)

    # =================================================================================
    print("\n[6] Dong thuan (majority) + redundancy thich ung")
    task_s1 = sql("task", f"select id from tasks where sample_id='{mau_a['s1']}'")
    kiem("s1 tranh chap → quality xin them nguoi → redundancy 3", cho(lambda: sql("task", f"select redundancy_target from tasks where id='{task_s1}'") == "3", 30),
         sql("task", f"select redundancy_target, state from tasks where id='{task_s1}'"))
    kiem("5 task con lai khop → agreed", cho(lambda: sql("quality", f"select count(*) from consensus_rounds where project_id='{A}' and status='agreed'") == "5", 30),
         sql("quality", f"select status, count(*) from consensus_rounds where project_id='{A}' group by 1"))
    l3 = lab["lab3"]
    requests.post(G + f"/projects/{A}/join", headers=l3["h"]).raise_for_status()
    t = nhan_task(l3, A)
    kiem("nguoi thu ba nhan dung task s1", t is not None and t["sampleId"] == mau_a["s1"], t)
    if t:
        nop(l3, t["assignmentId"], "a").raise_for_status()
    kiem("s1 du 3 nguoi → agreed, final = a",
         cho(lambda: sql("quality", f"select final::text from consensus_rounds where task_id='{task_s1}' and target=3 and status='agreed'"), 30),
         sql("quality", f"select target, status, final from consensus_rounds where task_id='{task_s1}'"))
    kiem("nhan duoc gan co khop / lech dong thuan",
         cho(lambda: all(a.get("consensusAgrees") is not None for a in ds_nhan(biz, A)), 20), [(a["status"], a.get("consensusAgrees")) for a in ds_nhan(biz, A)])
    res = requests.get(G + f"/annotations/projects/{A}/results", headers=biz["h"])
    kiem("ket qua du an 200", res.status_code == 200, res.text[:300])
    csv = requests.get(G + f"/annotations/projects/{A}/export?format=csv", headers=biz["h"])
    kiem("xuat CSV khi chua duyet nhan nao: chi dong tieu de", csv.status_code == 200 and len(csv.text.strip().splitlines()) == 1, csv.text[:200])
    kiem("xuat JSON", requests.get(G + f"/annotations/projects/{A}/export?format=json", headers=biz["h"]).status_code == 200)
    kiem("xuat COCO cho du an van ban → 400", requests.get(G + f"/annotations/projects/{A}/export?format=coco", headers=biz["h"]).status_code == 400)

    # =================================================================================
    print("\n[7] Duyet nhan, reviewer, khieu nai, vong tien treo → kha dung")
    dat_va_cho("ledger.hold_duration", 0, ("ledger",))
    dat_va_cho("ledger.hold_release_interval", 5, ("ledger",))
    l4 = lab["lab4"]
    r = requests.post(G + f"/projects/{A}/members", headers=biz["h"], json={"userId": l4["id"], "role": "reviewer"})
    kiem("chu du an them reviewer", r.status_code in (200, 201), r.text)
    nhan = ds_nhan(biz, A)
    cua = lambda u: [a for a in nhan if a["labelerId"] == u["id"]]
    n_l1, n_l2 = cua(l1), cua(l2)
    kiem("lab1 co 6 nhan, lab2 co 6 nhan cho duyet", len(n_l1) == 6 and len(n_l2) == 6, (len(n_l1), len(n_l2)))
    kiem("labeler xem hang duyet → 404", requests.get(G + f"/annotations/projects/{A}?status=pendingReview", headers=l1["h"]).status_code == 404)
    kiem("reviewer xem hang duyet → 200", cho(lambda: requests.get(G + f"/annotations/projects/{A}?status=pendingReview", headers=l4["h"]).status_code == 200, 10))
    r = requests.post(G + f"/annotations/{n_l1[0]['id']}/approve", headers=l4["h"])
    kiem("reviewer duyet nhan", r.status_code == 200, r.text)
    kiem("duyet lai → 409 da_duyet", requests.post(G + f"/annotations/{n_l1[0]['id']}/approve", headers=biz["h"]).status_code == 409)
    kiem("labeler tu duyet nhan cua minh → 404", requests.post(G + f"/annotations/{n_l1[1]['id']}/approve", headers=l1["h"]).status_code == 404)
    for a in n_l1[1:5]:
        requests.post(G + f"/annotations/{a['id']}/approve", headers=biz["h"]).raise_for_status()
    ok = cho(lambda: vi(l1)["availableVnd"] == 100000, 40)
    kiem("lab1: 5 nhan duyet x 20.000 → treo → giai phong → kha dung 100.000", ok, vi(l1))
    kiem("ledger: moi nhan duyet mot khoan treo (5)", sql("ledger", f"select count(*) from holds where project_id='{A}' and labeler_id='{l1['id']}'") == "5")

    r = requests.post(G + f"/annotations/{n_l2[0]['id']}/reject", headers=biz["h"], json={"reason": ""})
    kiem("tu choi khong ly do → 400", r.status_code == 400, r.text)
    for a in n_l2[0:2]:
        requests.post(G + f"/annotations/{a['id']}/reject", headers=biz["h"], json={"reason": "Sai nhan"}).raise_for_status()
    r = requests.post(G + f"/annotations/{n_l2[0]['id']}/appeal", headers=l1["h"], json={"message": "x"})
    kiem("khieu nai nhan cua nguoi khac → 404", r.status_code == 404, r.text)
    r = requests.post(G + f"/annotations/{n_l2[2]['id']}/appeal", headers=l2["h"], json={"message": "x"})
    kiem("khieu nai nhan chua bi tu choi → 409", r.status_code == 409, r.text)
    for a in n_l2[0:2]:
        requests.post(G + f"/annotations/{a['id']}/appeal", headers=l2["h"], json={"message": "De nghi xem lai"}).raise_for_status()
    kiem("khieu nai lan hai → 409", requests.post(G + f"/annotations/{n_l2[0]['id']}/appeal", headers=l2["h"], json={"message": "lai"}).status_code == 409)
    hang = ds(requests.get(G + "/annotations/appeals?pageSize=100", headers=admin).json())
    kiem("admin thay 2 khieu nai", sum(1 for x in hang if x["id"] in (n_l2[0]["id"], n_l2[1]["id"])) == 2, hang[:2])
    kiem("labeler xem hang khieu nai → 403", requests.get(G + "/annotations/appeals", headers=l2["h"]).status_code == 403)
    truoc = vi(l2)["availableVnd"] + vi(l2)["pendingVnd"]
    r = requests.post(G + f"/annotations/{n_l2[0]['id']}/appeal/resolve", headers=admin, json={"accept": True, "note": "Chap nhan"})
    kiem("chap nhan khieu nai → approved", r.status_code == 200, r.text)
    kiem("lab2 duoc tra 20.000", cho(lambda: vi(l2)["availableVnd"] + vi(l2)["pendingVnd"] == truoc + 20000, 20), (truoc, vi(l2)))
    r = requests.post(G + f"/annotations/{n_l2[1]['id']}/appeal/resolve", headers=admin, json={"accept": False, "note": "Giu nguyen"})
    kiem("bac khieu nai → rejected vinh vien", r.status_code == 200, r.text)
    kiem("giai quyet lai → 409", requests.post(G + f"/annotations/{n_l2[1]['id']}/appeal/resolve", headers=admin, json={"accept": True, "note": "x"}).status_code == 409)
    hist = requests.get(G + f"/annotations/{n_l2[0]['id']}/history", headers=biz["h"]).json()
    kiem("lich su nhan co tu choi + khieu nai + chap nhan", len(hist) >= 3, hist)
    r = requests.post(G + f"/annotations/projects/{A}/approve-agreed", headers=biz["h"])
    kiem("duyet hang loat nhan khop dong thuan", r.status_code == 200, r.text)
    con = [a for a in ds_nhan(biz, A) if a["status"] == "pendingReview"]
    kiem("con lai chi nhan lech dong thuan cho duyet tay", all(a.get("consensusAgrees") is False for a in con), [(a["status"], a.get("consensusAgrees")) for a in con])
    mine = requests.get(G + "/annotations/mine?pageSize=50", headers=l2["h"]).json()
    kiem("labeler xem lich su nhan cua minh (chi nhan cua minh)",
         len(mine["annotations"]["items"]) == 6 and all(a["labelerId"] == l2["id"] for a in mine["annotations"]["items"]), str(mine)[:300])
    csv = requests.get(G + f"/annotations/projects/{A}/export?format=csv", headers=biz["h"])
    kiem("xuat CSV sau khi duyet: co dong du lieu", csv.status_code == 200 and len(csv.text.strip().splitlines()) >= 2, csv.text[:300])

    # =================================================================================
    print("\n[8] Rut tien: toi thieu, so du, idempotency, tu duyet, admin duyet / tu choi")
    dat_va_cho("ledger.withdraw_min_vnd", 10000, ("ledger",))
    dat_va_cho("ledger.withdraw_auto_approve_max_vnd", 15000, ("ledger",))
    # approve-agreed o muc 7 duyet them nhan khop cua lab1 → lay so du goc sau khi treo da giai phong het.
    cho(lambda: vi(l1)["pendingVnd"] == 0, 30)
    goc = vi(l1)["availableVnd"]
    kiem("so du goc lab1 = so nhan da duyet x 20.000",
         goc == 20000 * int(sql("ledger", f"select count(*) from holds where labeler_id='{l1['id']}'")), (goc, vi(l1)))

    def rut(u, so, key=None):
        return requests.post(G + "/ledger/withdrawals", headers=dict(u["h"], **{"Idempotency-Key": key or uuid.uuid4().hex}),
                             json={"amountVnd": so, "bankAccount": "VCB-0123456789"})

    kiem("rut 5.000 < toi thieu → 400", rut(l1, 5000).status_code == 400)
    kiem("rut qua so du → 409 khong_du_so_du", rut(l1, 10000000).status_code == 409)
    kiem("doanh nghiep rut → 403", rut(biz, 10000).status_code == 403)
    key = uuid.uuid4().hex
    r = rut(l1, 10000, key)
    kiem("rut 10.000 ≤ nguong 15.000 → tu duyet", r.status_code == 200 and r.json()["state"] in ("requested", "completed"), r.text)
    w1 = r.json()["id"]
    kiem("cung Idempotency-Key → cung lenh, khong tru hai lan", rut(l1, 10000, key).json()["id"] == w1)
    kiem("lenh tu duyet → completed", cho(lambda: sql("ledger", f"select state from withdrawals where id='{w1}'") == "Completed", 30))
    r = rut(l1, 20000)
    kiem("rut 20.000 > nguong → pendingApproval", r.status_code == 200 and r.json()["state"] == "pendingApproval", r.text)
    w2 = r.json()["id"]
    kiem("labeler vao hang duyet rut → 403", requests.get(G + "/ledger/admin/withdrawals", headers=l1["h"]).status_code == 403)
    kiem("admin duyet", requests.post(G + f"/ledger/admin/withdrawals/{w2}/approve", headers=admin).status_code == 200)
    kiem("duyet lai → 409", requests.post(G + f"/ledger/admin/withdrawals/{w2}/approve", headers=admin).status_code == 409)
    kiem("lenh admin duyet → completed", cho(lambda: sql("ledger", f"select state from withdrawals where id='{w2}'") == "Completed", 30))
    w3 = rut(l1, 20000).json()["id"]
    kiem("dang cho duyet: tien da bi giu (goc - 50.000)", vi(l1)["availableVnd"] == goc - 50000, (goc, vi(l1)))
    kiem("tu choi khong ly do → 400", requests.post(G + f"/ledger/admin/withdrawals/{w3}/reject", headers=admin, json={"reason": ""}).status_code == 400)
    kiem("tu choi co ly do", requests.post(G + f"/ledger/admin/withdrawals/{w3}/reject", headers=admin, json={"reason": "Sai so tai khoan"}).status_code == 200)
    kiem("tu choi → but toan dao, tien ve vi (goc - 30.000)", cho(lambda: vi(l1)["availableVnd"] == goc - 30000, 15), (goc, vi(l1)))
    ls = ds(requests.get(G + "/ledger/withdrawals/mine", headers=l1["h"]).json())
    kiem("lich su rut 3 lenh", len(ls) == 3, ls)
    gd = requests.get(G + "/ledger/me/transactions?pageSize=50", headers=l1["h"]).json()
    kiem("so giao dich vi co ban ghi", len(ds(gd)) >= 5, str(gd)[:300])

    # =================================================================================
    print("\n[9] Cua so ngau nhien khi lay task (task.lease_candidate_window)")
    C, _ = tao_du_an(biz, "Cua so", {f"c{i:02d}": f"Mau cua so {i}" for i in range(40)}, 3, 3, 1000)
    publish_va_duyet(biz, C)
    for u in lab.values():
        requests.post(G + f"/projects/{C}/join", headers=u["h"])

    def mot_vong():
        ds_t = []
        for u in lab.values():
            t = nhan_task(u, C)
            ds_t.append(t)
        for u, t in zip(lab.values(), ds_t):
            requests.post(G + f"/tasks/assignments/{t['assignmentId']}/release", headers=u["h"])
        return len({t["taskId"] for t in ds_t})

    dat_va_cho("task.lease_candidate_window", 1, ("task",))
    d1 = [mot_vong() for _ in range(3)]
    kiem("cua so 1 (FIFO): 4 nguoi lien tiep → 2 task (3 nguoi chung task dau)", d1 == [2, 2, 2], d1)
    dat_va_cho("task.lease_candidate_window", 32, ("task",))
    d32 = [mot_vong() for _ in range(6)]
    kiem("cua so 32: 4 nguoi phan tan (tong so task khac nhau >= 18/24)", sum(d32) >= 18, d32)
    kiem("cua so ngoai 1..1000 → 400", dat("task.lease_candidate_window", 0).status_code == 400)

    # =================================================================================
    print("\n[10] Chinh sach posterior / voi voi labeler moi (do chinh xac = tien nghiem 0,7)")

    def chay_chinh_sach(ten_cs, dat_them):
        dat_va_cho("quality.redundancy_policy", ten_cs, ("quality",))
        for kk, vv in dat_them.items():
            dat_va_cho(kk, vv, ("quality",))
        moi = [dang_ky(["labeler"], f"{ten_cs}{i}") for i in range(4)]
        P, ids = tao_du_an(biz, f"CS {ten_cs}", {"q1": f"Mau chinh sach {ten_cs}"}, 2, 4, 1000)
        publish_va_duyet(biz, P)
        tid = None
        nguong = float(requests.get(G + "/admin/settings/quality.posterior_target", headers=admin).json()["value"])
        R = float(requests.get(G + "/admin/settings/quality.voi_value_ratio", headers=admin).json()["value"])
        da_nop = 0
        for u in moi:
            requests.post(G + f"/projects/{P}/join", headers=u["h"]).raise_for_status()
            t = nhan_task(u, P, 20)
            if t is None:
                break
            tid = t["taskId"]
            nop(u, t["assignmentId"], "a").raise_for_status()
            da_nop += 1
            if da_nop < 2:
                continue
            hau = rd.hau_nghiem(["a"] * da_nop, [0.7] * da_nop, ["a", "b"])
            if ten_cs == "posterior":
                mua = max(hau.values()) < nguong
            else:
                mua = rd.dang_mua_them(hau, 0.7, R, 4 - da_nop)
            if da_nop == 4:
                mong = "disputed" if mua else "agreed"
            else:
                mong = "more" if mua else "agreed"
            if mong == "more":
                ok = cho(lambda: sql("task", f"select redundancy_target from tasks where id='{tid}'") == str(da_nop + 1), 30)
                kiem(f"{ten_cs}: {da_nop} nhan trung (P={max(hau.values()):.3f}) → xin them nguoi", ok,
                     sql("quality", f"select target, status from consensus_rounds where task_id='{tid}'"))
            else:
                ok = cho(lambda: sql("quality", f"select status from consensus_rounds where task_id='{tid}' and target={da_nop}") == mong, 30)
                kiem(f"{ten_cs}: {da_nop} nhan trung (P={max(hau.values()):.3f}) → {mong}", ok,
                     sql("quality", f"select target, status from consensus_rounds where task_id='{tid}'"))
                return da_nop
        return da_nop

    n_post = chay_chinh_sach("posterior", {"quality.posterior_target": 0.95})
    kiem("posterior τ 0,95 voi labeler moi can 4 nhan (P: 0,845 → 0,927 → 0,967)", n_post == 4, n_post)
    n_voi = chay_chinh_sach("voi", {"quality.voi_value_ratio": 20})
    kiem("voi chay het vong quyet dinh", n_voi >= 2, n_voi)
    r = dat("quality.redundancy_policy", "ngau_nhien")
    kiem("chinh sach ngoai danh sach → 400", r.status_code == 400, r.text)
    kiem("labeler doi setting → 403", requests.put(G + "/admin/settings/quality.redundancy_policy", headers=l1["h"],
                                                   json={"value": "voi", "reason": "x"}).status_code == 403)

    # =================================================================================
    print("\n[11] Quan ly du an dang chay: tam dung, chan thanh vien, hoan thanh → hoan ky quy")
    r = requests.post(G + f"/projects/{C}/pause", headers=biz["h"])
    kiem("tam dung du an", r.status_code == 200, r.text)
    kiem("dang tam dung → next 403 du_an_khong_chay",
         cho(lambda: "du_an_khong_chay" in requests.post(G + f"/tasks/projects/{C}/next", headers=l1["h"]).text, 15))
    kiem("nguoi khac tam dung → 404", requests.post(G + f"/projects/{C}/resume", headers=biz2["h"]).status_code == 404)
    requests.post(G + f"/projects/{C}/resume", headers=biz["h"]).raise_for_status()
    t = nhan_task(l1, C, 20)
    kiem("chay tiep → nhan duoc task", t is not None)
    if t:
        requests.post(G + f"/tasks/assignments/{t['assignmentId']}/release", headers=l1["h"])
    r = requests.post(G + f"/projects/{C}/members/{l2['id']}/block", headers=biz["h"])
    kiem("chan lab2", r.status_code in (200, 204), r.text)
    kiem("bi chan → next 403 bi_chan_khoi_du_an",
         cho(lambda: "bi_chan_khoi_du_an" in requests.post(G + f"/tasks/projects/{C}/next", headers=l2["h"]).text, 15))
    requests.post(G + f"/projects/{C}/members/{l2['id']}/unblock", headers=biz["h"])
    kiem("bo chan → nhan lai duoc task", cho(lambda: requests.post(G + f"/tasks/projects/{C}/next", headers=l2["h"]).status_code == 200, 15))
    # ---- DONG DU AN (phuong an A): tam dung → het luot dang lam → duyet het → het han / xu ly khieu nai ----
    print("\n[11a] Dong du an: chi khi khong con viec nao sinh tien cho labeler")

    def dong(p, hanh_dong="complete"):
        return requests.post(G + f"/projects/{p}/{hanh_dong}", headers=biz["h"], json={"reason": "e2e"})

    def ly_do(r):
        try:
            return r.json().get("closeCheck", {}).get("reasons", [])
        except ValueError:
            return []

    # lab2 dang giu mot luot (buoc bo chan o tren); lab1 nhan them mot luot.
    t1 = nhan_task(l1, C, 20)
    r = dong(C)
    kiem("dang chay → hoan thanh 409 can_tam_dung_truoc", r.status_code == 409 and "can_tam_dung_truoc" in r.text, r.text)
    r = dong(C, "cancel")
    kiem("dang chay → huy 409 can_tam_dung_truoc", r.status_code == 409 and "can_tam_dung_truoc" in r.text, r.text)
    requests.post(G + f"/projects/{C}/pause", headers=biz["h"]).raise_for_status()
    # Luu y: requests.Response co bool = False voi ma 4xx → cho() phai tra JSON, khong tra Response.
    j = cho(lambda: (lambda x: x.json() if "con_luot_dang_lam" in ly_do(x) else None)(dong(C)), 15)
    kiem("tam dung nhung con 2 luot dang lam → 409 con_luot_dang_lam", j is not None and j["closeCheck"]["activeLeases"] == 2,
         dong(C).text)
    mine2 = requests.get(G + "/tasks/assignments/mine", headers=l2["h"]).json()
    t2 = next(x for x in mine2 if x["projectId"] == C)
    kiem("dang tam dung van NOP NOT duoc luot dang giu", nop(l1, t1["assignmentId"], "a").status_code == 200 and nop(l2, t2["assignmentId"], "b").status_code == 200)
    j = cho(lambda: (lambda x: x.json() if "con_nhan_cho_duyet" in ly_do(x) else None)(dong(C)), 20)
    kiem("2 nhan cho duyet → 409 con_nhan_cho_duyet, kem so lieu", j is not None and j["closeCheck"]["pendingReview"] == 2, dong(C).text)
    nhan_c = [a for a in ds_nhan(biz, C) if a["status"] == "pendingReview"]
    x1 = next(a for a in nhan_c if a["labelerId"] == l1["id"])
    x2 = next(a for a in nhan_c if a["labelerId"] == l2["id"])
    for a in (x1, x2):
        requests.post(G + f"/annotations/{a['id']}/reject", headers=biz["h"], json={"reason": "Sai"}).raise_for_status()
    r = dong(C)
    kiem("tu choi het roi dong ngay → 409 con_han_khieu_nai (chong lach khieu nai)",
         r.status_code == 409 and "con_han_khieu_nai" in ly_do(r) and r.json()["closeCheck"]["rejectedInAppealWindow"] == 2
         and r.json()["closeCheck"]["appealWindowEndsAt"], r.text)
    requests.post(G + f"/annotations/{x2['id']}/appeal", headers=l2["h"], json={"message": "Xem lai giup"}).raise_for_status()
    r = dong(C)
    kiem("khieu nai dang mo → 409 con_khieu_nai", r.status_code == 409 and "con_khieu_nai" in ly_do(r), r.text)
    requests.post(G + f"/annotations/{x2['id']}/appeal/resolve", headers=admin, json={"accept": True, "note": "Dung"}).raise_for_status()
    dat_va_cho("annotation.appeal_window", 0, ("annotation",))
    truoc = vi(biz)["businessAvailableVnd"]
    r = dong(C)
    kiem("het han khieu nai + da phan xu → hoan thanh 200", r.status_code == 200, r.text)
    dat_va_cho("annotation.appeal_window", 604800, ("annotation",))
    kiem("phan ky quy chua dung tra ve vi doanh nghiep", cho(lambda: vi(biz)["businessAvailableVnd"] > truoc, 20), (truoc, vi(biz)))
    kiem("lab2 duoc tra tien nhan khieu nai thang", cho(lambda: sql("ledger", f"select count(*) from holds where annotation_id='{x2['id']}'") == "1", 20))
    kiem("annotation-svc da dong so du an C",
         sql("annotation", f"select closed_at is not null and is_final from project_terms where project_id='{C}'") == "t")
    r = requests.post(G + f"/annotations/{x2['id']}/approve", headers=biz["h"])
    kiem("duyet sau khi dong → 409 du_an_da_ket_thuc", r.status_code == 409 and "du_an_da_ket_thuc" in r.text, r.text)
    r = requests.post(G + f"/annotations/{x1['id']}/appeal", headers=l1["h"], json={"message": "muon"})
    kiem("khieu nai sau khi dong → 409 du_an_da_ket_thuc", r.status_code == 409 and "du_an_da_ket_thuc" in r.text, r.text)
    r = requests.post(G + f"/annotations/projects/{C}/approve-agreed", headers=biz["h"])
    kiem("duyet hang loat sau khi dong → 409", r.status_code == 409, r.text)
    kiem("du an da xong → next 403", cho(lambda: requests.post(G + f"/tasks/projects/{C}/next", headers=l1["h"]).status_code == 403, 15))
    kiem("hoan thanh lai → 409", dong(C).status_code == 409)
    kiem("chay tiep du an da xong → 409", requests.post(G + f"/projects/{C}/resume", headers=biz["h"]).status_code == 409)
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai van lanh sau khi dong", rec.get("healthy") is True, rec)

    # Huy: du an dang chay khong co nhan nao → tam dung → huy duoc, hoan ky quy.
    H, _ = tao_du_an(biz, "Huy", {"h1": "x", "h2": "y"}, 1, 1, 10000)
    publish_va_duyet(biz, H)
    truoc = vi(biz)["businessAvailableVnd"]
    kiem("huy du an dang chay → 409 can_tam_dung_truoc", dong(H, "cancel").status_code == 409)
    requests.post(G + f"/projects/{H}/pause", headers=biz["h"]).raise_for_status()
    j = cho(lambda: (lambda x: x.json() if x.status_code == 200 else None)(dong(H, "cancel")), 15)
    kiem("tam dung, khong co nhan → huy 200", j is not None and j["status"] == "cancelled", dong(H, "cancel").text)
    kiem("huy → hoan ky quy", cho(lambda: vi(biz)["businessAvailableVnd"] > truoc, 20), (truoc, vi(biz)))

    # Endpoint noi bo: khong qua gateway, can khoa.
    kiem("endpoint noi bo qua gateway → 404", requests.get(G + f"/internal/projects/{C}/close-check").status_code == 404)
    kiem("endpoint noi bo khong khoa → 404", requests.get(f"{URL_TASK}/internal/projects/{C}/close-check").status_code == 404)
    kiem("endpoint noi bo sai khoa → 404", requests.post(f"{URL_ANNOTATION}/internal/projects/{C}/close",
                                                        headers={"X-Internal-Key": "sai"}, json={"submittedCount": 0}).status_code == 404)
    kiem("biz2 xem nhan du an A → 404", requests.get(G + f"/annotations/projects/{A}", headers=biz2["h"]).status_code == 404)
    kiem("labeler xem tong ket chat luong → 404", requests.get(G + f"/quality/projects/{A}/summary", headers=l1["h"]).status_code == 404)
    kiem("chu du an xem tong ket chat luong", requests.get(G + f"/quality/projects/{A}/summary", headers=biz["h"]).status_code == 200)
    kiem("labeler xem chat luong cua minh", requests.get(G + "/quality/me", headers=l1["h"]).status_code == 200)

    # =================================================================================
    print("\n[11b] Kiem dinh dang nhan: span van ban, so sanh cap (du an moi)")
    l1 = lab["lab1"]

    def du_an_cong_cu(modality, cong_cu, rows):
        r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-tong] {modality} {TAG}", "description": "e2e", "modality": modality, "visibility": "public"})
        p = r.json()["id"]
        r = requests.put(G + f"/projects/{p}/label-schema", headers=biz["h"], json={"modality": modality, "tools": cong_cu})
        assert r.status_code == 200, r.text
        requests.put(G + f"/projects/{p}/guideline", headers=biz["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
        requests.put(G + f"/projects/{p}/pricing", headers=biz["h"], json={"unitPriceVnd": 1000, "redundancy": 1, "maxRedundancy": 1,
                                                                          "budgetVnd": 20000, "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
        requests.post(G + f"/projects/{p}/datasets/manifest", headers=biz["h"], json={"name": "lo", "rows": rows}).raise_for_status()
        cho(lambda: [d for d in requests.get(G + f"/projects/{p}/datasets", headers=biz["h"]).json() if d["status"] == "ready"], 40)
        publish_va_duyet(biz, p)
        requests.post(G + f"/projects/{p}/join", headers=l1["h"]).raise_for_status()
        return p

    def nop_tho(aid, payload):
        return requests.post(G + f"/tasks/assignments/{aid}/submit", headers=l1["h"], json={"payload": payload})

    VB = du_an_cong_cu("text", [{"name": "cam_xuc", "kind": "classification", "classes": ["tich_cuc", "tieu_cuc"]},
                                {"name": "thuc_the", "kind": "span", "classes": ["DIA_DIEM", "TEN"], "required": False}],
                       [{"text": "Toi o Ha Noi", "name": "v1"}, {"text": "Ban Lan rat vui", "name": "v2"}])
    t = nhan_task(l1, VB)
    n = len(t["content"]["text"])
    kiem("span vuot do dai van ban → 400", nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "TEN", "start": 0, "end": n + 1}]}).status_code == 400)
    kiem("span start >= end → 400", nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "TEN", "start": 3, "end": 3}]}).status_code == 400)
    kiem("span nhan la → 400", nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "XYZ", "start": 0, "end": 2}]}).status_code == 400)
    kiem("thieu cong cu bat buoc → 400", nop_tho(t["assignmentId"], {"thuc_the": []}).status_code == 400)
    kiem("cong cu khong co trong tap nhan → 400", nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "la": {}}).status_code == 400)
    r = nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "DIA_DIEM", "start": 0, "end": min(3, n)}]})
    kiem("span hop le → 200", r.status_code == 200, r.text)
    t = nhan_task(l1, VB)
    r = nop_tho(t["assignmentId"], {"cam_xuc": {"labelIds": ["tieu_cuc"]}})
    kiem("bo trong cong cu khong bat buoc → 200", r.status_code == 200, r.text)

    CAP = du_an_cong_cu("pair", [{"name": "tot_hon", "kind": "pairwise", "allowTie": False},
                                 {"name": "an_toan", "kind": "classification", "classes": ["an_toan", "khong_an_toan"]}],
                        [{"prompt": "Thu do Viet Nam?", "a": "Ha Noi", "b": "Hue", "name": "c1"}])
    t = nhan_task(l1, CAP)
    kiem("task cap co content a / b", t is not None and t["content"].get("a") and t["content"].get("b"), t)
    kiem("choice ngoai a/b/tie → 400", nop_tho(t["assignmentId"], {"tot_hon": {"choice": "c"}, "an_toan": {"labelIds": ["an_toan"]}}).status_code == 400)
    kiem("tie khi allowTie=false → 400", nop_tho(t["assignmentId"], {"tot_hon": {"choice": "tie"}, "an_toan": {"labelIds": ["an_toan"]}}).status_code == 400)
    r = nop_tho(t["assignmentId"], {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}})
    kiem("so sanh cap hop le → 200", r.status_code == 200, r.text)

    # =================================================================================
    print("\n[12] Kiem tra tong")
    time.sleep(5)
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai lanh (can bang, khop so du, khong am, chuoi bam nguyen ven)",
         rec.get("healthy") is True and all(not v for v in rec.values() if isinstance(v, list)), rec)
    kiem("labeler khong xem doi soat → 403", requests.get(G + "/ledger/admin/reconciliation", headers=l1["h"]).status_code == 403)

finally:
    print("\n[13] Tra setting ve gia tri ban dau")
    for key, v in GOC.items():
        r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e tong the: tra lai"})
        if r.status_code != 200:
            print("  KHONG tra lai duoc", key, r.text)
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
