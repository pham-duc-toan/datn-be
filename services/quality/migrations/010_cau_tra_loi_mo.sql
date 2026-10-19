-- Chấm câu trả lời mở (freeText) theo NGHĨA — kiến trúc 3.19.
--
-- Mỗi câu đã trả lời của bài nộp, với mỗi công cụ freeText của tập nhãn: một dòng vector nhúng câu do
-- ml-svc tính (chuẩn hoá L2). vector NULL = công cụ bỏ trống / không làm (vẫn ghi dòng để worker không
-- chọn lại câu đó). Worker nhúng NGAY khi bài nộp tới, nên lúc đóng thu bài gần như đã đủ.
CREATE TABLE freetext_vectors (
    attempt_id  uuid NOT NULL,
    sample_id   uuid NOT NULL,
    tool        varchar(100) NOT NULL,
    project_id  uuid NOT NULL,
    model       varchar(200) NULL,
    vector      real[] NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_freetext_vectors PRIMARY KEY (attempt_id, sample_id, tool)
);
CREATE INDEX ix_freetext_vectors_project ON freetext_vectors (project_id);

-- Đợt chấm chờ phân tích câu trả lời mở: pending (chờ ml-svc) → done. method: semantic (so nghĩa)
-- | lexical (so chữ — ml-svc không phân tích kịp trong quality.freetext_analysis_timeout, hoặc không cấu hình).
ALTER TABLE project_collections ADD COLUMN freetext_state varchar(20) NULL;
ALTER TABLE project_collections ADD COLUMN freetext_method varchar(20) NULL;
ALTER TABLE project_collections ADD COLUMN freetext_correlation_id uuid NULL;
CREATE INDEX ix_project_collections_freetext_pending ON project_collections (project_id) WHERE freetext_state = 'pending';
