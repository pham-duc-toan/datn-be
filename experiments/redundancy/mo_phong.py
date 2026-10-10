"""Du lieu mo phong co kiem soat cho NC-D-01.

Mo hinh (co chu dich KHAC mo hinh "mot dong xu" ma chinh sach posterior gia dinh, de khong
"thang vi chinh minh dat luat"):
  - nguoi lam that: do chinh xac ~ Uniform(0.6, 0.95);
  - spammer (ti le s): tra loi NGAU NHIEN deu;
  - 20% muc KHO: do chinh xac cua moi nguoi lam that giam 0.2 (khong duoi ngau nhien) —
    mo hinh one-coin KHONG biet do kho tung muc;
  - moi muc lay `so_nhan` nguoi ngau nhien khac nhau trong pool.
"""

import random

from du_lieu import BoDuLieu


def sinh(so_lua_chon: int, ti_le_spam: float, hat_giong: int, so_muc: int = 1500, so_nguoi: int = 150, so_nhan: int = 7) -> BoDuLieu:
    rng = random.Random(hat_giong)
    lua_chon = [chr(ord("A") + i) for i in range(so_lua_chon)]
    ngau_nhien = 1.0 / so_lua_chon

    nguoi = []
    for i in range(so_nguoi):
        spam = rng.random() < ti_le_spam
        nguoi.append((f"w{i}", ngau_nhien if spam else rng.uniform(0.6, 0.95)))

    muc: dict[str, list[tuple[str, str]]] = {}
    dap_an: dict[str, str] = {}
    for j in range(so_muc):
        that = rng.choice(lua_chon)
        kho = rng.random() < 0.2
        nhan = []
        for w, a in rng.sample(nguoi, so_nhan):
            if a > ngau_nhien and kho:
                a = max(ngau_nhien, a - 0.2)
            if rng.random() < a:
                l = that
            else:
                l = rng.choice([x for x in lua_chon if x != that]) if a > ngau_nhien else rng.choice(lua_chon)
            nhan.append((w, l))
        muc[f"m{j}"] = nhan
        dap_an[f"m{j}"] = that

    return BoDuLieu(f"mo-phong-K{so_lua_chon}-spam{int(ti_le_spam * 100)}", lua_chon, muc, dap_an, "mo phong")
