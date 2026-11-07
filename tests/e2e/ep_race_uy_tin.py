# EP RACE UY TIN: quality-svc cham NHIEU du an cua cung cac labeler SONG SONG (kien truc 3.18).
#
# Uy tin tinh tu bang chung TOAN CUC cua labeler (moi du an). Tu 3.22 bang chung nam tren project.attempts,
# quality doc tong qua gRPC (GetLabelerEvidence) — SAU khi project-svc ghi xong project.graded cua du an, trong
# worker (reputation_dirty, FOR UPDATE SKIP LOCKED). Neu tinh ngay luc cham (nhu truoc) thi tong doc ve con
# thieu chinh du an vua cham → agree_total thieu.
#
# Cach ep: K du an (moi du an 1 cau, 3 suat). l1, l2 nop truoc; l3 mo san K bai roi nop K bai CUNG LUC →
# bai thu ba cua moi du an dong thu bai → K lan cham dong thoi cho cung ba labeler.
# Bat bien: reputations.agree_total == tong comparable_count tren project.attempts cua labeler == K.
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
            # Hai database khac nhau: so agree_total (quality) voi tong tren project.attempts (nguon).
            q = sql("quality", "select string_agg(agree_total::text, ',' order by labeler_id) from reputations "
                               f"where labeler_id in ('{ids}')")
            p = sql("project", "select string_agg(t::text, ',' order by labeler_id) from (select labeler_id, "
                               "sum(comparable_count) t from attempts where graded_at is not null "
                               f"and labeler_id in ('{ids}') group by labeler_id) x")
            return f"{q}|{p}"

        mong = ",".join([str(K)] * 3) + "|" + ",".join([str(K)] * 3)
        ds_p = ",".join(f"'{p}'" for p in du_an)
        # Xong = moi du an da xep labeler vao hang tinh uy tin VA hang do da rong.
        du = cho(lambda: sql("quality", f"select count(*) from project_gradings where project_id in ({ds_p}) "
                                        "and reputation_at is not null") == str(K)
                 and sql("quality", f"select count(*) from reputation_dirty where labeler_id in ('{ids}')") == "0", 120)
        time.sleep(1.5)   # transaction cuoi cung commit xong
        kiem(f"vong {vong}: {K} du an cham song song, agree_total cua ca ba = {K} (khong dem thieu)",
             du is not None and khop() == mong, khop())
finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "ep race uy tin: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
