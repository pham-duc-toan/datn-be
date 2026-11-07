"""Bo vi du gop nhan: contracts/labeling/testdata/gop.vectors.json — test hoi quy cua app/gop.py.

"expected" ban dau do ban C# cu (Crowd.Labeling, nay da bo — kien truc 3.24) sinh ra; app/gop.py la noi DUY
NHAT gop nhan nen "expected" chinh la ket qua giao doanh nghiep (project-svc nhan qua gRPC). Doi thuat toan
co chu dich thi sua ca "expected" (xem lai bang mat, cung commit).
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
def test_gop_mau_ra_dung_hinh_dang_giao_doanh_nghiep(vi_du):
    """gop_mau (gRPC LabelAggregation) ra DUNG object "expected": ca method / votes / kind, cung thu tu khoa."""
    thuc = gop.gop_mau(vi_du["schema"], vi_du["labels"], vi_du["metadata"], None, vi_du["minF1"])
    for tool in vi_du["schema"]["tools"]:
        ten = tool["name"]
        mong = vi_du["expected"][ten]
        assert list(thuc[ten].keys()) == list(mong.keys()), f"{ten}: khoa {list(thuc[ten].keys())} != {list(mong.keys())}"
        assert giong_nhau(thuc[ten], mong), f"{ten}: thuc {thuc[ten]} != mong {mong}"
        # So nguyen ghi khong co ".0" — giong JSON ban C# cu, bo xuat file doc GetValue<int> duoc. (Thu tu khoa
        # trong object long nhau khong mang nghia: file vi du ghi khoa da sap xep.)
        assert json.dumps(thuc[ten], sort_keys=True) == json.dumps(_so_nguyen_gon(mong), sort_keys=True), ten


def _so_nguyen_gon(v: Any) -> Any:
    if isinstance(v, float) and v.is_integer():
        return int(v)
    if isinstance(v, dict):
        return {k: _so_nguyen_gon(x) for k, x in v.items()}
    if isinstance(v, list):
        return [_so_nguyen_gon(x) for x in v]
    return v


@pytest.mark.parametrize("vi_du", VI_DU, ids=[v["name"] for v in VI_DU])
def test_dong_thuan_ca_task_nhat_quan_voi_tung_cong_cu(vi_du):
    """danh_gia (ca task) phai chot dung ket qua tung cong cu va cham khop tung nhan theo do."""
    nhan = [Nhan(f"a{i}", f"l{i}", du_lieu) for i, du_lieu in enumerate(vi_du["labels"])]
    kq = danh_gia(vi_du["schema"], nhan, tham_so=ThamSoGop(mau=vi_du["metadata"], f1_toi_thieu=vi_du["minF1"]))

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
