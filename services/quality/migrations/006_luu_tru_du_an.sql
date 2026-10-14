-- Dọn dữ liệu vận hành của dự án ĐÃ ĐÓNG (setting project.closed_data_retention).
--
-- labels (nhãn thô kèm jsonb), consensus_rounds và task_targets chỉ phục vụ việc tính đồng thuận khi dự án
-- đang chạy. Dự án đóng rồi thì không tính nữa, nhưng ba bảng này cứ lớn dần theo tổng số nhãn từng nộp.
-- Giữ lại: label_agreements, gold_answers, ds_skills — uy tín labeler tính TOÀN CỤC từ ba bảng đó, xoá là
-- đổi điểm của người ta.
--
-- Ba mốc trên projects:
--   closed_at   — nhận project.completed / project.cancelled;
--   archived_at — đã chụp báo cáo chất lượng vào project_reports (từ đây API đọc bản chụp, nhãn đến trễ bị bỏ);
--   purged_at   — đã xoá xong (xoá theo lô, dừng giữa chừng thì vòng sau xoá tiếp).
ALTER TABLE projects ADD COLUMN closed_at timestamptz NULL;
ALTER TABLE projects ADD COLUMN archived_at timestamptz NULL;
ALTER TABLE projects ADD COLUMN purged_at timestamptz NULL;

CREATE INDEX ix_projects_cho_don ON projects (closed_at) WHERE closed_at IS NOT NULL AND purged_at IS NULL;

-- Bản chụp đúng hình dạng hai API /quality/projects/{id}/summary và /labelers trả ra.
CREATE TABLE project_reports (
    project_id  uuid PRIMARY KEY,
    summary     jsonb NOT NULL,
    labelers    jsonb NOT NULL,
    created_at  timestamptz NOT NULL
);

-- Xoá theo dự án: task_targets trước giờ chỉ có khoá chính task_id.
CREATE INDEX ix_task_targets_project ON task_targets (project_id);
