"""Cham cau tra loi MO (freeText) theo nghia — kien truc 3.19. Logic thuan, khong DB, de test.

Cau tra loi mo khong co dap an dinh san nen khong so bang ("Con meo ngu tren ghe" va "Chu meo say giac
tren sofa" la mot y). Moi cau duoc ml-svc nhung thanh vector (chuan hoa L2: tich vo huong = cosine):

  - "da so" cua mot cau hoi = cau tra loi O GIUA (medoid): cau co tong do giong voi cac cau khac lon nhat;
  - mot nguoi GIONG da so khi do giong voi medoid >= nguong (setting quality.freetext_similarity);
  - cau hoi CO da so khi qua mot nua so nguoi lam cong cu do giong medoid — khong thi tranh chap
    (moi nguoi mot y), khong tinh cho ai, giong cac loai cong cu khac.
  - cau bo trong (chuoi rong) khong bao gio giong.

Du phong khi ml-svc khong phan tich kip (quality.freetext_analysis_timeout): cung thuat toan nhung do
giong bang n-gram ky tu (so CHU, nguong quality.freetext_lexical_similarity) — yeu hon so nghia, chi de
khong giu tien cua labeler vo thoi han.
"""

import math
import re
import unicodedata
from collections import Counter
from collections.abc import Callable, Sequence
from typing import Any

THEO_NGHIA = "semantic"
THEO_CHU = "lexical"


def van_ban(ket_qua: Any) -> str:
    """Payload cua cong cu freeText ({"text": "..."}) → chuoi (rong neu khong co)."""
    if isinstance(ket_qua, dict):
        t = ket_qua.get("text")
        return t.strip() if isinstance(t, str) else ""
    return ""


def _ngram(s: str, n: int = 3) -> Counter:
    s = unicodedata.normalize("NFC", s).lower()
    s = re.sub(r"[^\w\s]", " ", s)
    s = " " + re.sub(r"\s+", " ", s).strip() + " "
    return Counter(s[i:i + n] for i in range(max(1, len(s) - n + 1)))


def giong_chu(a: str, b: str) -> float:
    """Cosine giua tan suat n-gram 3 ky tu (khong phan biet hoa thuong, bo dau cau)."""
    if not a or not b:
        return 0.0
    x, y = _ngram(a), _ngram(b)
    tich = sum(v * y[k] for k, v in x.items())
    chuan = math.sqrt(sum(v * v for v in x.values()) * sum(v * v for v in y.values()))
    return 0.0 if chuan == 0 else tich / chuan


def giong_nghia(a: Sequence[float], b: Sequence[float]) -> float:
    """Tich vo huong hai vector da chuan hoa L2 (= cosine)."""
    return float(sum(x * y for x, y in zip(a, b)))


def gop_mo(cau: list[str], vector: list[Sequence[float] | None] | None, nguong: float) -> dict[str, Any]:
    """
    cau: cau tra loi cua tung nguoi lam cong cu nay (theo thu tu); vector: vector cung thu tu (None = so chu).
    Tra {"final": {"text": medoid} | None, "agrees": [bool...], "disputed": bool, "similarity": [float...]}.
    """
    n = len(cau)
    co = [i for i, c in enumerate(cau) if c]
    if not co:
        return {"final": None, "agrees": [False] * n, "disputed": True, "similarity": [0.0] * n}

    theo_nghia = vector is not None and all(vector[i] is not None for i in co)
    do: Callable[[int, int], float]
    if theo_nghia:
        do = lambda i, j: giong_nghia(vector[i], vector[j])  # noqa: E731
    else:
        do = lambda i, j: giong_chu(cau[i], cau[j])  # noqa: E731

    # Medoid: tong do giong voi cac cau khac lon nhat; hoa thi cau dung truoc (thu tu nop — tat dinh).
    tot, giua = -math.inf, co[0]
    for i in co:
        tong = sum(do(i, j) for j in co if j != i)
        if tong > tot + 1e-12:
            tot, giua = tong, i

    giong = [1.0 if i == giua else (do(i, giua) if i in co else 0.0) for i in range(n)]
    agrees = [i in co and giong[i] >= nguong for i in range(n)]
    ung_ho = sum(agrees)
    tranh_chap = not (2 * ung_ho > n)
    return {"final": None if tranh_chap else {"text": cau[giua]}, "agrees": agrees, "disputed": tranh_chap,
            "similarity": [round(g, 4) for g in giong]}
