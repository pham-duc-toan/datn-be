# E2E CHI SO CHAT LUONG (FQ-02) + KHONG BAN SAO (kien truc 3.22):
#   Krippendorff alpha cho MOI loai cong cu (nominal / interval / set_f1), Fleiss' kappa cho cong cu
#   chon mot gia tri, STAPLE (do nhay / dac hieu) cho mat na trong bao cao labeler;
#   quality-svc khong giu ban sao de bai / bai nop / nhan — doc qua gRPC tu project-svc; project-svc tat dung
#   luc quality nhan "dong thu bai" → yeu cau cham nam cho, project-svc chay lai thi worker cham (khong DLQ).
# Chay: python e2e_chi_so_chat_luong.py   (stack demo: E2E_PROFILE=demo)
import os, subprocess, sys
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
from moi_truong import TIEN_TO_CONTAINER  # noqa: E402
import e2e_cong_cu_helpers as h  # noqa: E402

DEMO = os.environ.get("E2E_PROFILE", "dev").lower() == "demo"


def docker(*lenh):
    subprocess.run(["docker", *lenh], check=True, capture_output=True)


def lam_tat_ca(u, pid, payload_cua):
    """Lam ca bai; payload_cua(sampleId) → nhan. Nop ca bai, tra so cau da tra loi."""
    b = bat_dau(u, pid, 20)
    for q in b["questions"]:
        luu(u, b["attemptId"], q["sampleId"], payload_cua(q["sampleId"])).raise_for_status()
    r = nop_bai(u, b["attemptId"])
    assert r.status_code == 200, r.text
    return r.json()["answeredCount"]


try:
    biz = dang_ky(["business"], "chiso")
    h.nap(biz, 3000000)
    L = [dang_ky(["labeler"], f"chiso-l{i}") for i in range(3)]

    # =================================================================================
    print("\n[1] Chi so theo loai cong cu: phan loai, so, khung, mat na")
    anh = [h.anh_mau(f"chiso-{m}.png", m, 40, 20) for m in ("navy", "olive", "maroon", "silver")]
    tap_nhan = {"modality": "image", "tools": [
        {"name": "lop", "kind": "classification", "classes": ["a", "b"]},
        {"name": "so", "kind": "number", "min": 0, "max": 1000},
        {"name": "khung", "kind": "bbox", "classes": ["xe"]},
        {"name": "vung", "kind": "mask", "classes": ["duong"]},
    ]}
    P, _, mau = h.du_an(biz, "Chi so", "image", tap_nhan, files=anh, red=3, tran=3)
    publish_va_duyet(biz, P)
    thu_tu = {m["id"]: i for i, m in enumerate(sorted(mau.values(), key=lambda m: m["id"]))}

    def payload(nguoi_kem):
        def f(sid):
            i = thu_tu[sid]
            return {
                "lop": {"labelIds": ["a" if i % 2 else "b"]},
                "so": {"value": 100 * i + (37 if nguoi_kem and i % 2 == 0 else 0)},
                "khung": [{"labelId": "xe", "x": 2 + 3 * i, "y": 2, "w": 10, "h": 8}],
                # 40x20, theo cot: nguoi tot to pixel 100..299, nguoi kem chi to nua sau (200..299).
                "vung": [{"labelId": "duong", "rle": {"size": [20, 40], "counts": [200, 100, 500] if nguoi_kem else [100, 200, 500]}}],
            }
        return f

    for k, u in enumerate(L):
        tham_gia(u, P)
        kiem(f"labeler {k} lam du 4 mau", lam_tat_ca(u, P, payload(k == 2)) == 4)

    # attempt.submitted (tin hieu) da toi quality → du an duoc danh dau can tinh DS; nhan doc qua gRPC luc chay lo.
    cho(lambda: sql("quality", f"select count(*) from ds_state where project_id='{P}'") == "1", 30)
    requests.post(G + "/quality/admin/dawid-skene/run", headers=admin).raise_for_status()

    def chi_so_du():
        tq = requests.get(G + f"/quality/projects/{P}/summary", headers=biz["h"]).json()
        cc = {t["tool"]: t for t in tq.get("tools", [])}
        return cc if len(cc) == 4 else None
    cc = cho(chi_so_du, 60)
    kiem("bao cao co chi so cho ca 4 cong cu", cc is not None, cc)
    cc = cc or {}
    kiem("phan loai: nominal, kappa = alpha ~ 1 (ca 3 deu dong y)",
         cc["lop"]["alphaMetric"] == "nominal" and cc["lop"]["fleissKappa"] is not None and cc["lop"]["krippendorffAlpha"] > 0.9, cc.get("lop"))
    kiem("so: interval, khong co kappa, alpha < 1 (nguoi kem lech 37 o 2 mau)",
         cc["so"]["alphaMetric"] == "interval" and cc["so"]["fleissKappa"] is None and 0 < cc["so"]["krippendorffAlpha"] < 1, cc.get("so"))
    kiem("khung: set_f1, alpha co gia tri", cc["khung"]["alphaMetric"] == "set_f1" and cc["khung"]["krippendorffAlpha"] is not None, cc.get("khung"))
    kiem("mat na: set_f1, 4 mau, 12 nhan", cc["vung"]["alphaMetric"] == "set_f1" and cc["vung"]["itemCount"] == 4 and cc["vung"]["labelCount"] == 12, cc.get("vung"))

    lb = {d["labelerId"]: d for d in requests.get(G + f"/quality/projects/{P}/labelers", headers=biz["h"]).json()}
    st = [lb.get(u["id"], {}).get("stapleMask", {}).get("vung") for u in L]
    kiem("STAPLE: ca 3 nguoi co do nhay / dac hieu tren mat na", all(s is not None and s["sampleCount"] == 4 for s in st), st)
    if all(st):
        kiem("STAPLE: nguoi to thieu co do nhay thap hon han", st[2]["sensitivity"] < 0.7 < st[0]["sensitivity"], st)
        kiem("STAPLE: to thieu (khong to lan) → do dac hieu van cao", st[2]["specificity"] > 0.9, st)
    if all(st):
        kiem("STAPLE: nguoi tot do dac hieu ~ 1 (nen ca anh duoc tinh)", st[0]["specificity"] > 0.95, st)
    # 4 nhan / nguoi < quality.ds_min_labels → DS chua cham ai; mat na thi khong bao gio vao DS.
    kiem("mat na khong vao Dawid-Skene", all("vung" not in d.get("dawidSkeneSkill", {}) for d in lb.values()), lb)

    # =================================================================================
    print("\n[2] Khong ban sao: cham doc bai nop qua gRPC; project-svc tat luc dong thu bai → cham lai sau")
    bang = sql("quality", "select string_agg(table_name, ',' order by table_name) from information_schema.tables "
                          "where table_schema = 'public' and table_name in ('projects', 'attempts', 'labels', "
                          "'project_collections', 'fraud_submissions', 'reputation_baseline')")
    kiem("quality khong con bang ban sao (projects / attempts / labels / fraud_submissions ...)", bang == "", bang)
    Q, _ = tao_du_an(biz, "cham qua grpc", {f"c{i}": f"cau so {i} {TAG}" for i in range(3)}, 2, 2, 1000)
    publish_va_duyet(biz, Q)
    for u in L[:2]:
        tham_gia(u, Q)
        lam_het(u, Q, lambda _t: "a")
    tham_gia(L[2], Q)
    if DEMO:
        QUALITY, PROJECT = f"{TIEN_TO_CONTAINER}quality", f"{TIEN_TO_CONTAINER}project"
        docker("stop", QUALITY)  # bai cuoi + "dong thu bai" nam cho trong queue cua quality
        try:
            lam_het(L[2], Q, lambda _t: "a")
            # Outbox cua project phai GUI XONG "dong thu bai" truoc khi tat — khong thi event chi di sau khi
            # project chay lai, quality cham ngay (khong bao gio thay trang thai cho).
            kiem("project-svc da phat project.collection_finished",
                 cho(lambda: sql("project", "select count(*) from outbox where event_type = 'project.collection_finished' "
                                            f"and envelope_json->'payload'->>'projectId' = '{Q}' and published_at is not null") == "1", 30))
            docker("stop", PROJECT)
            docker("start", QUALITY)
            kiem("project-svc tat: quality ghi yeu cau cham, CHUA cham (gRPC khong goi duoc), khong vao DLQ",
                 cho(lambda: sql("quality", f"select count(*) from project_gradings where project_id='{Q}' and graded_at is null") == "1", 60),
                 sql("quality", f"select graded_at from project_gradings where project_id='{Q}'"))
        finally:
            docker("start", PROJECT)
            docker("start", QUALITY)
    else:
        lam_het(L[2], Q, lambda _t: "a")
    kiem("worker cham xong khi project-svc tra loi",
         cho(lambda: sql("quality", f"select count(*) from project_gradings where project_id='{Q}' and graded_at is not null") == "1", 120),
         sql("quality", f"select * from project_gradings where project_id='{Q}'"))
    tq = cho(lambda: (lambda r: r.json() if r.status_code == 200 and r.json().get("attemptsGraded", 0) == 3 else None)(
        requests.get(G + f"/quality/projects/{Q}/summary", headers=biz["h"])), 90)
    kiem("du suat → cham du 3 bai, 3 cau co da so, ket qua cuoi lay tu project-svc", tq is not None and tq["agreed"] == 3 and tq["attemptsPassed"] == 3, tq)
    kiem("bai vua nop van duoc xet gian lan sau khi project-svc chay lai (hang cho rong)",
         cho(lambda: sql("quality", f"select count(*) from pending_attempts where project_id='{Q}'") == "0", 60),
         sql("quality", f"select * from pending_attempts where project_id='{Q}'"))
finally:
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
