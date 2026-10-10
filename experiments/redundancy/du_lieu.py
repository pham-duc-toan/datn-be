"""Nap du lieu cho thi nghiem NC-D-01.

Moi bo du lieu tra ve dang chung:
    BoDuLieu(ten, lua_chon, muc: dict[item → list[(worker, nhan)]], dap_an: dict[item → nhan])
Chi giu muc CO dap an that.

Nguon:
  - crowd-kit (Yandex Relevance-2, NIST TREC Relevance) — tu tai va cache cua crowd-kit;
  - kho benchmark truth-inference cua Zheng et al., "Truth Inference in Crowdsourcing: Is the
    Problem Solved?" (VLDB 2017): Dog, Face, Duck — nhan DAY (~10–39 nhan/muc) nen so duoc toi n = 7.
  - mo phong co kiem soat (mo_phong.py).
"""

import csv
import io
from dataclasses import dataclass
from pathlib import Path

import requests

CACHE = Path(__file__).resolve().parent.parent / ".cache"
GOC_ZHENG = "https://raw.githubusercontent.com/zhydhkcws/crowd_truth_infer/master/datasets/"
ZHENG = {
    "dog": "s4_Dog%20data",
    "face": "s4_Face%20Sentiment%20Identification",
    "duck": "d_Duck%20Identification",
}


@dataclass
class BoDuLieu:
    ten: str
    lua_chon: list[str]
    muc: dict[str, list[tuple[str, str]]]
    dap_an: dict[str, str]
    nguon: str


def _tai(url: str, dich: Path) -> str:
    if not dich.exists():
        dich.parent.mkdir(parents=True, exist_ok=True)
        r = requests.get(url, timeout=60)
        r.raise_for_status()
        dich.write_bytes(r.content)
    return dich.read_text(encoding="utf-8")


def nap_zheng(ten: str) -> BoDuLieu:
    thu_muc = ZHENG[ten]
    tl = _tai(GOC_ZHENG + thu_muc + "/answer.csv", CACHE / ten / "answer.csv")
    da = _tai(GOC_ZHENG + thu_muc + "/truth.csv", CACHE / ten / "truth.csv")

    muc: dict[str, list[tuple[str, str]]] = {}
    for d in csv.DictReader(io.StringIO(tl)):
        muc.setdefault(d["question"], []).append((d["worker"], d["answer"]))
    dap_an = {d["question"]: d["truth"] for d in csv.DictReader(io.StringIO(da))}

    muc = {k: v for k, v in muc.items() if k in dap_an}
    lua_chon = sorted({l for v in muc.values() for _, l in v} | set(dap_an.values()))
    return BoDuLieu(ten, lua_chon, muc, {k: dap_an[k] for k in muc}, "Zheng et al. VLDB 2017")


def nap_crowdkit(ten: str) -> BoDuLieu:
    from crowdkit.datasets import load_dataset

    df, gt = load_dataset(ten)
    df = df[df["task"].isin(gt.index)]
    muc: dict[str, list[tuple[str, str]]] = {}
    for task, worker, label in zip(df["task"].astype(str), df["worker"].astype(str), df["label"].astype(str)):
        muc.setdefault(task, []).append((worker, label))
    dap_an = {str(k): str(v) for k, v in gt.items() if str(k) in muc}
    lua_chon = sorted({l for v in muc.values() for _, l in v} | set(dap_an.values()))
    return BoDuLieu(ten, lua_chon, muc, dap_an, "crowd-kit (Toloka/Yandex, NIST)")


def nap(ten: str) -> BoDuLieu:
    return nap_zheng(ten) if ten in ZHENG else nap_crowdkit(ten)


def mo_ta(b: BoDuLieu) -> dict:
    so = [len(v) for v in b.muc.values()]
    tho = {w for v in b.muc.values() for w, _ in v}
    return {
        "ten": b.ten, "muc": len(b.muc), "lua_chon": len(b.lua_chon), "nguoi": len(tho),
        "nhan": sum(so), "nhan_tb": sum(so) / max(1, len(so)), "nguon": b.nguon,
    }
