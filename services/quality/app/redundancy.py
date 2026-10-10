"""Quyet dinh "dung hay xin them mot nguoi gan" cho MOT cong cu lua chon roi rac — NC-D-01.

Ham thuan, khong DB: quality-svc (consensus.py) va thi nghiem (experiments/redundancy) goi
CUNG cac ham nay, nen so lieu trong bao cao chinh la so lieu cua code dang chay.

Mo hinh "mot dong xu" (one-coin) cho moi labeler w co do chinh xac a_w:
    P(nhan = y | dap an that = y) = a_w
    P(nhan = y' | dap an that = y) = (1 − a_w) / (K − 1)    voi moi y' ≠ y
Tien nghiem deu tren K lua chon. Hau nghiem:
    P(y | nhan) ∝ Π_w P(nhan_w | y)

Ba chinh sach:
  majority  — quy tac P3: xin them khi KHONG lua chon nao qua ban; khong dung do chinh xac.
  posterior — dung khi max_y P(y | nhan) >= nguong (vd 0.95).
  voi       — dung toi uu NHIN TRUOC m BUOC: xin them neu ton tai m (1..con lai toi tran) ma
                 ti_le_gia_tri x (E[max P sau m nhan] − max P)  >  m
              (ti_le_gia_tri = mot nhan cuoi DUNG dang gia bao nhieu lan chi phi mot nhan mua them).
              Chi nhin MOT buoc (m = 1) la sai: khi mot nhan khong du lat quyet dinh thi gia tri
              thong tin cua no bang 0, du hai ba nhan nua lat duoc — chinh sach se dung qua som.
"""

from collections.abc import Iterator, Sequence
from dataclasses import dataclass
import math

# Nhin truoc toi da bay nhieu nhan (so to hop dem tang nhanh theo m va K).
NHIN_TRUOC_TOI_DA = 6

# Gioi han do chinh xac dua vao mo hinh: 1.0 lam log(0); <= 1/K nghia la "nguoc" hoac ngau nhien.
TRAN_DO_CHINH_XAC = 0.99


@dataclass(frozen=True)
class QuyetDinh:
    dung: bool
    dap_an: str | None          # lua chon dan dau (None neu chua co nhan / hoa)
    do_tin_cay: float           # max hau nghiem (majority: ti le phieu cua lua chon dan dau)


def kep_do_chinh_xac(a: float, so_lua_chon: int) -> float:
    """Dua do chinh xac ve (1/K, 0.99]: labeler te hon ngau nhien coi nhu ngau nhien (khong tin, khong dao nguoc)."""
    san = 1.0 / so_lua_chon + 1e-3
    return min(TRAN_DO_CHINH_XAC, max(san, a))


def hau_nghiem(nhan: Sequence[str], do_chinh_xac: Sequence[float], lua_chon: Sequence[str]) -> dict[str, float]:
    """P(dap an | cac nhan) theo mo hinh mot dong xu, tien nghiem deu. Tinh tren log de khong tran so."""
    k = len(lua_chon)
    if k < 2:
        raise ValueError("Can it nhat 2 lua chon")
    log_p = {y: 0.0 for y in lua_chon}
    for l, a in zip(nhan, do_chinh_xac):
        if l not in log_p:
            continue  # nhan ngoai tap lua chon (khong xay ra neu da kiem dinh dang)
        a = kep_do_chinh_xac(a, k)
        dung = math.log(a)
        sai = math.log((1 - a) / (k - 1))
        for y in lua_chon:
            log_p[y] += dung if y == l else sai
    m = max(log_p.values())
    mu = {y: math.exp(v - m) for y, v in log_p.items()}
    tong = sum(mu.values())
    return {y: v / tong for y, v in mu.items()}


def _phan_hoach(m: int, k: int) -> Iterator[tuple[int, ...]]:
    """Moi cach chia m nhan vao k lua chon (vector dem)."""
    if k == 1:
        yield (m,)
        return
    for i in range(m + 1):
        for phan_con in _phan_hoach(m - i, k - 1):
            yield (i,) + phan_con


def gia_tri_thong_tin(hau: dict[str, float], a_tiep: float, so_nhan: int = 1) -> float:
    """
    E[max P'] − max P khi mua them `so_nhan` nhan tu cac labeler cung do chinh xac a_tiep.
    Ket qua phu thuoc vector dem (da thuc):  E[max P'] = Σ_dem max_y P(y) P(dem | y)
    (mau so P(dem) triet tieu). Luon >= 0.
    """
    k = len(hau)
    a = kep_do_chinh_xac(a_tiep, k)
    sai = (1 - a) / (k - 1)
    log_a, log_sai = math.log(a), math.log(sai)
    lua_chon = list(hau)
    ky_vong = 0.0
    for dem in _phan_hoach(so_nhan, k):
        log_he_so = math.lgamma(so_nhan + 1) - sum(math.lgamma(c + 1) for c in dem)
        tot = 0.0
        for j, y in enumerate(lua_chon):
            # P(dem | y): dem[j] nhan dung, con lai sai.
            log_p = log_he_so + dem[j] * log_a + (so_nhan - dem[j]) * log_sai
            tot = max(tot, hau[y] * math.exp(log_p))
        ky_vong += tot
    return max(0.0, ky_vong - max(hau.values()))


def _dan_dau(xs: dict[str, float]) -> tuple[str | None, float]:
    if not xs:
        return None, 0.0
    tot = max(xs.values())
    dau = [y for y, v in xs.items() if v == tot]
    return (dau[0] if len(dau) == 1 else None), tot


def quyet_dinh_majority(nhan: Sequence[str], tran: int) -> QuyetDinh:
    """Quy tac P3: co lua chon QUA BAN thi dung; khong thi xin them toi tran."""
    n = len(nhan)
    if n == 0:
        return QuyetDinh(False, None, 0.0)
    dem: dict[str, int] = {}
    for l in nhan:
        dem[l] = dem.get(l, 0) + 1
    dau, so = _dan_dau({k: float(v) for k, v in dem.items()})
    qua_ban = so * 2 > n
    return QuyetDinh(qua_ban or n >= tran, dau if qua_ban else (dau if n >= tran else None), so / n)


def quyet_dinh_posterior(
    nhan: Sequence[str], do_chinh_xac: Sequence[float], lua_chon: Sequence[str], tran: int, nguong: float
) -> QuyetDinh:
    if not nhan:
        return QuyetDinh(False, None, 0.0)
    hau = hau_nghiem(nhan, do_chinh_xac, lua_chon)
    dau, p = _dan_dau(hau)
    return QuyetDinh(p >= nguong or len(nhan) >= tran, dau, p)


def quyet_dinh_voi(
    nhan: Sequence[str], do_chinh_xac: Sequence[float], lua_chon: Sequence[str], tran: int,
    a_tiep: float, ti_le_gia_tri: float,
) -> QuyetDinh:
    if not nhan:
        return QuyetDinh(False, None, 0.0)
    hau = hau_nghiem(nhan, do_chinh_xac, lua_chon)
    dau, p = _dan_dau(hau)
    if len(nhan) >= tran:
        return QuyetDinh(True, dau, p)
    return QuyetDinh(not dang_mua_them(hau, a_tiep, ti_le_gia_tri, tran - len(nhan)), dau, p)


def dang_mua_them(hau: dict[str, float], a_tiep: float, ti_le_gia_tri: float, con_lai: int) -> bool:
    """Co so nhan m nao (1..con_lai, toi da NHIN_TRUOC_TOI_DA) ma loi ky vong vuot chi phi m nhan."""
    for m in range(1, min(con_lai, NHIN_TRUOC_TOI_DA) + 1):
        if gia_tri_thong_tin(hau, a_tiep, m) * ti_le_gia_tri > m:
            return True
    return False
