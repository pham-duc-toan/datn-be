"""Gop nhan cua nhieu nguoi cho MOT cong cu — logic thuan, khong DB.

BAN PYTHON cua thuat toan trong shared/labeling (C#: Tools/DanhSachToolBase.cs, HinhHoc.cs, Rle.cs,
ThuocTinh.cs va tung *Tool.cs). Hai ben PHAI ra cung ket qua tren cung du lieu:
  - annotation-svc (C#) gop cac nhan DA DUYET → ket qua giao cho doanh nghiep;
  - quality-svc (file nay) gop moi nhan VUA NOP → dong thuan, xin them nguoi, cham khop / lech.
Doi chieu bang bo vi du chung contracts/labeling/testdata/gop.vectors.json (tests/test_gop_vectors.py
va GopVectorTests.cs). Sua thuat toan o mot ben thi sua ca ben kia va cap nhat bo vi du.

Cach gop theo loai cong cu:
  classification            lop duoc hon mot nua so nguoi chon (xet tung lop doc lap)
  pairwise                  lua chon qua ban
  bbox / polygon / keypoint / polyline / mask / temporalSegment / track
                            GHEP CUM cac doi tuong "cung mot vat" (do giong >= nguong), giu cum co hon
                            mot nua so nguoi, moi cum ra mot doi tuong dai dien
  span / relation           giu phan tu duoc hon mot nua so nguoi ghi y het
  transcription             ban "o giua" (tong do lech toi cac ban khac nho nhat)
  number                    trung vi
  ranking                   diem Borda
  freeText                  KHONG gop (cau tra loi mo) — gop_cong_cu tra None
"""

import math
import struct
from decimal import ROUND_HALF_UP, Decimal
from typing import Any, Callable

import numpy as np

SAI_SO_NGUONG = 1e-9
SO_DIEM_MAU_DUONG = 32
SO_MOC_MAU_TRACK = 50
F1_TOI_THIEU_MAC_DINH = 0.8

# Nguong mac dinh cua THU VIEN khi cong cu khong khai matchThreshold va khong co setting.
NGUONG_THU_VIEN = {
    "bbox": 0.5, "polygon": 0.5, "mask": 0.5, "temporalSegment": 0.5, "track": 0.5,
    "keypoint": 0.98, "polyline": 0.97, "transcription": 0.1, "ranking": 0.8,
}

CONG_CU_DANH_SACH = ("bbox", "polygon", "keypoint", "polyline", "mask", "span", "relation", "temporalSegment", "track")
CONG_CU_KHONG_GOP = ("freeText",)


def gop_duoc(tool: dict[str, Any]) -> bool:
    return tool.get("kind") not in CONG_CU_KHONG_GOP


# =====================================================================
# SO, CHU
# =====================================================================


def lam_tron(v: float) -> float:
    """Lam tron 3 chu so thap phan (nua thi len) — cung cong thuc voi HinhHoc.LamTron."""
    return math.floor(v * 1000 + 0.5) / 1000


def trung_vi(gia_tri: list[float]) -> float:
    ds = sorted(gia_tri)
    giua = len(ds) // 2
    return ds[giua] if len(ds) % 2 == 1 else (ds[giua - 1] + ds[giua]) / 2


def _don_vi(s: str) -> tuple[int, ...]:
    """Chuoi → day don vi UTF-16, de do dai va khoang cach sua tinh GIONG chuoi cua C#."""
    b = s.encode("utf-16-le")
    return struct.unpack("<" + "H" * (len(b) // 2), b)


def chuan_hoa_chu(s: str) -> str:
    """Chu thuong, gop moi cum khoang trang thanh mot dau cach (HinhHoc.ChuanHoaChu)."""
    return " ".join(s.strip().lower().split())


def khoang_cach_sua(a: tuple[int, ...], b: tuple[int, ...]) -> int:
    truoc = list(range(len(b) + 1))
    for i in range(1, len(a) + 1):
        nay = [i] + [0] * len(b)
        ai = a[i - 1]
        for j in range(1, len(b) + 1):
            thay = truoc[j - 1] + (0 if ai == b[j - 1] else 1)
            nay[j] = min(truoc[j] + 1, nay[j - 1] + 1, thay)
        truoc = nay
    return truoc[len(b)]


def cer(nop: str, dap_an: str) -> float:
    """Ti le loi ky tu: khoang cach sua / do dai dap an, sau khi chuan hoa."""
    a = _don_vi(chuan_hoa_chu(nop))
    b = _don_vi(chuan_hoa_chu(dap_an))
    if len(b) == 0:
        return 0.0 if len(a) == 0 else 1.0
    return khoang_cach_sua(a, b) / len(b)


def do_lech_chu(x: str, y: str) -> float:
    """Do lech doi xung 0..1: khoang cach sua / do dai ban dai hon."""
    a = _don_vi(chuan_hoa_chu(x))
    b = _don_vi(chuan_hoa_chu(y))
    dai = max(len(a), len(b))
    return 0.0 if dai == 0 else khoang_cach_sua(a, b) / dai


def ban_o_giua(chu: list[str]) -> int:
    """Vi tri ban co tong do lech toi cac ban khac nho nhat (hoa thi ban dau tien)."""
    tot = 0
    it_nhat = math.inf
    for i in range(len(chu)):
        tong = 0.0
        for j in range(len(chu)):
            if i != j:
                tong += do_lech_chu(chu[i], chu[j])
        if tong < it_nhat - SAI_SO_NGUONG:
            it_nhat = tong
            tot = i
    return tot


# =====================================================================
# HINH HOC
# =====================================================================


def iou_hop(x1, y1, w1, h1, x2, y2, w2, h2) -> float:
    trai = max(x1, x2)
    tren = max(y1, y2)
    phai = min(x1 + w1, x2 + w2)
    duoi = min(y1 + h1, y2 + h2)
    giao = max(0, phai - trai) * max(0, duoi - tren)
    hop = (w1 * h1) + (w2 * h2) - giao
    return 0.0 if hop <= 0 else giao / hop


def dinh_khung_xoay(x, y, w, h, goc) -> list[list[float]]:
    """Bon dinh khung xoay goc (do) quanh tam: tren-trai → tren-phai → duoi-phai → duoi-trai (BboxTool.Dinh)."""
    cx, cy = x + (w / 2), y + (h / 2)
    rad = goc * math.pi / 180
    c, s = math.cos(rad), math.sin(rad)
    return [[cx + (dx * c) - (dy * s), cy + (dx * s) + (dy * c)]
            for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]


def iou_khung(a: dict, b: dict) -> float:
    """IoU hai khung; co khung xoay thi IoU da giac bon dinh (BboxTool.DoGiongDaPhanTich)."""
    ga, gb = a.get("angle", 0) or 0, b.get("angle", 0) or 0
    if abs(ga) <= 1e-9 and abs(gb) <= 1e-9:
        return iou_hop(a["x"], a["y"], a["w"], a["h"], b["x"], b["y"], b["w"], b["h"])
    d1 = dinh_khung_xoay(a["x"], a["y"], a["w"], a["h"], ga)
    d2 = dinh_khung_xoay(b["x"], b["y"], b["w"], b["h"], gb)
    if not hinh_bao_giao_nhau(d1, d2):
        return 0.0
    return iou_da_giac(d1, d2)


def iou_doan(a1, b1, a2, b2) -> float:
    giao = max(0, min(b1, b2) - max(a1, a2))
    hop = (b1 - a1) + (b2 - a2) - giao
    return 0.0 if hop <= 0 else giao / hop


def _trong_da_giac(p: list[list[float]], xs: np.ndarray, ys: np.ndarray) -> np.ndarray:
    """
    Ray casting cho CA luoi diem cung luc → mang bool [chi so x, chi so y]. Cung phep so sanh voi
    HinhHoc.IouDaGiac ben C# (quet tung hang): voi moi hang y, tinh hoanh do giao diem cua hang voi tung
    canh "vat qua" hang do; diem (x, y) nam trong khi so giao diem ben PHAI no (x < giao diem) la le.
    """
    dinh = np.asarray(p, dtype=np.float64)
    xi, yi = dinh[:, 0], dinh[:, 1]
    xj, yj = np.roll(xi, 1), np.roll(yi, 1)                      # canh (i, i − 1), dinh dau noi dinh cuoi
    vat_qua = (yi[None, :] > ys[:, None]) != (yj[None, :] > ys[:, None])      # [hang, canh]
    with np.errstate(divide="ignore", invalid="ignore"):
        giao = ((xj - xi)[None, :] * (ys[:, None] - yi[None, :]) / (yj - yi)[None, :]) + xi[None, :]
    giao = np.where(vat_qua, giao, -np.inf)                       # canh khong vat qua: khong bao gio o ben phai

    # Dem so giao diem ben phai tung diem ma khong so tung cap (diem, canh): luoi x tang dan, nen giao diem c
    # nam ben phai dung cac diem co chi so < searchsorted(xs, c) (x < c). Dem so giao diem theo chi so do
    # tren tung hang, roi cong don tu phai sang.
    n = xs.shape[0]
    so_hang = ys.shape[0]
    chi_so = np.searchsorted(xs, giao.ravel(), side="left").reshape(giao.shape)
    o = (np.arange(so_hang)[:, None] * (n + 1) + chi_so).ravel()
    dem = np.bincount(o, minlength=so_hang * (n + 1)).reshape(so_hang, n + 1)
    so_ben_phai = dem[:, ::-1].cumsum(axis=1)[:, ::-1]            # [hang, k] = so giao diem co chi so >= k
    return ((so_ben_phai[:, 1:] & 1) == 1).T                      # diem i: cac giao diem co chi so >= i + 1


def iou_da_giac(p1: list[list[float]], p2: list[list[float]]) -> float:
    """IoU uoc luong bang luoi 100 x 100 phu hinh bao cua ca hai da giac (HinhHoc.IouDaGiac)."""
    xs = [d[0] for d in p1] + [d[0] for d in p2]
    ys = [d[1] for d in p1] + [d[1] for d in p2]
    min_x, max_x, min_y, max_y = min(xs), max(xs), min(ys), max(ys)

    n = 100
    buoc_x = (max_x - min_x) / n
    buoc_y = (max_y - min_y) / n
    if buoc_x <= 0 or buoc_y <= 0:
        return 0.0

    chi_so = np.arange(n, dtype=np.float64)
    luoi_x = min_x + ((chi_so + 0.5) * buoc_x)
    luoi_y = min_y + ((chi_so + 0.5) * buoc_y)
    trong1 = _trong_da_giac(p1, luoi_x, luoi_y)
    trong2 = _trong_da_giac(p2, luoi_x, luoi_y)
    hop = int(np.count_nonzero(trong1 | trong2))
    return 0.0 if hop == 0 else int(np.count_nonzero(trong1 & trong2)) / hop


def _hinh_bao(p: list[list[float]]) -> tuple[float, float, float, float]:
    return min(d[0] for d in p), min(d[1] for d in p), max(d[0] for d in p), max(d[1] for d in p)


def hinh_bao_giao_nhau(p1, p2) -> bool:
    a = _hinh_bao(p1)
    b = _hinh_bao(p2)
    return not (a[2] < b[0] or b[2] < a[0] or a[3] < b[1] or b[3] < a[1])


def khoang_cach(x1, y1, x2, y2) -> float:
    dx = x1 - x2
    dy = y1 - y2
    return math.sqrt((dx * dx) + (dy * dy))


def duong_cheo(mau: dict[str, Any] | None, diem_du_phong: list[list[float]]) -> float:
    """Thuoc do chuan hoa do lech: duong cheo anh; mau khong co kich thuoc thi dung hinh bao cac diem."""
    rong = (mau or {}).get("width")
    cao = (mau or {}).get("height")
    if rong and cao and rong > 0 and cao > 0:
        # SampleMetadata ben C# doc width / height thanh so nguyen (lam tron nua ra xa 0).
        return khoang_cach(0, 0, math.floor(rong + 0.5), math.floor(cao + 0.5))
    if not diem_du_phong:
        return 1.0
    a = _hinh_bao(diem_du_phong)
    return max(1.0, khoang_cach(a[0], a[1], a[2], a[3]))


def do_dai_duong(p: list[list[float]]) -> float:
    tong = 0.0
    for i in range(1, len(p)):
        tong = tong + khoang_cach(p[i - 1][0], p[i - 1][1], p[i][0], p[i][1])
    return tong


def lay_mau_duong(p: list[list[float]], so_diem: int) -> list[tuple[float, float]]:
    """so_diem diem cach deu theo do dai doc duong gap khuc (HinhHoc.LayMauDuong)."""
    tong = do_dai_duong(p)
    if tong <= 0:
        return [(p[0][0], p[0][1])] * so_diem

    ra = []
    doan = 1
    da_di = 0.0
    dai_doan = khoang_cach(p[0][0], p[0][1], p[1][0], p[1][1])
    for k in range(so_diem):
        dich = tong * k / (so_diem - 1)
        while doan < len(p) - 1 and da_di + dai_doan < dich:
            da_di = da_di + dai_doan
            doan += 1
            dai_doan = khoang_cach(p[doan - 1][0], p[doan - 1][1], p[doan][0], p[doan][1])
        t = 0.0 if dai_doan <= 0 else (dich - da_di) / dai_doan
        t = min(1.0, max(0.0, t))
        ra.append((p[doan - 1][0] + (t * (p[doan][0] - p[doan - 1][0])),
                   p[doan - 1][1] + (t * (p[doan][1] - p[doan - 1][1]))))
    return ra


def do_lech_duong(p1, p2) -> float:
    """Do lech trung binh (pixel) giua hai duong, thu ca chieu nguoc (HinhHoc.DoLechDuong)."""
    a = lay_mau_duong(p1, SO_DIEM_MAU_DUONG)
    b = lay_mau_duong(p2, SO_DIEM_MAU_DUONG)
    xuoi = 0.0
    nguoc = 0.0
    for k in range(SO_DIEM_MAU_DUONG):
        q = b[k]
        r = b[SO_DIEM_MAU_DUONG - 1 - k]
        xuoi = xuoi + khoang_cach(a[k][0], a[k][1], q[0], q[1])
        nguoc = nguoc + khoang_cach(a[k][0], a[k][1], r[0], r[1])
    return min(xuoi, nguoc) / SO_DIEM_MAU_DUONG


def kendall(a: list[int], b: list[int]) -> float:
    """Ti le cap muc duoc xep cung chieu trong hai thu tu cua cung tap muc (HinhHoc.Kendall)."""
    if len(a) != len(b):
        return 0.0
    vi_tri = {muc: i for i, muc in enumerate(b)}
    if any(muc not in vi_tri for muc in a):
        return 0.0
    tong = 0
    cung_chieu = 0
    for i in range(len(a)):
        for j in range(i + 1, len(a)):
            tong += 1
            if vi_tri[a[i]] < vi_tri[a[j]]:
                cung_chieu += 1
    return 1.0 if tong == 0 else cung_chieu / tong


# =====================================================================
# MAT NA (RLE khong nen kieu COCO: doan 0 / 1 xen ke, bat dau bang doan 0, duyet theo cot)
# =====================================================================


def rle_iou(a: dict[str, Any], b: dict[str, Any]) -> float:
    if list(a["size"]) != list(b["size"]):
        return 0.0
    da, db = a["counts"], b["counts"]
    # Loc nhanh (nhu Rle.Iou): khoang [pixel bat dau tien, sau pixel bat cuoi cung) cua hai mat na khong
    # cham nhau thi IoU bang 0.
    tong = a["size"][0] * a["size"][1]
    cuoi_a = tong - da[-1] if len(da) % 2 == 1 else tong
    cuoi_b = tong - db[-1] if len(db) % 2 == 1 else tong
    if cuoi_a <= db[0] or cuoi_b <= da[0]:
        return 0.0
    giao = 0
    hop = 0
    ia = ib = 0
    con_a = da[0] if da else 0
    con_b = db[0] if db else 0
    while ia < len(da) and ib < len(db):
        buoc = min(con_a, con_b)
        bat_a = ia % 2 == 1
        bat_b = ib % 2 == 1
        if bat_a and bat_b:
            giao += buoc
        if bat_a or bat_b:
            hop += buoc
        con_a -= buoc
        con_b -= buoc
        if con_a == 0:
            ia += 1
            con_a = da[ia] if ia < len(da) else 0
        if con_b == 0:
            ib += 1
            con_b = db[ib] if ib < len(db) else 0
    return 0.0 if hop == 0 else giao / hop


def rle_da_so(ds: list[list[int]]) -> list[int]:
    """Mat na da so: pixel bat khi HON MOT NUA so mat na bat no (Rle.DaSo)."""
    k = len(ds)
    chi_so = [0] * k
    con = [d[0] if d else 0 for d in ds]
    ra: list[int] = []
    dang_bat = False
    dang_dai = 0

    while True:
        het = False
        for i in range(k):
            while con[i] == 0 and chi_so[i] < len(ds[i]):
                chi_so[i] += 1
                con[i] = ds[i][chi_so[i]] if chi_so[i] < len(ds[i]) else 0
            if chi_so[i] >= len(ds[i]):
                het = True
        if het:
            break

        buoc = min(con)
        so_bat = sum(1 for i in range(k) if chi_so[i] % 2 == 1)
        bat = so_bat * 2 > k
        if bat == dang_bat:
            dang_dai += buoc
        else:
            ra.append(dang_dai)
            dang_bat = bat
            dang_dai = buoc
        for i in range(k):
            con[i] -= buoc

    ra.append(dang_dai)
    return ra


# =====================================================================
# CHUOI KHUNG (track)
# =====================================================================


def _khung_tai(moc: list[dict[str, Any]], t: float) -> tuple[float, float, float, float] | None:
    """Khung (x, y, w, h) tai thoi diem t, noi suy tuyen tinh; None neu khong ton tai / dang an (visible = false)."""
    if t < moc[0]["t"] or t > moc[-1]["t"]:
        return None
    for i in range(1, len(moc)):
        if t <= moc[i]["t"]:
            truoc, sau = moc[i - 1], moc[i]
            if truoc.get("visible", True) is False:
                return None
            f = (t - truoc["t"]) / (sau["t"] - truoc["t"])
            return (truoc["x"] + (f * (sau["x"] - truoc["x"])), truoc["y"] + (f * (sau["y"] - truoc["y"])),
                    truoc["w"] + (f * (sau["w"] - truoc["w"])), truoc["h"] + (f * (sau["h"] - truoc["h"])))
    chot = moc[-1]
    if chot.get("visible", True) is False:
        return None
    return chot["x"], chot["y"], chot["w"], chot["h"]


def iou_chuoi_khung(a: dict[str, Any], b: dict[str, Any]) -> float:
    """IoU khong-thoi gian cua hai chuoi khung (TrackTool.DoGiong)."""
    ma, mb = a["keyframes"], b["keyframes"]
    dau = min(ma[0]["t"], mb[0]["t"])
    cuoi = max(ma[-1]["t"], mb[-1]["t"])
    if cuoi <= dau:
        return 0.0

    tong_giao = 0.0
    tong_hop = 0.0
    for k in range(SO_MOC_MAU_TRACK):
        t = dau + ((k + 0.5) * (cuoi - dau) / SO_MOC_MAU_TRACK)
        ka = _khung_tai(ma, t)
        kb = _khung_tai(mb, t)
        dien_tich_a = 0.0 if ka is None else ka[2] * ka[3]
        dien_tich_b = 0.0 if kb is None else kb[2] * kb[3]
        giao = 0.0
        if ka is not None and kb is not None:
            rong = min(ka[0] + ka[2], kb[0] + kb[2]) - max(ka[0], kb[0])
            cao = min(ka[1] + ka[3], kb[1] + kb[3]) - max(ka[1], kb[1])
            giao = max(0, rong) * max(0, cao)
        tong_giao = tong_giao + giao
        tong_hop = tong_hop + dien_tich_a + dien_tich_b - giao
    return 0.0 if tong_hop <= 0 else tong_giao / tong_hop


# =====================================================================
# THUOC TINH CUA DOI TUONG (ThuocTinh.cs)
# =====================================================================


def _thuoc_tinh_khop(chuan: dict[str, Any], xet: dict[str, Any], tool: dict[str, Any]) -> bool:
    """Chi so cac thuoc tinh ban CHUAN co gia tri."""
    c = chuan.get("attributes")
    if not isinstance(c, dict):
        return True
    x = xet.get("attributes") if isinstance(xet.get("attributes"), dict) else {}
    for khai in tool.get("attributes", []):
        ten = khai["name"]
        if c.get(ten) is None:
            continue
        if x.get(ten) is None:
            return False
        if khai["type"] == "number":
            if abs(c[ten] - x[ten]) > SAI_SO_NGUONG:
                return False
        elif khai["type"] == "text":
            if cer(x[ten], c[ten]) > NGUONG_THU_VIEN["transcription"] + SAI_SO_NGUONG:
                return False
        elif type(c[ten]) is not type(x[ten]) or c[ten] != x[ten]:
            return False
    return True


def _thuoc_tinh_gop(thanh_vien: list[dict[str, Any]], tool: dict[str, Any]) -> dict[str, Any] | None:
    ra: dict[str, Any] = {}
    for khai in tool.get("attributes", []):
        ten = khai["name"]
        gia_tri = [m["attributes"][ten] for m in thanh_vien
                   if isinstance(m.get("attributes"), dict) and m["attributes"].get(ten) is not None]
        if not gia_tri:
            continue
        if khai["type"] == "number":
            ra[ten] = lam_tron(trung_vi(gia_tri))
        elif khai["type"] == "text":
            ra[ten] = gia_tri[ban_o_giua(gia_tri)]
        else:
            # choice / boolean: dem, giu thu tu gap dau tien. (True va "true" la hai gia tri khac nhau.)
            khoa: list[tuple[type, Any]] = []
            phieu: dict[tuple[type, Any], int] = {}
            for v in gia_tri:
                k = (type(v), v)
                if k not in phieu:
                    khoa.append(k)
                phieu[k] = phieu.get(k, 0) + 1
            for k in khoa:
                if phieu[k] * 2 > len(gia_tri):
                    ra[ten] = k[1]
    return ra or None


# =====================================================================
# CONG CU DANG DANH SACH: ghep cum (DanhSachToolBase.Gop)
# =====================================================================


def _so_de_sap_xep(so: float) -> str:
    """Giong so.ToString("0000000000.000") cua C#: lam tron nua ra xa 0 tren gia tri chinh xac."""
    return format(Decimal(so).quantize(Decimal("0.001"), rounding=ROUND_HALF_UP), "014.3f")


def _thu_tu(khoa: str) -> bytes:
    """So sanh chuoi kieu Ordinal cua C# = so sanh tung don vi UTF-16."""
    return khoa.encode("utf-16-be")


def _diem(p: dict[str, Any]) -> list[list[float]]:
    return p["points"]


class _Loai:
    """Hanh vi cua mot loai cong cu dang danh sach — tuong ung mot lop con cua DanhSachToolBase."""

    def __init__(self, do_giong: Callable[[dict, dict, float], float], khoa: Callable[[dict], str],
                 dai_dien: Callable[[list[dict], float], dict] | None = None,
                 ty_le: Callable[[dict | None, list[dict]], float] | None = None, trung_het: bool = False):
        self.do_giong = do_giong
        self.khoa = khoa
        self._dai_dien = dai_dien
        self._ty_le = ty_le
        self.trung_het = trung_het   # span / relation: ghep cap luon doi trung het

    def ty_le(self, mau: dict | None, tat_ca: list[dict]) -> float:
        return 1.0 if self._ty_le is None else self._ty_le(mau, tat_ca)

    def dai_dien(self, thanh_vien: list[dict], ty_le: float) -> dict:
        if self._dai_dien is not None:
            ra = self._dai_dien(thanh_vien, ty_le)
            if ra is not None:
                return ra
        # Mac dinh: thanh vien "o giua" — tong do giong toi cac thanh vien khac lon nhat (hoa thi lay dau).
        tot = 0
        nhieu_nhat = -math.inf
        for i in range(len(thanh_vien)):
            tong = 0.0
            for j in range(len(thanh_vien)):
                if i != j:
                    tong = tong + self.do_giong(thanh_vien[i], thanh_vien[j], ty_le)
            if tong > nhieu_nhat + SAI_SO_NGUONG:
                nhieu_nhat = tong
                tot = i
        return {k: v for k, v in thanh_vien[tot].items() if k not in ("labelId", "attributes")}


def _nho_do_giong(loai: _Loai) -> _Loai:
    """
    Ban sao cua `loai` co NHO ket qua do_giong theo cap doi tuong, dung trong MOT lan gop: cung mot cap bi
    hoi lai o buoc ghep cum, chon thanh vien "o giua" (ca hai chieu) va cham F1. Do giong doi xung nen
    (a, b) va (b, a) dung chung mot o. Khoa theo id() — cac doi tuong song suot lan gop.
    """
    da_tinh: dict[tuple[int, int], float] = {}
    goc = loai.do_giong

    def do_giong(a: dict, b: dict, ty_le: float) -> float:
        khoa = (id(a), id(b)) if id(a) <= id(b) else (id(b), id(a))
        g = da_tinh.get(khoa)
        if g is None:
            g = goc(a, b, ty_le)
            da_tinh[khoa] = g
        return g

    return _Loai(do_giong=do_giong, khoa=loai.khoa, dai_dien=loai._dai_dien, ty_le=loai._ty_le, trung_het=loai.trung_het)


def _trung_vi_cac_truong(*truong: str) -> Callable[[list[dict], float], dict]:
    def lam(thanh_vien: list[dict], _ty_le: float) -> dict:
        return {t: lam_tron(trung_vi([m[t] for m in thanh_vien])) for t in truong}
    return lam


def _khung_dai_dien(thanh_vien: list[dict], _ty_le: float) -> dict:
    ra = {t: lam_tron(trung_vi([m[t] for m in thanh_vien])) for t in ("x", "y", "w", "h")}
    if any("angle" in m for m in thanh_vien):
        ra["angle"] = lam_tron(trung_vi([m.get("angle", 0) or 0 for m in thanh_vien]))
    return ra


def _mat_na_dai_dien(thanh_vien: list[dict], _ty_le: float) -> dict | None:
    da_so = rle_da_so([m["rle"]["counts"] for m in thanh_vien])
    if len(da_so) < 2:
        return None   # khong pixel nao qua ban — lay mat na "o giua"
    return {"rle": {"size": list(thanh_vien[0]["rle"]["size"]), "counts": da_so}}


def _do_giong_da_giac(a: dict, b: dict, _ty_le: float) -> float:
    if not hinh_bao_giao_nhau(_diem(a), _diem(b)):
        return 0.0
    return iou_da_giac(_diem(a), _diem(b))


def _khoa_doan_van(p: dict) -> str:
    return f'{p["labelId"]}|{p["start"]}|{p["end"]}'


def _khoa_dau(p: dict, dau: str) -> str:
    return f'{p[dau]["start"]}-{p[dau]["end"]}'


def _khoa_quan_he(p: dict) -> str:
    return f'{p["labelId"]}|{_khoa_dau(p, "from")}|{_khoa_dau(p, "to")}'


_LOAI: dict[str, _Loai] = {
    "bbox": _Loai(
        do_giong=lambda a, b, _t: iou_khung(a, b),
        khoa=lambda p: "|".join([p["labelId"]] + [_so_de_sap_xep(p[k]) for k in ("x", "y", "w", "h")]),
        dai_dien=_khung_dai_dien),
    "polygon": _Loai(
        do_giong=_do_giong_da_giac,
        khoa=lambda p: f'{p["labelId"]}|{_so_de_sap_xep(_diem(p)[0][0])}|{_so_de_sap_xep(_diem(p)[0][1])}|{len(_diem(p))}'),
    "keypoint": _Loai(
        do_giong=lambda a, b, t: max(0.0, 1 - (khoang_cach(a["x"], a["y"], b["x"], b["y"]) / t)),
        khoa=lambda p: f'{p["labelId"]}|{_so_de_sap_xep(p["x"])}|{_so_de_sap_xep(p["y"])}',
        dai_dien=_trung_vi_cac_truong("x", "y"),
        ty_le=lambda mau, tat_ca: duong_cheo(mau, [[p["x"], p["y"]] for p in tat_ca])),
    "polyline": _Loai(
        do_giong=lambda a, b, t: max(0.0, 1 - (do_lech_duong(_diem(a), _diem(b)) / t)),
        khoa=lambda p: f'{p["labelId"]}|{_so_de_sap_xep(_diem(p)[0][0])}|{_so_de_sap_xep(_diem(p)[0][1])}|{len(_diem(p))}',
        ty_le=lambda mau, tat_ca: duong_cheo(mau, [d for p in tat_ca for d in _diem(p)])),
    "mask": _Loai(
        do_giong=lambda a, b, _t: rle_iou(a["rle"], b["rle"]),
        khoa=lambda p: f'{p["labelId"]}|{_so_de_sap_xep(p["rle"]["counts"][0])}|{_so_de_sap_xep(len(p["rle"]["counts"]))}',
        dai_dien=_mat_na_dai_dien),
    "span": _Loai(
        do_giong=lambda a, b, _t: 1.0 if _khoa_doan_van(a) == _khoa_doan_van(b) else 0.0,
        khoa=lambda p: f'{_so_de_sap_xep(p["start"])}|{p["labelId"]}',
        trung_het=True),
    "relation": _Loai(
        do_giong=lambda a, b, _t: 1.0 if _khoa_quan_he(a) == _khoa_quan_he(b) else 0.0,
        khoa=lambda p: "|".join([_so_de_sap_xep(p["from"]["start"]), _so_de_sap_xep(p["from"]["end"]),
                                 _so_de_sap_xep(p["to"]["start"]), _so_de_sap_xep(p["to"]["end"]), p["labelId"]]),
        trung_het=True),
    "temporalSegment": _Loai(
        do_giong=lambda a, b, _t: iou_doan(a["start"], a["end"], b["start"], b["end"]),
        khoa=lambda p: f'{_so_de_sap_xep(p["start"])}|{p["labelId"]}|{_so_de_sap_xep(p["end"])}',
        dai_dien=_trung_vi_cac_truong("start", "end")),
    "track": _Loai(
        do_giong=lambda a, b, _t: iou_chuoi_khung(a, b),
        khoa=lambda p: "|".join([p["labelId"]] + [_so_de_sap_xep(p["keyframes"][0][k]) for k in ("t", "x", "y")])),
}


def _ghep_tham_lam(cap: list[tuple[float, int, int]], so_phan_tu: int, so_dich: int) -> list[tuple[int, int]]:
    """Ghep mot-mot: cap giong nhat truoc; hoa thi dich nho truoc, roi phan tu nho truoc. → [(dich, phan_tu)]."""
    cap.sort(key=lambda c: (-c[0], c[1], c[2]))
    da_dung = [False] * so_phan_tu
    dich_da_nhan = [False] * so_dich
    ra = []
    for _g, dich, phan_tu in cap:
        if da_dung[phan_tu] or dich_da_nhan[dich]:
            continue
        da_dung[phan_tu] = True
        dich_da_nhan[dich] = True
        ra.append((dich, phan_tu))
    return ra


def _diem_f1(cua_nguoi: list[dict], chuan: list[dict], tool: dict, loai: _Loai, nguong: float, ty_le: float) -> float:
    """F1 giua doi tuong cua mot nguoi va ban chuan (DanhSachToolBase.DiemF1). Ca hai rong = 1."""
    if not cua_nguoi and not chuan:
        return 1.0
    cap = []
    for o, phan_tu in enumerate(cua_nguoi):
        for c, dich in enumerate(chuan):
            if dich["labelId"] != phan_tu["labelId"]:
                continue
            g = loai.do_giong(phan_tu, dich, ty_le)
            if g >= nguong - SAI_SO_NGUONG and _thuoc_tinh_khop(dich, phan_tu, tool):
                cap.append((g, c, o))
    dung = len(_ghep_tham_lam(cap, len(cua_nguoi), len(chuan)))
    return (2.0 * dung) / (len(cua_nguoi) + len(chuan))


def _gop_danh_sach(tool: dict, ds: list[list[dict]], mau: dict | None, nguong_mac_dinh: float | None,
                   f1_toi_thieu: float) -> dict[str, Any]:
    loai = _nho_do_giong(_LOAI[tool["kind"]])
    so_nguoi = len(ds)
    if loai.trung_het:
        nguong = 1.0
    else:
        nguong = tool.get("matchThreshold")
        if nguong is None:
            nguong = nguong_mac_dinh if nguong_mac_dinh is not None else NGUONG_THU_VIEN[tool["kind"]]
    ty_le = loai.ty_le(mau, [p for mot in ds for p in mot])

    # 1. Ghep cum: moi nguoi ghep doi tuong cua minh vao cac cum DA CO (do nguoi truoc tao), theo doi tuong
    #    MO DAU cum; khong ghep duoc thi mo cum moi.
    cum: list[list[dict]] = []
    for cua_nguoi in ds:
        so_cum_cu = len(cum)
        cap = []
        for o, phan_tu in enumerate(cua_nguoi):
            for c in range(so_cum_cu):
                if cum[c][0]["labelId"] != phan_tu["labelId"]:
                    continue
                g = loai.do_giong(phan_tu, cum[c][0], ty_le)
                if g >= nguong - SAI_SO_NGUONG:
                    cap.append((g, c, o))
        da_dung = [False] * len(cua_nguoi)
        for c, o in _ghep_tham_lam(cap, len(cua_nguoi), so_cum_cu):
            cum[c].append(cua_nguoi[o])
            da_dung[o] = True
        for o, phan_tu in enumerate(cua_nguoi):
            if not da_dung[o]:
                cum.append([phan_tu])

    # 2. Chot cum duoc hon mot nua so nguoi ve.
    chot: list[tuple[dict, int]] = []
    for thanh_vien in cum:
        if len(thanh_vien) * 2 <= so_nguoi:
            continue
        dd = dict(loai.dai_dien(thanh_vien, ty_le))
        dd["labelId"] = thanh_vien[0]["labelId"]
        thuoc_tinh = _thuoc_tinh_gop(thanh_vien, tool)
        if thuoc_tinh is not None:
            dd["attributes"] = thuoc_tinh
        chot.append((dd, len(thanh_vien)))
    chot.sort(key=lambda k: _thu_tu(loai.khoa(k[0])))   # sort cua Python on dinh, nhu OrderBy cua C#
    doi_tuong = [k[0] for k in chot]

    # 3. Tung nguoi co dong y voi ket qua chot khong. MOT nguoi thi ket qua chot chinh la nhan cua ho → luon khop.
    dong_y = [so_nguoi == 1 or _diem_f1(cua_nguoi, doi_tuong, tool, loai, nguong, ty_le) >= f1_toi_thieu - SAI_SO_NGUONG
              for cua_nguoi in ds]
    tranh_chap = sum(dong_y) * 2 <= so_nguoi

    ra: dict[str, Any] = {"final": None if tranh_chap else doi_tuong, "agrees": dong_y, "disputed": tranh_chap}
    if not tranh_chap:
        ra["supports"] = [k[1] for k in chot]
    return ra


# =====================================================================
# CONG CU CA MAU
# =====================================================================


def _gop_phan_loai(ds: list[Any]) -> dict[str, Any]:
    n = len(ds)
    phieu: dict[str, int] = {}
    for k in ds:
        for lop in set(k.get("labelIds", [])):
            phieu[lop] = phieu.get(lop, 0) + 1
    # Sap xep nhu SortedDictionary(StringComparer.Ordinal) cua C#.
    chot = sorted((lop for lop, v in phieu.items() if v * 2 > n), key=_thu_tu)
    tap = set(chot)
    return {
        "final": {"labelIds": chot} if chot else None,
        "agrees": [bool(chot) and set(k.get("labelIds", [])) == tap for k in ds],
        "disputed": not chot,
    }


def _gop_cap(ds: list[Any]) -> dict[str, Any]:
    n = len(ds)
    phieu: dict[str, int] = {}
    for k in ds:
        phieu[k["choice"]] = phieu.get(k["choice"], 0) + 1
    thang = next((c for c, v in phieu.items() if v * 2 > n), None)
    return {
        "final": {"choice": thang} if thang is not None else None,
        "agrees": [thang is not None and k["choice"] == thang for k in ds],
        "disputed": thang is None,
    }


def _ket_qua_theo_dong_y(final: Any, dong_y: list[bool]) -> dict[str, Any]:
    tranh_chap = sum(dong_y) * 2 <= len(dong_y)
    return {"final": None if tranh_chap else final, "agrees": dong_y, "disputed": tranh_chap}


def _gop_chep_loi(tool: dict, ds: list[Any], nguong_mac_dinh: float | None) -> dict[str, Any]:
    chu = [k["text"] for k in ds]
    giua = chu[ban_o_giua(chu)]
    nguong = tool.get("matchThreshold")
    if nguong is None:
        nguong = nguong_mac_dinh if nguong_mac_dinh is not None else NGUONG_THU_VIEN["transcription"]
    return _ket_qua_theo_dong_y({"text": giua}, [cer(c, giua) <= nguong + SAI_SO_NGUONG for c in chu])


def _gop_so(tool: dict, ds: list[Any]) -> dict[str, Any]:
    gia_tri = [k["value"] for k in ds]
    giua = trung_vi(gia_tri)
    giua = math.floor(giua + 0.5) if tool.get("integer") else lam_tron(giua)
    sai_so = tool.get("tolerance") or 0
    return _ket_qua_theo_dong_y({"value": giua}, [abs(v - giua) <= sai_so + SAI_SO_NGUONG for v in gia_tri])


def _gop_xep_hang(tool: dict, ds: list[Any], nguong_mac_dinh: float | None) -> dict[str, Any]:
    thu_tu = [list(k["order"]) for k in ds]
    diem: dict[int, int] = {}
    for mot in thu_tu:
        for vi_tri, muc in enumerate(mot):
            diem[muc] = diem.get(muc, 0) + (len(mot) - 1 - vi_tri)
    chot = sorted(diem, key=lambda muc: (-diem[muc], muc))
    nguong = tool.get("matchThreshold")
    if nguong is None:
        nguong = nguong_mac_dinh if nguong_mac_dinh is not None else NGUONG_THU_VIEN["ranking"]
    ra = _ket_qua_theo_dong_y({"order": chot}, [kendall(mot, chot) >= nguong - SAI_SO_NGUONG for mot in thu_tu])
    ra["scores"] = {str(muc): diem[muc] for muc in sorted(diem)}
    return ra


def gop_cong_cu(tool: dict[str, Any], ket_qua: list[Any], mau: dict[str, Any] | None = None,
                nguong_mac_dinh: float | None = None, f1_toi_thieu: float = F1_TOI_THIEU_MAC_DINH) -> dict[str, Any] | None:
    """
    Gop ket qua cua nhieu nguoi cho MOT cong cu (ket_qua: moi nguoi mot phan tu, theo thu tu nop).

    Tra ve {"final": ... | None, "agrees": [bool, ...], "disputed": bool, ...}:
      final     ket qua gop; None = TRANH CHAP (khong co ket qua duoc qua nua so nguoi ung ho)
      agrees    tung nguoi (theo thu tu dua vao) co khop ket qua gop khong
      supports  (cong cu dang danh sach) so nguoi ve tung doi tuong trong final
    None = cong cu khong gop tu dong (freeText).

    nguong_mac_dinh: setting labeling.threshold.<loai>; None = mac dinh cua thu vien. matchThreshold
    cua cong cu luon duoc uu tien. mau: metadata cua mau (width / height cho diem moc, duong gap khuc).
    """
    kind = tool["kind"]
    if kind in CONG_CU_KHONG_GOP:
        return None
    if kind == "classification":
        return _gop_phan_loai(ket_qua)
    if kind == "pairwise":
        return _gop_cap(ket_qua)
    if kind == "transcription":
        return _gop_chep_loi(tool, ket_qua, nguong_mac_dinh)
    if kind == "number":
        return _gop_so(tool, ket_qua)
    if kind == "ranking":
        return _gop_xep_hang(tool, ket_qua, nguong_mac_dinh)
    if kind in _LOAI:
        return _gop_danh_sach(tool, [list(k) for k in ket_qua], mau, nguong_mac_dinh, f1_toi_thieu)
    raise ValueError(f"Chua ho tro gop cong cu '{kind}'")
