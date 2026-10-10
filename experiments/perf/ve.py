"""Ve bieu do NC-B-06 tu ket-qua/*.json → docs/thi-nghiem/img/."""

import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

KQ = Path(__file__).resolve().parent / "ket-qua"
IMG = Path(__file__).resolve().parents[2] / "docs" / "thi-nghiem" / "img"

MAU = {"perClick-x1": "#d62728", "batched-x1": "#1f77b4", "perClick-x3": "#ff9896", "batched-x3": "#aec7e8"}
NHAN = {"perClick-x1": "perClick, 1 instance", "batched-x1": "batched, 1 instance",
        "perClick-x3": "perClick, 3 instance", "batched-x3": "batched, 3 instance"}


def ve_ledger() -> None:
    d = json.loads((KQ / "ledger-cpm.json").read_text(encoding="utf-8"))
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(11, 4.2))
    for k in ("perClick-x1", "batched-x1", "perClick-x3", "batched-x3"):
        if k not in d:
            continue
        muc = d[k]["muc"]
        x = [m["toc_do_phat"] for m in muc]
        kieu = "-" if k.endswith("x1") else "--"
        a1.plot(x, [m["thong_luong"] for m in muc], marker="o", linestyle=kieu, color=MAU[k], label=NHAN[k])
        a2.plot(x, [m["tre_p99"] for m in muc], marker="o", linestyle=kieu, color=MAU[k], label=NHAN[k] + " p99")
        a2.plot(x, [m["tre_p50"] for m in muc], marker=".", linestyle=":", color=MAU[k], alpha=0.8)
    lo = [10, 800]
    a1.plot(lo, lo, color="#999", linewidth=0.8, label="lý tưởng (= tải đưa vào)")
    for a in (a1, a2):
        a.set_xscale("log")
        a.set_xlabel("Tải đưa vào (lượt vượt link / giây)")
        a.grid(alpha=0.3, which="both")
    a1.set_yscale("log")
    a1.set_ylabel("Thông lượng chi tiền đạt được (lượt / giây)")
    a1.set_title("Ledger: thông lượng chi tiền cổng link")
    a2.set_yscale("log")
    a2.set_ylabel("Độ trễ tới khi tiền vào ví (giây)")
    a2.set_title("Độ trễ p99 (liền) / p50 (chấm)")
    a1.legend(fontsize=8)
    a2.legend(fontsize=7)
    fig.tight_layout()
    fig.savefig(IMG / "nc-b-06-ledger.png", dpi=140)
    plt.close(fig)


BAN_TASK = [
    ("task-lease-random.json", "ORDER BY random() (ban đầu)", "#d62728"),
    ("task-lease-id.json", "ORDER BY id", "#ff7f0e"),
    ("task-lease.json", "ORDER BY id + chỉ mục labeler (cuối)", "#2ca02c"),
]


def ve_task() -> None:
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(11, 4.2))
    for ten, nhan, mau in BAN_TASK:
        p = KQ / ten
        if not p.exists():
            continue
        muc = json.loads(p.read_text(encoding="utf-8"))["muc"]
        x = [m["dong_thoi"] for m in muc]
        a1.plot(x, [m["thong_luong"] for m in muc], marker="o", color=mau, label=nhan)
        a2.plot(x, [m["lay_p99_ms"] for m in muc], marker="o", color=mau, label=nhan + " — p99")
        a2.plot(x, [m["lay_p50_ms"] for m in muc], marker=".", linestyle=":", color=mau, alpha=0.8)
    cu = KQ / "task-lease-mot-tien-trinh.json"
    if cu.exists():
        d = json.loads(cu.read_text(encoding="utf-8"))
        for k, mau in (("random", "#d62728"), ("id", "#ff7f0e")):
            muc = d[k]["muc"]
            a1.plot([m["dong_thoi"] for m in muc], [m["thong_luong"] for m in muc], linestyle="--", marker="x",
                    color=mau, alpha=0.5, label=f"bộ đo cũ (1 tiến trình), {k}")
    a1.axvspan(300, 450, color="#999", alpha=0.15)
    a1.text(310, 8, "máy đo" + chr(10) + "bão hoà CPU", fontsize=7, color="#555")
    a1.set_ylabel("Lượt nộp / giây")
    a1.set_title("task-svc: thông lượng lấy task → nộp")
    a1.set_ylim(0, 185)
    a1.legend(fontsize=7, loc="upper left", ncol=2)
    a2.set_ylabel("Độ trễ lấy task (ms)")
    a2.set_title("Độ trễ lấy task p99 (liền) / p50 (chấm)")
    a2.set_yscale("log")
    a2.legend(fontsize=7)
    for a in (a1, a2):
        a.set_xscale("log")
        a.set_xlabel("Số labeler đồng thời")
        a.grid(alpha=0.3, which="both")
    fig.tight_layout()
    fig.savefig(IMG / "nc-b-06-task.png", dpi=140)
    plt.close(fig)


if __name__ == "__main__":
    IMG.mkdir(parents=True, exist_ok=True)
    ve_ledger()
    ve_task()
    print("xong")
