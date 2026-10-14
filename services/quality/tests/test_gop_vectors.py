"""Bo vi du gop nhan CHUNG voi C#: contracts/labeling/testdata/gop.vectors.json.

"expected" trong file do Crowd.Labeling (C#) sinh ra (GopVectorTests.cs, UPDATE_GOP_VECTORS=1).
app/gop.py phai ra dung ket qua do — lech la thuat toan hai ben da troi khoi nhau.
"""

import json
import math
from pathlib import Path
from typing import Any

import pytest

from app import gop
from app.consensus import Nhan, ThamSoGop, danh_gia

VI_DU = json.loads(
    (Path(__file__).resolve().parents[3] / "contracts" / "labeling" / "testdata" / "gop.vectors.json").read_text(encoding="utf-8"))


def giong_nhau(a: Any, b: Any) -> bool:
    """So sanh sau; so lech duoi 1e-6 coi nhu bang (4 va 4.0 la mot). True / False khong phai so."""
    if isinstance(a, bool) or isinstance(b, bool):
        return a is b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return math.isclose(a, b, abs_tol=1e-6)
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(giong_nhau(a[k], b[k]) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(giong_nhau(x, y) for x, y in zip(a, b))
    return a == b


@pytest.mark.parametrize("vi_du", VI_DU, ids=[v["name"] for v in VI_DU])
def test_gop_tung_cong_cu_khop_ket_qua_cua_csharp(vi_du):
    for tool in vi_du["schema"]["tools"]:
        ten = tool["name"]
        mong = vi_du["expected"][ten]
        ket_qua = [nhan[ten] for nhan in vi_du["labels"] if ten in nhan]

        if mong["method"] == "none":
            # freeText, hoac cong cu tuy chon khong ai lam.
            assert tool["kind"] == "freeText" or not ket_qua
            if ket_qua:
                assert gop.gop_cong_cu(tool, ket_qua) is None
            continue

        thuc = gop.gop_cong_cu(tool, ket_qua, vi_du["metadata"], None, vi_du["minF1"])
        for truong in ("final", "agrees", "disputed", "supports", "scores"):
            assert giong_nhau(thuc.get(truong), mong.get(truong)), f"{ten}.{truong}: thuc {thuc.get(truong)} != mong {mong.get(truong)}"


@pytest.mark.parametrize("vi_du", VI_DU, ids=[v["name"] for v in VI_DU])
def test_dong_thuan_ca_task_nhat_quan_voi_tung_cong_cu(vi_du):
    """danh_gia (ca task) phai chot dung ket qua tung cong cu va cham khop tung nhan theo do."""
    nhan = [Nhan(f"a{i}", f"l{i}", du_lieu) for i, du_lieu in enumerate(vi_du["labels"])]
    so_nguoi = len(nhan)
    kq = danh_gia(vi_du["schema"], nhan, target=so_nguoi, max_redundancy=so_nguoi,
                  tham_so=ThamSoGop(mau=vi_du["metadata"], f1_toi_thieu=vi_du["minF1"]))

    co_gop = {t["name"]: vi_du["expected"][t["name"]] for t in vi_du["schema"]["tools"]
              if vi_du["expected"][t["name"]]["method"] != "none"}
    assert kq.status == ("disputed" if any(e["disputed"] for e in co_gop.values()) else "agreed")
    for ten, mong in co_gop.items():
        assert giong_nhau(kq.final[ten], mong["final"])

    # Mot nhan khop khi khop moi cong cu KHONG tranh chap ma nguoi do co lam.
    for i, n in enumerate(nhan):
        cham = []
        for ten, mong in co_gop.items():
            if mong["disputed"] or ten not in n.data:
                continue
            thu_tu = [j for j, m in enumerate(vi_du["labels"]) if ten in m].index(i)
            cham.append(mong["agrees"][thu_tu])
        assert kq.agrees[n.annotation_id] == (all(cham) if cham else None)
