# E2E CHI SO CHAT LUONG (FQ-02) + BU BAN SAO DU AN:
#   Krippendorff alpha cho MOI loai cong cu (nominal / interval / set_f1), Fleiss' kappa cho cong cu
#   chon mot gia tri, STAPLE (do nhay / dac hieu) cho mat na trong bao cao labeler;
#   quality-svc thieu ban sao du an → tu hoi project-svc (/internal/projects/{id}/published-terms) luc CHAM.
# Chay: python e2e_chi_so_chat_luong.py   (stack demo: E2E_PROFILE=demo)
import os, sys
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import *  # noqa: E402,F401,F403
import e2e_cong_cu_helpers as h  # noqa: E402


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

    cho(lambda: sql("quality", f"select count(*) from labels where project_id='{P}'") == "12", 30)
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
    print("\n[2] Bu ban sao du an: quality mat ban sao → tu hoi project-svc")
    Q, _ = tao_du_an(biz, "bu ban sao", {f"c{i}": f"cau so {i} {TAG}" for i in range(3)}, 2, 2, 1000)
    publish_va_duyet(biz, Q)
    kiem("quality nhan project.published", cho(lambda: sql("quality", f"select count(*) from projects where project_id='{Q}'") == "1", 30))
    sql("quality", f"delete from projects where project_id='{Q}'")
    kiem("xoa ban sao (gia lap du an publish truoc khi quality ton tai)", sql("quality", f"select count(*) from projects where project_id='{Q}'") == "0")
    for u in L:
        tham_gia(u, Q)
        lam_het(u, Q, lambda _t: "a")
    kiem("ban sao duoc bu lai tu project-svc", cho(lambda: sql("quality", f"select count(*) from projects where project_id='{Q}'") == "1", 30))
    kiem("label_schema / nguong dat (t) dung",
         sql("quality", f"select label_schema->'tools'->0->>'kind' || '/' || pass_threshold_percent from projects where project_id='{Q}'") == "classification/60")
    tq = cho(lambda: (lambda j: j if j.get("attemptsGraded", 0) == 3 else None)(requests.get(G + f"/quality/projects/{Q}/summary", headers=biz["h"]).json()), 60)
    kiem("du suat → cham du 3 bai, 3 cau co da so (khong bi bo qua)", tq is not None and tq["agreed"] == 3 and tq["attemptsPassed"] == 3, tq)
finally:
    print(f"\n== KET QUA: {KQ['pass']} PASS, {KQ['fail']} FAIL")
    for l in KQ["loi"]:
        print("   FAIL:", l)
