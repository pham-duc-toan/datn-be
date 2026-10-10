"""Sinh cac BANG so cho bao cao NC-D-01 tu ket-qua/ket-qua.json (khong chep tay so lieu).

In ra markdown; docs/thi-nghiem/nc-d-01-redundancy-thich-ung.md dan cac bang nay.
"""

import json
from pathlib import Path

KQ = json.loads((Path(__file__).resolve().parent / "ket-qua" / "ket-qua.json").read_text(encoding="utf-8"))

TEN = {"dog": "Dog", "face": "Face", "duck": "Duck", "nist-trec-relevance": "NIST TREC", "relevance-2": "Relevance-2"}


def pct(x):
    return "—" if x is None else f"{x * 100:.1f}"


def tk(x):
    return "không đạt" if x is None else f"{x['nhan']:.2f} (−{x['tiet_kiem'] * 100:.0f}%)" if x["tiet_kiem"] >= 0 else f"{x['nhan']:.2f} (+{-x['tiet_kiem'] * 100:.0f}%)"


def acc_tai(duong, c):
    duong = sorted(duong)
    for (c0, a0), (c1, a1) in zip(duong, duong[1:]):
        if c0 <= c <= c1:
            return a0 + (a1 - a0) * (c - c0) / (c1 - c0) if c1 > c0 else a1
    return None


def bang_du_lieu():
    print("| Bộ dữ liệu | Nguồn | Lớp | Mẫu dùng (đủ trần nhãn) | Người gán | Trần |")
    print("|---|---|---|---|---|---|")
    for k, v in KQ["benchmark"].items():
        m = v["mo_ta"]
        print(f"| {TEN[k]} | {m['nguon']} | {m['lua_chon']} | {v['muc_dung']} | {m['nguoi']} | {v['tran']} |")


def bang_tiet_kiem(moc):
    print("| Bộ dữ liệu | Cố định n | Độ chính xác cố định (%) | Posterior: nhãn cần (tiết kiệm) | VOI: nhãn cần (tiết kiệm) | P3 hiện tại |")
    print("|---|---|---|---|---|---|")
    for k, v in KQ["benchmark"].items():
        for t in v["tiet_kiem"]:
            if t["moc"] != moc:
                continue
            print(f"| {TEN[k]} | {t['co_dinh']} | {pct(t['acc_goc'])} | {tk(t['posterior'])} | {tk(t['voi'])} | {tk(t['p3'])} |")


def bang_cung_chi_phi():
    print("| Bộ dữ liệu | P3: độ chính xác @ số nhãn | Posterior cùng số nhãn | VOI cùng số nhãn | Độ chính xác cao nhất P3 / Posterior |")
    print("|---|---|---|---|---|")
    for k, v in KQ["benchmark"].items():
        r = v["ket_qua"]
        c = v["tran"] if v["tran"] % 2 == 1 else v["tran"] - 1
        p3 = r[f"p3-tran-{c}"]
        post = [(x["nhan"], x["acc"]) for kk, x in r.items() if kk.startswith("posterior-")]
        voi = [(x["nhan"], x["acc"]) for kk, x in r.items() if kk.startswith("voi-")]
        p3max = max(x["acc"] for kk, x in r.items() if kk.startswith("p3-"))
        pmax = max(a for _, a in post)
        print(f"| {TEN[k]} | {pct(p3['acc'])} @ {p3['nhan']:.2f} | {pct(acc_tai(post, p3['nhan']))} | {pct(acc_tai(voi, p3['nhan']))} | {pct(p3max)} / {pct(pmax)} |")


def bang_mo_phong():
    print("| K | Spammer | Cố định 5 (%) | Posterior tiết kiệm vs đa số | VOI tiết kiệm vs đa số | Posterior tiết kiệm vs trọng số | P3 |")
    print("|---|---|---|---|---|---|---|")
    for v in sorted(KQ["mo_phong"].values(), key=lambda v: (v["K"], v["spam"])):
        a = next(x for x in v["tiet_kiem"] if x["co_dinh"] == 5 and x["moc"] == "da-so")
        b = next(x for x in v["tiet_kiem"] if x["co_dinh"] == 5 and x["moc"] == "trong-so")
        print(f"| {v['K']} | {int(v['spam'] * 100)}% | {pct(a['acc_goc'])} | {tk(a['posterior'])} | {tk(a['voi'])} | {tk(b['posterior'])} | {tk(a['p3'])} |")


if __name__ == "__main__":
    for ten, f in (("du lieu", bang_du_lieu), ("tiet kiem da so", lambda: bang_tiet_kiem("da-so")),
                   ("tiet kiem trong so", lambda: bang_tiet_kiem("trong-so")), ("cung chi phi P3", bang_cung_chi_phi),
                   ("mo phong", bang_mo_phong)):
        print(f"\n<!-- {ten} -->")
        f()
