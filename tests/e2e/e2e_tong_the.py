# E2E TONG THE — tu tao tai khoan + du an moi moi lan chay (khong phu thuoc du lieu seed).
# Phu (kien truc 3.18 — du an lam trung tam): xac thuc, nap tien, vong doi du an + saga ky quy theo suat,
# bai test dau vao, lam bai (luu / sua / bo trong / bo bai / het suat / BOLA), kiem dinh dang luc nop,
# cham ca du an + thuong bai dat, khieu nai ca bai bi bac, chot du an → hoan suat thua, rut tien,
# setting gioi han (x, y, suat, thoi gian giu bai), tam dung / chan thanh vien / ket thuc som, va
# kiem tra tong (doi soat). Luong chinh chi tiet hon: e2e_du_an_trung_tam.py.
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
    print("\n[3] Vong doi du an: tao, gia CA DU AN, ky quy theo suat (saga), duyet, thieu tien")
    dat_va_cho("ledger.reward_hold", 0, ("ledger",))
    dat_va_cho("task.appeal_window", 15, ("task",))
    dat_va_cho("task.reaper_interval", 2, ("task",))
    MAU_A = {"s1": "Hang tot nhung giao cham", "s2": "Tuyet voi", "s3": "Qua te", "s4": "Rat hai long", "s5": "That vong", "s6": "On"}
    DUNG_A = {"Hang tot nhung giao cham": "a", "Tuyet voi": "a", "Qua te": "b", "Rat hai long": "a", "That vong": "b", "On": "a"}
    THUONG_A = 60000
    r = requests.post(G + "/projects", headers=lab["lab1"]["h"], json={"name": "x", "description": "x", "modality": "text", "visibility": "public"})
    kiem("labeler tao du an → 403", r.status_code == 403, r.text)
    A, mau_a = tao_du_an(biz, "Chinh", MAU_A, thuong=THUONG_A, suat=3, nguong=70)
    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"rewardVnd": 0, "slots": 3, "passThresholdPercent": 70,
                                                                          "deadline": "2027-12-31T00:00:00Z"})
    kiem("thuong 0 → 400 thuong_khong_hop_le", r.status_code == 400 and "thuong_khong_hop_le" in r.text, r.text)
    r = requests.put(G + f"/projects/{A}/pricing", headers=biz["h"], json={"rewardVnd": THUONG_A, "slots": 1, "passThresholdPercent": 70,
                                                                          "deadline": "2027-12-31T00:00:00Z"})
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    kiem("1 suat < toi thieu 3 → bi chan (400 hoac readiness thieu)",
         r.status_code == 400 or (not rdy["ready"] and rdy["missing"]), (r.status_code, r.text, rdy))
    dat_gia(biz, A, THUONG_A, 3, 70)
    rdy = requests.get(G + f"/projects/{A}/readiness", headers=biz["h"]).json()
    phi = rdy["platformFeePerPassVnd"]
    ky_quy_a = 3 * (THUONG_A + phi)
    kiem("ky quy = 3 suat x (60.000 + phi san), san sang publish", rdy["estimatedCostVnd"] == ky_quy_a and rdy["ready"] and phi > 0, rdy)
    kiem("biz2 sua du an cua biz → 404 (BOLA)", requests.put(G + f"/projects/{A}/guideline", headers=biz2["h"],
                                                             json={"markdown": "hack", "examples": []}).status_code == 404)
    kiem("labeler xem du an nhap cua nguoi khac → 404", requests.get(G + f"/projects/{A}", headers=lab["lab1"]["h"]).status_code == 404)
    requests.post(G + f"/projects/{A}/publish", headers=biz["h"]).raise_for_status()
    ok = cho(lambda: requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"] == "pendingApproval", 30)
    kiem("ky quy xong → cho admin duyet", ok, requests.get(G + f"/projects/{A}", headers=biz["h"]).text)
    kiem("vi doanh nghiep tru dung ky quy", cho(lambda: vi(biz)["businessAvailableVnd"] == 3000000 - ky_quy_a, 10), (ky_quy_a, vi(biz)))
    kiem("admin thay du an trong hang cho duyet", any(p["id"] == A for p in ds(requests.get(G + "/projects/pending-approval?pageSize=100", headers=admin).json())))
    kiem("labeler duyet du an → 403", requests.post(G + f"/projects/{A}/approve", headers=lab["lab1"]["h"]).status_code == 403)
    kiem("chu du an tu duyet → 403", requests.post(G + f"/projects/{A}/approve", headers=biz["h"]).status_code == 403)
    r = requests.post(G + f"/projects/{A}/approve", headers=admin)
    kiem("admin duyet → running", r.status_code == 200 and cho(lambda: requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"] == "running", 10), r.text)
    kiem("duyet lan hai → 409", requests.post(G + f"/projects/{A}/approve", headers=admin).status_code == 409)
    cho(lambda: requests.get(G + f"/tasks/projects/{A}/slots", headers=biz["h"]).status_code == 200, 30)

    T, _ = tao_du_an(biz2, "Thieu tien", {"t1": "x", "t2": "y"}, thuong=10000, suat=3)
    requests.post(G + f"/projects/{T}/publish", headers=biz2["h"]).raise_for_status()
    du = cho(lambda: (lambda p: p if p["status"] == "draft" and p.get("statusReason") else None)(requests.get(G + f"/projects/{T}", headers=biz2["h"]).json()), 30)
    kiem("biz2 khong co tien → ledger tu choi ky quy, du an ve nhap kem ly do", du is not None, requests.get(G + f"/projects/{T}", headers=biz2["h"]).text)
    R, _ = tao_du_an(biz, "Bi tu choi", {"r1": "x"}, thuong=10000, suat=3)
    truoc = vi(biz)["businessAvailableVnd"]
    requests.post(G + f"/projects/{R}/publish", headers=biz["h"]).raise_for_status()
    cho(lambda: requests.get(G + f"/projects/{R}", headers=biz["h"]).json()["status"] == "pendingApproval", 30)
    r = requests.post(G + f"/projects/{R}/reject", headers=admin, json={"reason": "Noi dung khong phu hop"})
    kiem("admin tu choi du an", r.status_code == 200, r.text)
    kiem("tu choi → hoan ky quy (compensation)", cho(lambda: vi(biz)["businessAvailableVnd"] == truoc, 20), (truoc, vi(biz)))

    # =================================================================================
    print("\n[4] Bai test dau vao (du an B)")
    MAU_B = {"b1": "Cau hoi test 1", "b2": "Cau hoi test 2", "b3": "Task that 3", "b4": "Task that 4"}
    B, mau_b = tao_du_an(biz, "Test dau vao", MAU_B, thuong=20000, suat=3,
                         dieu_kien={"minLevel": None, "minReputation": None, "requireEntranceTest": True, "entranceQuestionCount": 2, "entrancePassPercent": 100},
                         vang=[("b1", "a", "entranceTest"), ("b2", "b", "entranceTest")])
    publish_va_duyet(biz, B)
    l3 = lab["lab3"]
    r = requests.post(G + f"/projects/{B}/join", headers=l3["h"])
    kiem("join du an can test → 409 can_lam_test", r.status_code == 409 and "can_lam_test" in r.text, r.text)
    kiem("chua tham gia → mo bai 403", requests.post(G + f"/tasks/projects/{B}/attempt", headers=l3["h"]).status_code == 403)
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
    bb = bat_dau(l3, B)
    kiem("dau test → bai lam chi gom cau THAT (khong co cau test)",
         isinstance(bb, dict) and {q["sampleId"] for q in bb["questions"]} == {mau_b["b3"], mau_b["b4"]}, bb if isinstance(bb, dict) else bb.text)

    # =================================================================================
    print("\n[5] Lam bai (du an A, 3 suat): luu / sua / bo trong, kiem dinh dang luc nop, BOLA, bo bai, het suat")
    l1, l2, l4 = lab["lab1"], lab["lab2"], lab["lab4"]
    r = requests.post(G + f"/tasks/projects/{A}/attempt", headers=l1["h"])
    kiem("chua tham gia → 403 khong_phai_thanh_vien", r.status_code == 403 and "khong_phai_thanh_vien" in r.text, r.text)
    for u in (l1, l2, l3, l4):
        r = requests.post(G + f"/projects/{A}/join", headers=u["h"])
        assert r.status_code == 200, r.text
    kiem("join lan hai → khong loi (idempotent hoac 409)", requests.post(G + f"/projects/{A}/join", headers=l1["h"]).status_code in (200, 409))
    b1 = bat_dau(l1, A)
    kiem("mo bai: 6 cau, co noi dung, tap nhan, thuong ca du an", isinstance(b1, dict) and b1["questionCount"] == 6
         and b1["questions"][0]["content"]["text"] and b1["labelSchema"] and b1["rewardVnd"] == THUONG_A, b1)
    kiem("bam mo bai lan hai → CHINH bai dang lam", bat_dau(l1, A)["attemptId"] == b1["attemptId"])
    aid = b1["attemptId"]
    q = [x["sampleId"] for x in b1["questions"]]
    r = requests.put(G + f"/tasks/attempts/{aid}/answers/{q[0]}", headers=l1["h"], json={"data": "chuoi"})
    kiem("cau tra loi khong phai object → 400 cau_tra_loi_sai_dinh_dang", r.status_code == 400 and "cau_tra_loi_sai_dinh_dang" in r.text, r.text)
    luu(l1, aid, q[0], {"labelIds": ["a"]}).raise_for_status()
    luu(l1, aid, q[1], {"nhan": {"labelIds": ["khong_co"]}}).raise_for_status()
    luu(l1, aid, q[2], {"nhan": {"labelIds": ["a", "b"]}}).raise_for_status()
    r = nop_bai(l1, aid)
    sai = {x["sampleId"] for x in r.json().get("invalidAnswers", [])} if r.status_code == 400 else set()
    kiem("nop bai co 3 cau sai (thieu ten cong cu, lop la, chon hai lop) → 400 bai_chua_hop_le kem dung 3 cau",
         r.status_code == 400 and "bai_chua_hop_le" in r.text and sai == {q[0], q[1], q[2]}, r.text)
    kiem("bai van dang lam sau khi nop hong", requests.get(G + f"/tasks/attempts/{aid}", headers=l1["h"]).json()["state"] == "inProgress")
    kiem("lab2 luu cau vao bai cua lab1 → 404 (BOLA)", luu(l2, aid, q[0], {"nhan": {"labelIds": ["a"]}}).status_code == 404)
    kiem("lab2 nop bai cua lab1 → 404", nop_bai(l2, aid).status_code == 404)
    kiem("lab2 bo bai cua lab1 → 404", requests.post(G + f"/tasks/attempts/{aid}/abandon", headers=l2["h"]).status_code == 404)
    kiem("lab2 xem bai cua lab1 → 404", requests.get(G + f"/tasks/attempts/{aid}", headers=l2["h"]).status_code == 404)
    r = luu(l1, aid, q[0], None)
    kiem("xoa cau tra loi (bo trong) → answeredCount giam", r.status_code == 200 and r.json()["answeredCount"] == 2, r.text)
    r = requests.post(G + f"/tasks/attempts/{aid}/abandon", headers=l1["h"])
    kiem("lab1 bo bai → tra suat", r.status_code in (200, 204), r.text)
    kiem("luu cau vao bai da bo → 409 bai_da_ket_thuc", luu(l1, aid, q[3], {"nhan": {"labelIds": ["a"]}}).status_code == 409)
    b1 = bat_dau(l1, A)
    kiem("bo bai roi mo lai → bai MOI, chua tra loi cau nao", b1["attemptId"] != aid and b1["answeredCount"] == 0, b1)

    def tra_loi_l2(text):
        return "b" if text == MAU_A["s1"] else DUNG_A[text]

    # l1 dung het; l2 khac o s1 (5/6); l3 chon 'a' het (4/6) — nguong 70%.
    _, r = lam_bai(l1, A, lambda x: DUNG_A[x])
    kiem("lab1 nop ca bai 6/6", r.status_code == 200 and r.json()["answeredCount"] == 6, r.text)
    kiem("nop lai → 409", nop_bai(l1, b1["attemptId"]).status_code == 409)
    kiem("mo bai sau khi da nop → 409 da_nop_bai", "da_nop_bai" in requests.post(G + f"/tasks/projects/{A}/attempt", headers=l1["h"]).text)
    _, r = lam_bai(l2, A, tra_loi_l2)
    kiem("lab2 nop", r.status_code == 200, r.text)
    b3, _ = lam_bai(l3, A, lambda _: "a", nop=False)
    sl = requests.get(G + f"/tasks/projects/{A}/slots", headers=l4["h"]).json()
    kiem("suat: 3 suat, 2 da nop, 1 dang lam, het cho", sl["slots"] == 3 and sl["submitted"] == 2 and sl["available"] == 0, sl)
    r = requests.post(G + f"/tasks/projects/{A}/attempt", headers=l4["h"])
    kiem("nguoi thu tu mo bai → 409 het_suat", r.status_code == 409 and "het_suat" in r.text, r.text)
    t = requests.get(G + f"/tasks/projects/{A}/progress", headers=biz["h"]).json()
    kiem("tien do: 2 bai nop, 1 dang lam, dang mo", t["submitted"] == 2 and t["inProgress"] == 1 and t["phase"] == "open", t)
    kiem("labeler xem tien do → 404", requests.get(G + f"/tasks/projects/{A}/progress", headers=l1["h"]).status_code == 404)

    # =================================================================================
    print("\n[6] Du suat → cham MOT lan ca du an, thuong bai dat, khieu nai bi bac, chot → hoan suat thua")
    r = nop_bai(l3, b3["attemptId"])
    kiem("lab3 nop bai thu ba → du suat", r.status_code == 200, r.text)
    g1 = cho_cham(l1, A)
    g2 = cho_cham(l2, A)
    g3 = cho_cham(l3, A)
    kiem("lab1 dat 100%", g1 and g1["passed"] and g1["agreementPercent"] == 100, g1)
    kiem("lab2 dat 83% (lech s1)", g2 and g2["passed"] and g2["agreementPercent"] == 83, g2)
    kiem("lab3 truot 66% < 70%, duoc khieu nai", g3 and not g3["passed"] and g3["agreementPercent"] == 66 and g3["canAppeal"], g3)
    kiem("lab1, lab2 nhan thuong 60.000 vao so du kha dung", cho(lambda: vi(l1)["availableVnd"] == THUONG_A and vi(l2)["availableVnd"] == THUONG_A, 30),
         (vi(l1), vi(l2)))
    kiem("ledger: moi bai dat mot khoan ProjectReward (2)",
         sql("ledger", f"select count(*) from holds where project_id='{A}' and kind='ProjectReward'") == "2")
    kiem("ket qua chi gom nhan bai dat: 2 x 6 = 12",
         cho(lambda: requests.get(G + f"/annotations/projects/{A}?status=accepted&pageSize=1", headers=biz["h"]).json()["total"] == 12, 30))
    kiem("nhan bai truot bi loai (6)", requests.get(G + f"/annotations/projects/{A}?status=excluded&pageSize=1", headers=biz["h"]).json()["total"] == 6)
    res = requests.get(G + f"/annotations/projects/{A}/results", headers=biz["h"])
    kiem("ket qua du an 200, du 6 mau", res.status_code == 200 and res.json()["sampleCount"] == 6, res.text[:300])
    csv = requests.get(G + f"/annotations/projects/{A}/export?format=csv", headers=biz["h"])
    kiem("xuat CSV: tieu de + 6 dong", csv.status_code == 200 and len(csv.text.strip().splitlines()) == 7, csv.text[:200])
    kiem("xuat JSON", requests.get(G + f"/annotations/projects/{A}/export?format=json", headers=biz["h"]).status_code == 200)
    kiem("xuat COCO cho du an van ban → 400", requests.get(G + f"/annotations/projects/{A}/export?format=coco", headers=biz["h"]).status_code == 400)
    kiem("biz2 xem nhan du an A → 404", requests.get(G + f"/annotations/projects/{A}", headers=biz2["h"]).status_code == 404)
    kiem("labeler xem tong ket chat luong → 404", requests.get(G + f"/quality/projects/{A}/summary", headers=l1["h"]).status_code == 404)
    kiem("chu du an xem tong ket chat luong", requests.get(G + f"/quality/projects/{A}/summary", headers=biz["h"]).status_code == 200)
    kiem("labeler xem chat luong cua minh", requests.get(G + "/quality/me", headers=l1["h"]).status_code == 200)

    r = requests.post(G + f"/tasks/attempts/{b1['attemptId']}/appeal", headers=l1["h"], json={"message": "x"})
    kiem("khieu nai bai da dat → 409 bai_da_dat", r.status_code == 409 and "bai_da_dat" in r.text, r.text)
    r = requests.post(G + f"/tasks/attempts/{b3['attemptId']}/appeal", headers=l2["h"], json={"message": "x"})
    kiem("khieu nai bai cua nguoi khac → 404", r.status_code == 404, r.text)
    r = requests.post(G + f"/tasks/attempts/{b3['attemptId']}/appeal", headers=l3["h"], json={"message": ""})
    kiem("khieu nai rong → 400", r.status_code == 400, r.text)
    r = requests.post(G + f"/tasks/attempts/{b3['attemptId']}/appeal", headers=l3["h"], json={"message": "Toi nghi cau 'Qua te' la a"})
    kiem("lab3 khieu nai ca bai → open", r.status_code == 200 and r.json()["appealState"] == "open", r.text)
    hang = requests.get(G + "/tasks/admin/appeals?state=open", headers=admin).json()
    kiem("admin thay khieu nai", any(x["attemptId"] == b3["attemptId"] for x in ds(hang)), str(hang)[:300])
    kiem("labeler xem hang khieu nai → 403", requests.get(G + "/tasks/admin/appeals", headers=l3["h"]).status_code == 403)
    r = requests.post(G + f"/tasks/admin/attempts/{b3['attemptId']}/appeal/resolve", headers=admin, json={"accept": False, "note": "Da so dung"})
    kiem("admin bac khieu nai", r.status_code == 200 and r.json()["appealState"] == "rejected", r.text)
    r = requests.post(G + f"/tasks/admin/attempts/{b3['attemptId']}/appeal/resolve", headers=admin, json={"accept": True, "note": "x"})
    kiem("phan xu lai → 409 khong_co_khieu_nai_mo", r.status_code == 409 and "khong_co_khieu_nai_mo" in r.text, r.text)
    truoc = vi(biz)["businessAvailableVnd"]
    kiem("het han khieu nai → du an Completed",
         cho(lambda: requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"] == "completed", 90),
         requests.get(G + f"/projects/{A}", headers=biz["h"]).json()["status"])
    kiem("hoan 1 suat (bai truot) ve vi doanh nghiep", cho(lambda: vi(biz)["businessAvailableVnd"] == truoc + THUONG_A + phi, 30), (truoc, vi(biz)))
    kiem("lab3 khong nhan gi", vi(l3)["availableVnd"] == 0, vi(l3))

    # =================================================================================
    print("\n[7] Rut tien: toi thieu, so du, idempotency, tu duyet, admin duyet / tu choi")
    dat_va_cho("ledger.withdraw_min_vnd", 10000, ("ledger",))
    dat_va_cho("ledger.withdraw_auto_approve_max_vnd", 15000, ("ledger",))
    goc = vi(l1)["availableVnd"]
    kiem("so du goc lab1 = thuong mot bai dat", goc == THUONG_A, vi(l1))

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
    kiem("so giao dich vi co ban ghi", len(ds(gd)) >= 4, str(gd)[:300])

    # =================================================================================
    print("\n[8] Setting gioi han du an: nguong x <= y, thoi gian giu bai, RBAC")
    r = dat("project.pass_threshold_min", 96)
    kiem("x = 96 > y = 95 → 409 xung_dot_setting", r.status_code == 409 and "xung_dot_setting" in r.text, r.text)
    r = dat("task.attempt_duration", 60)
    kiem("thoi gian giu bai < 10 phut → 400", r.status_code == 400, r.text)
    kiem("labeler doi setting → 403", requests.put(G + "/admin/settings/project.pass_threshold_min", headers=l1["h"],
                                                   json={"value": 50, "reason": "x"}).status_code == 403)
    # T (cua biz2) van la nhap sau khi ky quy that bai.
    r = requests.put(G + f"/projects/{T}/pricing", headers=biz2["h"], json={"rewardVnd": 10000, "slots": 3, "passThresholdPercent": 50,
                                                                           "deadline": "2027-12-31T00:00:00Z"})
    rdy = requests.get(G + f"/projects/{T}/readiness", headers=biz2["h"]).json()
    kiem("doanh nghiep dat t = 50 < x = 60 → bi chan", r.status_code == 400 or not rdy["ready"], (r.status_code, r.text, rdy))

    # =================================================================================
    print("\n[9] Quan ly du an dang chay: tam dung, chan thanh vien, ket thuc som, huy")
    C, _ = tao_du_an(biz, "Quan ly", {f"c{i}": f"Mau quan ly {i}" for i in range(5)}, thuong=10000, suat=3)
    publish_va_duyet(biz, C)
    for u in (l1, l2, l3):
        requests.post(G + f"/projects/{C}/join", headers=u["h"])

    def mo(u):
        return requests.post(G + f"/tasks/projects/{C}/attempt", headers=u["h"])

    def ban_sao(trang_thai):
        """Doi project.paused / resumed toi task-svc (ban sao). Truoc do task-svc van coi du an dang chay."""
        return cho(lambda: sql("task", f"select status from project_snapshots where project_id='{C}'") == trang_thai, 20)

    r = requests.post(G + f"/projects/{C}/pause", headers=biz["h"])
    kiem("tam dung du an", r.status_code == 200, r.text)
    ban_sao("Paused")
    # l3 chua co bai o C (nguoi DANG co bai van mo lai duoc bai cua minh, chi khong nop duoc).
    kiem("dang tam dung → mo bai moi 403 du_an_khong_chay", cho(lambda: "du_an_khong_chay" in mo(l3).text, 15), mo(l3).text[:300])
    kiem("nguoi khac chay tiep → 404", requests.post(G + f"/projects/{C}/resume", headers=biz2["h"]).status_code == 404)
    requests.post(G + f"/projects/{C}/resume", headers=biz["h"]).raise_for_status()
    ban_sao("Running")
    kiem("chay tiep → mo bai duoc", cho(lambda: mo(l1).status_code in (200, 201), 15))
    r = requests.post(G + f"/projects/{C}/members/{l2['id']}/block", headers=biz["h"])
    kiem("chan lab2", r.status_code in (200, 204), r.text)
    kiem("bi chan → mo bai 403 bi_chan_khoi_du_an", cho(lambda: "bi_chan_khoi_du_an" in mo(l2).text, 15))
    requests.post(G + f"/projects/{C}/members/{l2['id']}/unblock", headers=biz["h"])
    kiem("bo chan → mo bai lai duoc", cho(lambda: mo(l2).status_code in (200, 201), 15))
    _, r = lam_bai(l1, C, lambda _: "a")
    kiem("lab1 nop bai", r.status_code == 200, r.text)
    bai2, _ = lam_bai(l2, C, lambda _: "a", nop=False)

    def dong(hanh_dong):
        return requests.post(G + f"/projects/{C}/{hanh_dong}", headers=biz["h"], json={"reason": "e2e"})

    kiem("huy du an da co bai nop → bi tu choi", dong("cancel").status_code in (400, 409))
    requests.post(G + f"/projects/{C}/pause", headers=biz["h"]).raise_for_status()
    ban_sao("Paused")
    kiem("dang tam dung: nop bai → 409 du_an_tam_dung (bai van giu)",
         cho(lambda: "du_an_tam_dung" in nop_bai(l2, bai2["attemptId"]).text, 15), nop_bai(l2, bai2["attemptId"]).text[:300])
    kiem("tam dung van khong huy duoc (co bai nop)", dong("cancel").status_code in (400, 409))
    requests.post(G + f"/projects/{C}/resume", headers=biz["h"]).raise_for_status()
    ban_sao("Running")
    kiem("chay tiep → nop duoc bai dang giu", cho(lambda: nop_bai(l2, bai2["attemptId"]).status_code == 200, 15),
         nop_bai(l2, bai2["attemptId"]).text[:300])
    r = dong("finish")
    kiem("ket thuc som (2/3 suat)", r.status_code in (200, 202), r.text)
    kiem("du an sang grading", cho(lambda: requests.get(G + f"/projects/{C}", headers=biz["h"]).json()["status"] in ("grading", "completed"), 30))
    kiem("ket thuc lai → bi tu choi", dong("finish").status_code in (400, 409))
    kiem("da dong thu bai → lab3 mo bai bi tu choi", cho(lambda: mo(l3).status_code in (403, 409), 15))
    x1, x2 = cho_cham(l1, C), cho_cham(l2, C)
    kiem("it hon 3 bai, khong co cau vang → ca hai dat", x1 and x2 and x1["passed"] and x2["passed"], (x1, x2))
    kiem("het han khieu nai → Completed", cho(lambda: requests.get(G + f"/projects/{C}", headers=biz["h"]).json()["status"] == "completed", 90))
    kiem("chay tiep du an da xong → 409", requests.post(G + f"/projects/{C}/resume", headers=biz["h"]).status_code == 409)
    kiem("annotation-svc da chot du an C",
         cho(lambda: sql("annotation", f"select closed_at is not null and is_final from project_terms where project_id='{C}'") == "t", 20))

    H, _ = tao_du_an(biz, "Huy", {"h1": "x", "h2": "y"}, thuong=10000, suat=3)
    publish_va_duyet(biz, H)
    truoc = vi(biz)["businessAvailableVnd"]
    kiem("huy du an dang chay (chua tam dung) → 409", requests.post(G + f"/projects/{H}/cancel", headers=biz["h"], json={"reason": "e2e"}).status_code == 409)
    requests.post(G + f"/projects/{H}/pause", headers=biz["h"]).raise_for_status()
    j = cho(lambda: (lambda x: x.json() if x.status_code == 200 else None)(
        requests.post(G + f"/projects/{H}/cancel", headers=biz["h"], json={"reason": "e2e"})), 30, 1)
    kiem("tam dung, chua co bai nop → huy 200", j is not None and j["status"] == "cancelled", j)
    kiem("huy → hoan ky quy", cho(lambda: vi(biz)["businessAvailableVnd"] > truoc, 20), (truoc, vi(biz)))

    # Endpoint noi bo: khong qua gateway, can khoa.
    kiem("endpoint noi bo qua gateway → 404", requests.get(G + f"/internal/projects/{C}/close-check").status_code == 404)
    kiem("endpoint noi bo khong khoa → 404", requests.get(f"{URL_TASK}/internal/projects/{C}/close-check").status_code == 404)

    # =================================================================================
    print("\n[10] Kiem dinh dang nhan luc NOP bai: span van ban, so sanh cap (du an moi)")

    def du_an_cong_cu(modality, cong_cu, rows):
        r = requests.post(G + "/projects", headers=biz["h"], json={"name": f"[e2e-tong] {modality} {TAG}", "description": "e2e", "modality": modality, "visibility": "public"})
        p = r.json()["id"]
        r = requests.put(G + f"/projects/{p}/label-schema", headers=biz["h"], json={"modality": modality, "tools": cong_cu})
        assert r.status_code == 200, r.text
        requests.put(G + f"/projects/{p}/guideline", headers=biz["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
        dat_gia(biz, p, 10000, 3)
        requests.post(G + f"/projects/{p}/datasets/manifest", headers=biz["h"], json={"name": "lo", "rows": rows}).raise_for_status()
        cho(lambda: [d for d in requests.get(G + f"/projects/{p}/datasets", headers=biz["h"]).json() if d["status"] == "ready"], 40)
        publish_va_duyet(biz, p)
        requests.post(G + f"/projects/{p}/join", headers=l1["h"]).raise_for_status()
        return p

    def thu(bai, sid, payload):
        """Luu payload cho cau sid roi nop: tra ma loi cua cau do (None = nop duoc)."""
        luu(l1, bai["attemptId"], sid, payload).raise_for_status()
        r = nop_bai(l1, bai["attemptId"])
        if r.status_code == 200:
            return None
        return next((x["code"] for x in r.json().get("invalidAnswers", []) if x["sampleId"] == sid), r.text)

    VB = du_an_cong_cu("text", [{"name": "cam_xuc", "kind": "classification", "classes": ["tich_cuc", "tieu_cuc"]},
                                {"name": "thuc_the", "kind": "span", "classes": ["DIA_DIEM", "TEN"], "required": False}],
                       [{"text": "Toi o Ha Noi", "name": "v1"}, {"text": "Ban Lan rat vui", "name": "v2"}])
    bai = bat_dau(l1, VB)
    q0, q1 = bai["questions"]
    n = len(q0["content"]["text"])
    luu(l1, bai["attemptId"], q1["sampleId"], {"cam_xuc": {"labelIds": ["tieu_cuc"]}}).raise_for_status()
    kiem("span vuot do dai van ban → bi chan", thu(bai, q0["sampleId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "TEN", "start": 0, "end": n + 1}]}) is not None)
    kiem("span start >= end → bi chan", thu(bai, q0["sampleId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "TEN", "start": 3, "end": 3}]}) is not None)
    kiem("span nhan la → bi chan", thu(bai, q0["sampleId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "XYZ", "start": 0, "end": 2}]}) is not None)
    kiem("thieu cong cu bat buoc → bi chan", thu(bai, q0["sampleId"], {"thuc_the": []}) is not None)
    kiem("cong cu khong co trong tap nhan → bi chan", thu(bai, q0["sampleId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "la": {}}) is not None)
    kiem("span hop le + cau kia bo trong cong cu khong bat buoc → nop 200",
         thu(bai, q0["sampleId"], {"cam_xuc": {"labelIds": ["tich_cuc"]}, "thuc_the": [{"labelId": "DIA_DIEM", "start": 0, "end": min(3, n)}]}) is None)

    CAP = du_an_cong_cu("pair", [{"name": "tot_hon", "kind": "pairwise", "allowTie": False},
                                 {"name": "an_toan", "kind": "classification", "classes": ["an_toan", "khong_an_toan"]}],
                        [{"prompt": "Thu do Viet Nam?", "a": "Ha Noi", "b": "Hue", "name": "c1"}])
    bai = bat_dau(l1, CAP)
    qc = bai["questions"][0]
    kiem("cau so sanh cap co content a / b", qc["content"].get("a") and qc["content"].get("b"), qc)
    kiem("choice ngoai a/b/tie → bi chan", thu(bai, qc["sampleId"], {"tot_hon": {"choice": "c"}, "an_toan": {"labelIds": ["an_toan"]}}) is not None)
    kiem("tie khi allowTie=false → bi chan", thu(bai, qc["sampleId"], {"tot_hon": {"choice": "tie"}, "an_toan": {"labelIds": ["an_toan"]}}) is not None)
    kiem("so sanh cap hop le → nop 200", thu(bai, qc["sampleId"], {"tot_hon": {"choice": "a"}, "an_toan": {"labelIds": ["an_toan"]}}) is None)

    # =================================================================================
    print("\n[11] Kiem tra tong")
    time.sleep(5)
    rec = requests.get(G + "/ledger/admin/reconciliation", headers=admin).json()
    kiem("doi soat so cai lanh (can bang, khop so du, khong am, chuoi bam nguyen ven)",
         rec.get("healthy") is True and all(not v for v in rec.values() if isinstance(v, list)), rec)
    kiem("labeler khong xem doi soat → 403", requests.get(G + "/ledger/admin/reconciliation", headers=l1["h"]).status_code == 403)

finally:
    print("\n[12] Tra setting ve gia tri ban dau")
    for key, v in GOC.items():
        r = requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "e2e tong the: tra lai"})
        if r.status_code != 200:
            print("  KHONG tra lai duoc", key, r.text)
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
