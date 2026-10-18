-- Dự án làm trung tâm (kiến trúc mục 3.18).
--
-- Labeler làm CẢ BÀI rồi nộp một lần (attempt.submitted); task-svc đóng thu bài (project.collection_finished)
-- → quality-svc chấm MỘT lần cả dự án: so từng câu với đa số, cộng câu vàng, so ngưỡng t → project.graded.
-- Không còn đồng thuận từng task, redundancy thích ứng, gold.answered.
--
-- Giữ các bảng cũ (labels, label_agreements, gold_answers...) — dữ liệu cũ vẫn là bằng chứng uy tín.

-- Bản sao dự án: tham số chấm của bản v2; redundancy không còn ý nghĩa.
ALTER TABLE projects ALTER COLUMN redundancy DROP NOT NULL;
ALTER TABLE projects ALTER COLUMN max_redundancy DROP NOT NULL;
ALTER TABLE projects ADD COLUMN pass_threshold_percent integer NULL;
ALTER TABLE projects ADD COLUMN slots integer NULL;
ALTER TABLE projects ADD COLUMN question_count integer NULL;

-- labels: một dòng mỗi câu ĐÃ TRẢ LỜI của bài nộp (khóa tất định uuid5(bài, mẫu)); task_id chỉ còn ở dữ liệu cũ.
ALTER TABLE labels ALTER COLUMN task_id DROP NOT NULL;
ALTER TABLE labels ADD COLUMN attempt_id uuid NULL;
CREATE UNIQUE INDEX ux_labels_attempt_sample ON labels (attempt_id, sample_id) WHERE attempt_id IS NOT NULL;

-- Bài đã nộp. sample_ids = các câu THẬT trong đề (cả câu bỏ trống) — câu bỏ trống tính là không giống đa số.
CREATE TABLE attempts (
    attempt_id      uuid PRIMARY KEY,
    project_id      uuid NOT NULL,
    labeler_id      uuid NOT NULL,
    sample_ids      uuid[] NOT NULL,
    answered_count  integer NOT NULL,
    gold_total      integer NOT NULL,
    gold_correct    integer NOT NULL,
    started_at      timestamptz NOT NULL,
    submitted_at    timestamptz NOT NULL
);
CREATE INDEX ix_attempts_project ON attempts (project_id);

-- Đợt thu bài của dự án (project.collection_finished): danh sách bài PHẢI chấm, câu bị loại, mốc chấm.
-- Chấm khi đã có đủ mọi bài trong danh sách (attempt.submitted có thể tới SAU, hai queue khác nhau).
CREATE TABLE project_collections (
    project_id           uuid PRIMARY KEY,
    reason               varchar(20) NOT NULL,
    attempt_ids          uuid[] NOT NULL,
    excluded_sample_ids  uuid[] NOT NULL,
    finished_at          timestamptz NOT NULL,
    graded_at            timestamptz NULL,
    majority_used        boolean NULL
);

-- Kết quả gộp đa số của từng câu lúc chấm (báo cáo cho chủ dự án).
CREATE TABLE sample_majorities (
    project_id   uuid NOT NULL,
    sample_id    uuid NOT NULL,
    -- agreed | disputed | notApplicable
    status       varchar(20) NOT NULL,
    final        jsonb NULL,
    label_count  integer NOT NULL,
    CONSTRAINT pk_sample_majorities PRIMARY KEY (project_id, sample_id)
);

-- Kết quả chấm từng bài — cũng là bằng chứng uy tín (giống / so được, câu vàng) thay label_agreements + gold_answers.
CREATE TABLE attempt_grades (
    attempt_id         uuid PRIMARY KEY,
    project_id         uuid NOT NULL,
    labeler_id         uuid NOT NULL,
    comparable_count   integer NOT NULL,
    matched_count      integer NOT NULL,
    agreement_percent  integer NOT NULL,
    gold_total         integer NOT NULL,
    gold_correct       integer NOT NULL,
    passed             boolean NOT NULL,
    answered_count     integer NOT NULL,
    graded_at          timestamptz NOT NULL
);
CREATE INDEX ix_attempt_grades_project ON attempt_grades (project_id);
CREATE INDEX ix_attempt_grades_labeler ON attempt_grades (labeler_id);

-- Chống gian lận theo BÀI: assignment_id giữ id bài, work_seconds = số giây trung bình mỗi câu đã trả lời.
ALTER TABLE fraud_submissions ALTER COLUMN task_id DROP NOT NULL;
