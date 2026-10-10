"""NC-D-01 — so cac chinh sach redundancy tren benchmark cong khai + mo phong.

Cach phat lai ("replay") cho moi muc co >= TRAN nhan that:
  1. 20% muc lam CAU VANG: uoc luong do chinh xac moi nguoi tren cac muc nay, lam muot ve tien
     nghiem 0.7 voi suc nang 2 (DUNG cong thuc uy tin cua quality-svc). Nguoi chua gap → 0.7.
     Danh gia chi tren 80% con lai — khong dung dap an cua muc dang danh gia.
  2. Moi muc: lay ngau nhien TRAN nhan, xao thu tu = thu tu "nguoi lam toi". Chinh sach doc dan
     tung nhan va quyet dinh dung hay mua them; chi phi = so nhan da doc.
  3. Lap lai voi nhieu hat giong; bao cao trung binh ± do lech chuan.

Chinh sach (ham quyet dinh lay tu services/quality/app/redundancy.py — cung code quality-svc chay):
  co-dinh-n        — luon n nhan, bo phieu da so (hoa → ngau nhien);
  co-dinh-n-trongso — luon n nhan, gop theo hau nghiem (trong so do chinh xac);
  p3-tran-C        — quy tac hien tai: bat dau 2, tranh chap thi them 1, toi tran C;
  posterior-τ      — bat dau 1, dung khi hau nghiem dan dau >= τ (toi tran);
  voi-R            — bat dau 1, dung toi uu nhin truoc m buoc, R = gia tri / chi phi.

Chay:  ../.venv/Scripts/python chay.py [--nhanh]
"""

import argparse
import json
import random
import statistics
import sys
import time
from pathlib import Path

GOC = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(GOC / "services" / "quality"))

from app import redundancy as rd  # noqa: E402

from du_lieu import BoDuLieu, mo_ta, nap  # noqa: E402
import mo_phong  # noqa: E402

TIEN_NGHIEM = 0.7
SUC_NANG = 2.0
TI_LE_VANG = 0.2

NGUONG = [0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.97, 0.99, 0.995]
TI_LE_GIA_TRI = [2, 4, 8, 15, 30, 60, 120, 250, 500, 1000]

KET_QUA = Path(__file__).resolve().parent / "ket-qua"


def _da_so(nhan: list[str], rng: random.Random) -> str:
    dem: dict[str, int] = {}
    for l in nhan:
        dem[l] = dem.get(l, 0) + 1
    tot = max(dem.values())
    return rng.choice(sorted(k for k, v in dem.items() if v == tot))


def _hau_nghiem_max(nhan: list[str], acc: list[float], lua_chon: list[str], rng: random.Random) -> str:
    h = rd.hau_nghiem(nhan, acc, lua_chon)
    tot = max(h.values())
    return rng.choice(sorted(k for k, v in h.items() if v == tot))


def chay_mot_lan(bo: BoDuLieu, tran: int, hat: int) -> dict[str, tuple[float, float]]:
    """Mot hat giong → {cau hinh: (do chinh xac, nhan trung binh / muc)}."""
    rng = random.Random(hat)
    muc = sorted(k for k, v in bo.muc.items() if len(v) >= tran)
    rng.shuffle(muc)
    so_vang = int(len(muc) * TI_LE_VANG)
    vang, danh_gia = muc[:so_vang], muc[so_vang:]

    dung: dict[str, int] = {}
    tong: dict[str, int] = {}
    for m in vang:
        for w, l in bo.muc[m]:
            tong[w] = tong.get(w, 0) + 1
            dung[w] = dung.get(w, 0) + (1 if l == bo.dap_an[m] else 0)

    def do_cx(w: str) -> float:
        return (dung.get(w, 0) + TIEN_NGHIEM * SUC_NANG) / (tong.get(w, 0) + SUC_NANG)

    lc = bo.lua_chon
    kq: dict[str, list[float]] = {}   # cau hinh → [so dung, tong nhan]

    def ghi(ten: str, dap_an: str, that: str, so_nhan: int) -> None:
        x = kq.setdefault(ten, [0.0, 0.0])
        x[0] += 1 if dap_an == that else 0
        x[1] += so_nhan

    for m in danh_gia:
        chon = rng.sample(bo.muc[m], tran)                 # TRAN nhan, thu tu ngau nhien
        nhan = [l for _, l in chon]
        acc = [do_cx(w) for w, _ in chon]
        that = bo.dap_an[m]

        for n in range(1, tran + 1):
            ghi(f"co-dinh-{n}", _da_so(nhan[:n], rng), that, n)
            ghi(f"co-dinh-{n}-trongso", _hau_nghiem_max(nhan[:n], acc[:n], lc, rng), that, n)

        for c in range(3, tran + 1, 2):
            n = 2
            while not rd.quyet_dinh_majority(nhan[:n], c).dung:
                n += 1
            ghi(f"p3-tran-{c}", _da_so(nhan[:n], rng), that, n)

        for t in NGUONG:
            n = 1
            while not rd.quyet_dinh_posterior(nhan[:n], acc[:n], lc, tran, t).dung:
                n += 1
            ghi(f"posterior-{t}", _hau_nghiem_max(nhan[:n], acc[:n], lc, rng), that, n)

        for r in TI_LE_GIA_TRI:
            n = 1
            while not rd.quyet_dinh_voi(nhan[:n], acc[:n], lc, tran, TIEN_NGHIEM, r).dung:
                n += 1
            ghi(f"voi-{r}", _hau_nghiem_max(nhan[:n], acc[:n], lc, rng), that, n)

    so = len(danh_gia)
    return {k: (v[0] / so, v[1] / so) for k, v in kq.items()}


def chay_bo(bo: BoDuLieu, tran: int, so_hat: int) -> dict:
    lan = [chay_mot_lan(bo, tran, h) for h in range(so_hat)]
    gop = {}
    for k in lan[0]:
        acc = [x[k][0] for x in lan]
        chi = [x[k][1] for x in lan]
        gop[k] = {
            "acc": statistics.mean(acc), "acc_sd": statistics.pstdev(acc),
            "nhan": statistics.mean(chi), "nhan_sd": statistics.pstdev(chi),
        }
    return gop


def chi_phi_de_dat(duong: list[tuple[float, float]], muc_tieu: float) -> float | None:
    """Duong (nhan, acc) sap theo chi phi: chi phi nho nhat (noi suy tuyen tinh) dat acc >= muc_tieu."""
    duong = sorted(duong)
    for i, (c, a) in enumerate(duong):
        if a >= muc_tieu:
            if i == 0:
                return c
            c0, a0 = duong[i - 1]
            if a0 >= muc_tieu or a == a0:
                return c
            return c0 + (muc_tieu - a0) * (c - c0) / (a - a0)
    return None


def tiet_kiem(gop: dict, tran: int) -> list[dict]:
    """
    Chinh sach thich ung can bao nhieu nhan de dat CUNG do chinh xac voi co dinh n (3, 5, 7).
    Hai moc so sanh:
      da-so    — co dinh n, bo phieu da so (cach lam pho bien);
      trong-so — co dinh n, gop co trong so do chinh xac: tach rieng loi ich cua viec DUNG SOM
                 khoi loi ich cua viec biet ai gioi.
    """
    duong = {
        "posterior": [(v["nhan"], v["acc"]) for k, v in gop.items() if k.startswith("posterior-")],
        "voi": [(v["nhan"], v["acc"]) for k, v in gop.items() if k.startswith("voi-")],
        "p3": [(v["nhan"], v["acc"]) for k, v in gop.items() if k.startswith("p3-")],
    }
    ds = []
    for moc, hau_to in (("da-so", ""), ("trong-so", "-trongso")):
        for n in range(3, tran + 1, 2):
            goc = gop[f"co-dinh-{n}{hau_to}"]
            dong = {"moc": moc, "co_dinh": n, "acc_goc": goc["acc"]}
            for ten, d in duong.items():
                c = chi_phi_de_dat(d, goc["acc"])
                dong[ten] = None if c is None else {"nhan": c, "tiet_kiem": 1 - c / n}
            ds.append(dong)
    return ds


def ve(ten: str, gop: dict, tran: int, dich: Path) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(7.2, 4.6))
    nhom = [
        ("Cố định n (đa số)", lambda k: k.startswith("co-dinh-") and not k.endswith("trongso"), "#7f7f7f", "o", "-"),
        ("Cố định n (trọng số)", lambda k: k.endswith("-trongso"), "#bcbd22", "s", ":"),
        ("Quy tắc P3 (trần 3/5/7)", lambda k: k.startswith("p3-"), "#ff7f0e", "^", "-"),
        ("Posterior τ", lambda k: k.startswith("posterior-"), "#1f77b4", "D", "-"),
        ("VOI nhìn trước", lambda k: k.startswith("voi-"), "#2ca02c", "v", "-"),
    ]
    for nhan, loc, mau, dau, kieu in nhom:
        diem = sorted((v["nhan"], v["acc"], v["acc_sd"]) for k, v in gop.items() if loc(k))
        if not diem:
            continue
        ax.errorbar([d[0] for d in diem], [d[1] * 100 for d in diem], yerr=[d[2] * 100 for d in diem],
                    label=nhan, color=mau, marker=dau, linestyle=kieu, markersize=4, capsize=2, linewidth=1.4)
    ax.set_xlabel("Số nhãn phải mua trung bình / mẫu")
    ax.set_ylabel("Độ chính xác (%)")
    ax.set_title(f"{ten} — độ chính xác ↔ chi phí (trần {tran})")
    ax.grid(alpha=0.3)
    ax.legend(fontsize=8, loc="lower right")
    fig.tight_layout()
    fig.savefig(dich, dpi=140)
    plt.close(fig)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--nhanh", action="store_true", help="it hat giong, de thu")
    args = ap.parse_args()
    so_hat = 3 if args.nhanh else 20

    KET_QUA.mkdir(exist_ok=True)
    img = GOC / "docs" / "thi-nghiem" / "img"
    img.mkdir(parents=True, exist_ok=True)

    tong_hop = {"benchmark": {}, "mo_phong": {}, "cau_hinh": {
        "so_hat": so_hat, "ti_le_vang": TI_LE_VANG, "tien_nghiem": TIEN_NGHIEM, "suc_nang": SUC_NANG,
        "nguong": NGUONG, "ti_le_gia_tri": TI_LE_GIA_TRI}}

    for ten, tran in [("dog", 7), ("face", 7), ("duck", 7), ("nist-trec-relevance", 7), ("relevance-2", 5)]:
        t0 = time.time()
        bo = nap(ten)
        gop = chay_bo(bo, tran, so_hat)
        dung = sum(1 for v in bo.muc.values() if len(v) >= tran)
        tong_hop["benchmark"][ten] = {"mo_ta": mo_ta(bo), "tran": tran, "muc_dung": dung, "ket_qua": gop, "tiet_kiem": tiet_kiem(gop, tran)}
        ve(ten, gop, tran, img / f"nc-d-01-{ten}.png")
        print(f"{ten}: {time.time() - t0:.0f}s", flush=True)

    for k in (2, 4):
        for s in (0.0, 0.1, 0.2, 0.3, 0.4):
            t0 = time.time()
            bo = mo_phong.sinh(k, s, hat_giong=1000 + int(s * 100) + k)
            gop = chay_bo(bo, 7, max(3, so_hat // 4))
            tong_hop["mo_phong"][bo.ten] = {"K": k, "spam": s, "ket_qua": gop, "tiet_kiem": tiet_kiem(gop, 7)}
            print(f"{bo.ten}: {time.time() - t0:.0f}s", flush=True)

    (KET_QUA / "ket-qua.json").write_text(json.dumps(tong_hop, ensure_ascii=False, indent=1), encoding="utf-8")
    ve_mo_phong(tong_hop["mo_phong"], img / "nc-d-01-mo-phong.png")
    print("xong")


def ve_mo_phong(mp: dict, dich: Path) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, axs = plt.subplots(1, 2, figsize=(10, 4), sharey=True)
    for ax, k in zip(axs, (2, 4)):
        for ten, mau in (("posterior", "#1f77b4"), ("voi", "#2ca02c"), ("p3", "#ff7f0e")):
            xs, ys = [], []
            for v in sorted((v for v in mp.values() if v["K"] == k), key=lambda v: v["spam"]):
                d = next(x for x in v["tiet_kiem"] if x["co_dinh"] == 5 and x["moc"] == "da-so")
                if d[ten] is not None:
                    xs.append(v["spam"] * 100)
                    ys.append(d[ten]["tiet_kiem"] * 100)
            ax.plot(xs, ys, marker="o", color=mau, label=ten)
        ax.axhline(0, color="#999", linewidth=0.8)
        ax.set_title(f"Mô phỏng K = {k}: tiết kiệm so với cố định n = 5")
        ax.set_xlabel("Tỉ lệ spammer (%)")
        ax.grid(alpha=0.3)
    axs[0].set_ylabel("Giảm số nhãn ở cùng độ chính xác (%)")
    axs[0].legend()
    fig.tight_layout()
    fig.savefig(dich, dpi=140)
    plt.close(fig)


if __name__ == "__main__":
    main()
