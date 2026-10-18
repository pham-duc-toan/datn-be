# EP RACE UY TIN: quality-svc cham NHIEU du an cua cung cac labeler SONG SONG (kien truc 3.18).
#
# Uy tin tinh tu bang chung TOAN CUC cua labeler (moi du an: attempt_grades). Khong khoa theo labeler thi
# moi transaction chi dem bai da commit + bai cua minh roi ghi de len nhau: agree_total bi thieu.
#
# Cach ep: K du an (moi du an 1 cau, 3 suat). l1, l2 nop truoc; l3 mo san K bai roi nop K bai CUNG LUC →
# bai thu ba cua moi du an dong thu bai → K lan cham dong thoi cho cung ba labeler.
# Bat bien: reputations.agree_total == tong comparable_count trong attempt_grades cua labeler == K.
# Chay: python ep_race_uy_tin.py [so_vong]   (stack demo: E2E_PROFILE=demo)
import os, sys
from concurrent.futures import ThreadPoolExecutor
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403

K = 8
SO_VONG = int(sys.argv[1]) if len(sys.argv) > 1 else 3


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


try:
    biz = dang_ky(["business"], "biz")
    nap(biz, 5_000_000)
    for vong in range(1, SO_VONG + 1):
        LAB = [dang_ky(["labeler"], f"{x}{vong}") for x in ("a", "b", "c")]
        du_an = []
        for i in range(K):
            P, _ = tao_du_an(biz, f"RACE-UT{vong}-{i}", {"m": f"Mau {vong}-{i}"}, thuong=10000, suat=3)
            publish_va_duyet(biz, P)
            du_an.append(P)
        for P in du_an:
            for u in LAB:
                tham_gia(u, P)
        for P in du_an:
            for u in LAB[:2]:
                assert lam_het(u, P, lambda _: "a") == 1
        # l3 mo san K bai (da tra loi), roi nop dong loat.
        bai = [lam_bai(LAB[2], P, lambda _: "a", nop=False)[0]["attemptId"] for P in du_an]
        with ThreadPoolExecutor(max_workers=K) as nhom:
            ma = list(nhom.map(lambda aid: nop_bai(LAB[2], aid).status_code, bai))
        assert all(m == 200 for m in ma), ma

        ids = "','".join(u["id"] for u in LAB)

        def khop():
            return sql("quality",
                       "select string_agg(r.agree_total || '/' || (select coalesce(sum(g.comparable_count), 0) from attempt_grades g "
                       f"where g.labeler_id = r.labeler_id), ',' order by r.labeler_id) from reputations r where r.labeler_id in ('{ids}')")

        mong = ",".join([f"{K}/{K}"] * 3)
        ds_p = ",".join(f"'{p}'" for p in du_an)
        du = cho(lambda: sql("quality", f"select count(*) from attempt_grades where project_id in ({ds_p})") == str(3 * K), 90)
        time.sleep(1.5)   # transaction cuoi cung commit xong
        kiem(f"vong {vong}: {K} du an cham song song, agree_total cua ca ba = {K} (khong dem thieu)",
             du is not None and khop() == mong, khop())
finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "ep race uy tin: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
