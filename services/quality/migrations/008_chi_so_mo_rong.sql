-- Chỉ số độ tin cậy cho MỌI loại công cụ (FQ-02).
--
-- project_metrics trước chỉ có Krippendorff alpha của công cụ chọn một giá trị (DS). Nay mỗi công cụ
-- đo được đều có alpha, kèm tên hàm khoảng cách dùng (nominal / interval / cer / kendall / set_f1) và
-- Fleiss' kappa cho công cụ nominal.
ALTER TABLE project_metrics ADD COLUMN metric varchar(20) NULL;
ALTER TABLE project_metrics ADD COLUMN kappa double precision NULL;

-- STAPLE (mặt nạ): độ nhạy / độ đặc hiệu từng labeler theo (dự án, công cụ). Chỉ để chủ dự án xem
-- trong báo cáo labeler — KHÔNG đưa vào điểm uy tín (uy tín vẫn từ đồng thuận + câu vàng + DS).
CREATE TABLE staple_skills (
    labeler_id    uuid NOT NULL,
    project_id    uuid NOT NULL,
    tool          varchar(40) NOT NULL,
    sensitivity   double precision NOT NULL,
    specificity   double precision NOT NULL,
    sample_count  integer NOT NULL,
    computed_at   timestamptz NOT NULL,
    CONSTRAINT pk_staple_skills PRIMARY KEY (labeler_id, project_id, tool)
);
