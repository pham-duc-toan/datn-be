-- Chống gian lận (FM-07). quality-svc nghe assignment.submitted, giữ dấu vết từng lượt nộp
-- và mở HỒ SƠ NGHI VẤN cho admin; admin quyết định (bỏ qua / xác nhận, có thể khoá tài khoản).

CREATE TABLE fraud_submissions (
    assignment_id uuid PRIMARY KEY,
    project_id    uuid NOT NULL,
    task_id       uuid NOT NULL,
    labeler_id    uuid NOT NULL,
    work_seconds  double precision NOT NULL,       -- submitted_at - leased_at
    answer        text,                            -- đáp án công cụ phân loại đầu tiên (lớp, nối bằng ','), null nếu không có
    client_ip     varchar(64),
    device_id     varchar(100),
    submitted_at  timestamptz NOT NULL
);

-- Cửa sổ N lượt gần nhất của labeler trong dự án (quá nhanh, một nhãn liên tục).
CREATE INDEX ix_fraud_sub_labeler ON fraud_submissions (labeler_id, project_id, submitted_at DESC);
-- Trung vị thời gian làm của dự án.
CREATE INDEX ix_fraud_sub_project ON fraud_submissions (project_id, submitted_at DESC);
-- Chung IP / thiết bị trong khoảng thời gian.
CREATE INDEX ix_fraud_sub_ip ON fraud_submissions (client_ip, submitted_at) WHERE client_ip IS NOT NULL;
CREATE INDEX ix_fraud_sub_device ON fraud_submissions (device_id, submitted_at) WHERE device_id IS NOT NULL;

CREATE TABLE fraud_cases (
    id           uuid PRIMARY KEY,
    type         varchar(30) NOT NULL,            -- qua_nhanh | mot_nhan_lien_tuc | chung_ip | chung_thiet_bi
    case_key     varchar(200) NOT NULL,           -- khoá gộp trùng: cùng tín hiệu cùng đối tượng → cập nhật hồ sơ đang mở
    project_id   uuid,
    user_ids     uuid[] NOT NULL,
    evidence     jsonb NOT NULL,
    status       varchar(20) NOT NULL,            -- open | dismissed | confirmed
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,
    resolved_at  timestamptz,
    resolved_by  uuid,
    note         varchar(1000),
    blocked      boolean NOT NULL DEFAULT false
);

-- Mỗi khoá chỉ MỘT hồ sơ đang mở (hai consumer cùng phát hiện → một bên cập nhật).
CREATE UNIQUE INDEX ux_fraud_cases_mo ON fraud_cases (case_key) WHERE status = 'open';
CREATE INDEX ix_fraud_cases_hang_doi ON fraud_cases (status, created_at);
CREATE INDEX ix_fraud_cases_key ON fraud_cases (case_key, resolved_at);
