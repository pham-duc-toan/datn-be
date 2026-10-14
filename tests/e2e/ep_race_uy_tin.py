# EP RACE UY TIN: quality-svc quyet dinh NHIEU task cua cung mot labeler SONG SONG.
#
# Uy tin tinh tu bang chung TOAN CUC cua labeler (moi du an). Truoc khi co khoa theo labeler, moi
# transaction dem label_agreements chi thay dong cua minh + dong da commit, roi ghi de len nhau:
# agree_total bi thieu, uy tin sai toi lan cap nhat ke tiep cua labeler do.
#
# Cach ep: mot labeler chi giu duoc MOT task moi du an, nen dung K du an (moi du an 1 mau). l1 nop het
# truoc; l2 giu san K luot (moi du an mot luot) roi nop K luot CUNG LUC → K lan tinh dong thuan dong thoi
# cho cung hai labeler. Bat bien: reputations.agree_total == so dong label_agreements cua labeler == K.
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
        l1 = dang_ky(["labeler"], f"a{vong}")
        l2 = dang_ky(["labeler"], f"b{vong}")
        du_an = []
        for i in range(K):
            P, _ = tao_du_an(biz, f"RACE-UT{vong}-{i}", {"m": f"Mau {vong}-{i}"}, 2, 2, 10000)
            publish_va_duyet(biz, P)
            du_an.append(P)
        for P in du_an:
            tham_gia(l1, P)
            tham_gia(l2, P)
        for P in du_an:
            assert lam_het(l1, P, lambda _: "a") == 1
        # l2 giu san K luot, roi nop dong loat.
        luot = [nhan_task(l2, P, 20)["assignmentId"] for P in du_an]
        with ThreadPoolExecutor(max_workers=K) as nhom:
            ma = list(nhom.map(lambda aid: nop(l2, aid, "a").status_code, luot))
        assert all(m == 200 for m in ma), ma

        def khop():
            return sql("quality",
                       "select string_agg(r.agree_total || '/' || (select count(*) from label_agreements a where a.labeler_id = r.labeler_id), "
                       f"',' order by r.labeler_id) from reputations r where r.labeler_id in ('{l1['id']}','{l2['id']}')")

        mong = ",".join([f"{K}/{K}"] * 2)
        ds_p = ",".join(f"'{p}'" for p in du_an)
        du = cho(lambda: sql("quality", f"select count(*) from label_agreements where project_id in ({ds_p})") == str(2 * K), 60)
        time.sleep(1.5)   # transaction cuoi cung commit xong
        kiem(f"vong {vong}: {K} task quyet dinh song song, agree_total cua ca hai = {K} (khong dem thieu)",
             du is not None and khop() == mong, khop())
finally:
    for key, v in GOC.items():
        requests.put(G + f"/admin/settings/{key}", headers=admin, json={"value": v, "reason": "ep race uy tin: tra lai"})
    print(f"\nKET QUA: {KQ['pass']} pass, {KQ['fail']} fail")
    for x in KQ["loi"]:
        print("   - FAIL:", x)
