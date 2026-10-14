"""Dọn MỘT LẦN dữ liệu thừa sinh ra TRƯỚC đợt tối ưu lưu trữ (kiến trúc mục 3.14).

Code mới tự xử lý dữ liệu phát sinh từ nay; hai việc dưới đây cần biết dữ liệu của service khác
(task_db) nên không viết thành migration của từng service được:

  1. gate_db: xoá mẫu + câu vàng của dự án ĐÃ PUBLISH mà không bật cổng link, và của dự án cổng link
     đã đóng. Dự án còn nháp (chưa publish) không đụng tới — lúc publish mới biết có bật cổng link không.
  2. quality_db: điền projects.closed_at cho dự án đã đóng từ trước (quality-svc mới nghe
     project.completed / project.cancelled từ bản này) — để project.closed_data_retention bắt đầu đếm.

Mặc định CHỈ IN những gì sẽ làm. Thêm --thuc-hien để ghi thật. Stack demo: thêm --demo.

    python deploy/don_du_lieu_cu.py                    # dev, xem trước
    python deploy/don_du_lieu_cu.py --thuc-hien        # dev, làm thật
    python deploy/don_du_lieu_cu.py --demo --thuc-hien
"""

import argparse
import subprocess
import sys


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--demo", action="store_true", help="stack demo (container datn-demo-db-*)")
    ap.add_argument("--thuc-hien", action="store_true", help="ghi thật (mặc định chỉ in)")
    a = ap.parse_args()
    tien_to = "datn-demo-db-" if a.demo else "datn-db-"

    def sql(db: str, cau: str) -> list[list[str]]:
        kq = subprocess.run(["docker", "exec", "-i", tien_to + db, "psql", "-U", f"{db}_user", "-d", f"{db}_db",
                             "-v", "ON_ERROR_STOP=1", "-At", "-F", "|", "-c", cau],
                            capture_output=True, text=True, encoding="utf-8")
        if kq.returncode != 0:
            raise SystemExit(f"[{db}] {kq.stderr.strip()}")
        return [d.split("|") for d in kq.stdout.splitlines() if d and not d.startswith(("DELETE", "UPDATE"))]

    def danh_sach(ids) -> str:
        return ",".join(f"'{i}'" for i in ids) or "NULL"

    # ---- nguồn sự thật về trạng thái dự án phía vận hành: bản sao của task-svc
    du_an = sql("task", "select project_id, status, status_changed_at from project_snapshots where is_configured")
    da_publish = [d[0] for d in du_an]
    da_dong = [(d[0], d[2]) for d in du_an if d[1] == "Closed"]
    print(f"task_db: {len(da_publish)} dự án đã publish, {len(da_dong)} đã đóng")

    # ---- 1. gate: mẫu của dự án đã publish mà gate không phục vụ (không bật cổng link / cổng đã đóng)
    dang_phuc_vu = {d[0] for d in sql("gate", "select project_id from gate_projects where status <> 'Closed'")}
    thua = [p for p in da_publish if p not in dang_phuc_vu]
    so_mau = sql("gate", f"select count(*), count(distinct project_id) from gate_samples where project_id in ({danh_sach(thua)})")[0]
    so_vang = sql("gate", f"select count(*) from gate_gold_items where project_id in ({danh_sach(thua)})")[0][0]
    print(f"gate_db: {so_mau[0]} mẫu + {so_vang} câu vàng của {so_mau[1]} dự án không phục vụ qua cổng link "
          f"(giữ {len(dang_phuc_vu)} dự án đang phục vụ, không đụng dự án còn nháp)")
    if a.thuc_hien and thua:
        sql("gate", f"delete from gate_gold_items where project_id in ({danh_sach(thua)})")
        sql("gate", f"delete from gate_samples where project_id in ({danh_sach(thua)})")
        print("   → đã xoá")

    # ---- 2. quality: mốc đóng của dự án đã đóng từ trước
    thieu = {d[0] for d in sql("quality", "select project_id from projects where closed_at is null")}
    can_dien = [(p, luc) for p, luc in da_dong if p in thieu]
    print(f"quality_db: {len(can_dien)} dự án đã đóng chưa có closed_at")
    if a.thuc_hien:
        for p, luc in can_dien:
            sql("quality", f"update projects set closed_at = '{luc}' where project_id = '{p}' and closed_at is null")
        if can_dien:
            print("   → đã điền")

    if not a.thuc_hien:
        print("\n(chưa ghi gì — thêm --thuc-hien để làm thật)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
