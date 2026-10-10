"""Chay TLC cho moi bien the cua TienVaDongDuAn va kiem KY VONG:
  - "dung"     : khong bat bien nao bi vi pham;
  - bien the loi: TLC PHAI tim ra vi pham dung bat bien da biet (mo hinh bat duoc loi do).

    python spec/tla/chay_tlc.py              # moi bien the
    python spec/tla/chay_tlc.py dung         # mot bien the

Can Java 11+. Lan dau tu tai tla2tools.jar (v1.7.4) vao spec/tla/.tools/.
Ket qua (so trang thai, trace rut gon) ghi vao spec/tla/ket-qua/. Thoat 1 neu sai ky vong.
"""

import os
import re
import subprocess
import sys
import time
import urllib.request

THU_MUC = os.path.dirname(os.path.abspath(__file__))
JAR = os.path.join(THU_MUC, ".tools", "tla2tools.jar")
URL_JAR = "https://github.com/tlaplus/tlaplus/releases/download/v1.7.4/tla2tools.jar"
KET_QUA = os.path.join(THU_MUC, "ket-qua")

# bien the → bat bien PHAI bi vi pham (None = khong duoc vi pham gi)
KY_VONG = {
    "dung": None,
    "hien_tai": {"KhongNoLabeler", "KhongDLQ"},
    "loi_khong_idempotent": {"ChiMotLan"},
    "loi_dong_khong_dem_nhan": {"KhongBoSotLuotNop", "KhongDLQ"},
    "loi_dong_khong_han_kn": {"KhongTuocQuyenKhieuNai"},
    "loi_dong_khi_con_lease": {"KhongBoSotLuotNop", "KhongDLQ"},
}

BIEN_QUAN_TAM = ["duAn", "taskTT", "annDong", "annFinal", "nhan", "conHan", "daNopTask", "lease",
                 "kyQuyTT", "kyQuy", "dn", "soLanChi", "treo", "khaDung", "rut", "dlq"]


def tai_jar() -> None:
    if not os.path.exists(JAR):
        os.makedirs(os.path.dirname(JAR), exist_ok=True)
        print("tai tla2tools.jar ...", flush=True)
        urllib.request.urlretrieve(URL_JAR, JAR)


def trace_rut_gon(out: str) -> str:
    """Moi buoc: ten action + cac bien (quan tam) DOI so voi buoc truoc."""
    dong = []
    cu: dict[str, str] = {}
    for m in re.finditer(r"^State (\d+): <?(\w[^>\n]*)>?\n(.*?)(?=^State \d+:|\Z)", out, flags=re.M | re.S):
        so, hd, than = m.group(1), m.group(2), m.group(3).split("\n\n")[0]
        gt = {}
        for mm in re.finditer(r"^/\\ (\w+) = (.*?)(?=^/\\ |\Z)", than, flags=re.M | re.S):
            gt[mm.group(1)] = " ".join(mm.group(2).split())
        doi = [f"{k}={gt[k]}" for k in BIEN_QUAN_TAM if k in gt and cu.get(k) != gt[k]]
        dong.append(f"{so:>3} {hd.split(' ')[0]:16s} " + "; ".join(doi))
        cu = gt
    return "\n".join(dong)


def chay(ten: str) -> bool:
    # Bien the loi chay MOT luong: BFS tuan tu cho trace NGAN NHAT va giong nhau moi lan (tai lap).
    t0 = time.time()
    p = subprocess.run(
        ["java", "-XX:+UseParallelGC", "-cp", JAR, "tlc2.TLC", "-config", f"{ten}.cfg",
         "-workers", "auto" if KY_VONG[ten] is None else "1", "-cleanup", "-metadir", os.path.join(KET_QUA, "states", ten), "TienVaDongDuAn.tla"],
        cwd=THU_MUC, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = p.stdout + p.stderr
    vi_pham = set(re.findall(r"Invariant (\w+) is violated", out))
    so_tt = re.search(r"([\d,]+) states generated, ([\d,]+) distinct states found", out)
    hoan_tat = "Model checking completed" in out or vi_pham
    if not hoan_tat:
        ket = False
    elif KY_VONG[ten] is None:
        ket = not vi_pham and "No error has been found" in out
    else:
        ket = bool(vi_pham & KY_VONG[ten])
    with open(os.path.join(KET_QUA, f"{ten}.txt"), "w", encoding="utf-8") as f:
        f.write(out)
    if vi_pham:
        with open(os.path.join(KET_QUA, f"{ten}.trace.txt"), "w", encoding="utf-8") as f:
            f.write(f"Vi pham: {', '.join(sorted(vi_pham))}\n\n" + trace_rut_gon(out) + "\n")
    print(f"{'DAT ' if ket else 'SAI '} {ten:26s} "
          f"{'vi pham ' + ','.join(sorted(vi_pham)) if vi_pham else 'khong vi pham':42s} "
          f"{(so_tt.group(1) + ' trang thai / ' + so_tt.group(2) + ' phan biet') if so_tt else '':42s} {time.time() - t0:.0f}s",
          flush=True)
    return ket


if __name__ == "__main__":
    os.makedirs(KET_QUA, exist_ok=True)
    tai_jar()
    ds = sys.argv[1:] or list(KY_VONG)
    ok = all([chay(t) for t in ds])
    print("TAT CA DUNG KY VONG" if ok else "CO BIEN THE SAI KY VONG")
    sys.exit(0 if ok else 1)
