-- quality-svc: lược đồ khởi tạo.
-- Chạy tự động lúc service khởi động (app/db.py), mỗi file đúng một lần.

-- ---- Hạ tầng sự kiện: CÙNG cấu trúc với bảng outbox / processed_events của C# ----

CREATE TABLE outbox (
    id              uuid PRIMARY KEY,
    event_type      varchar(100) NOT NULL,
    version         integer NOT NULL,
    correlation_id  uuid NOT NULL,
    occurred_at     timestamptz NOT NULL,
    envelope_json   jsonb NOT NULL,
    published_at    timestamptz NULL,
    attempt_count   integer NOT NULL DEFAULT 0,
    last_error      varchar(2000) NULL,
    next_attempt_at timestamptz NOT NULL
);
CREATE INDEX ix_outbox_cho_gui ON outbox (next_attempt_at) WHERE published_at IS NULL;

CREATE TABLE processed_events (
    event_id     uuid NOT NULL,
    handler      varchar(200) NOT NULL,
    processed_at timestamptz NOT NULL,
    CONSTRAINT pk_processed_events PRIMARY KEY (event_id, handler)
);

-- ---- Bản sao dự án (từ project.published) ----

CREATE TABLE projects (
    project_id      uuid PRIMARY KEY,
    owner_id        uuid NOT NULL,
    modality        varchar(20) NOT NULL,
    label_schema    jsonb NOT NULL,
    redundancy      integer NOT NULL,
    max_redundancy  integer NOT NULL,
    published_at    timestamptz NOT NULL
);

-- ---- Nhãn đã nộp (từ annotation.submitted) — đầu vào của đồng thuận và DS ----

CREATE TABLE labels (
    annotation_id  uuid PRIMARY KEY,
    project_id     uuid NOT NULL,
    task_id        uuid NOT NULL,
    sample_id      uuid NOT NULL,
    labeler_id     uuid NULL,
    data           jsonb NOT NULL,
    received_at    timestamptz NOT NULL
);
CREATE INDEX ix_labels_task ON labels (task_id);
CREATE INDEX ix_labels_project ON labels (project_id);

-- ---- Mục tiêu redundancy hiện tại của task (từ task.redundancy_reached) ----

CREATE TABLE task_targets (
    task_id     uuid PRIMARY KEY,
    project_id  uuid NOT NULL,
    sample_id   uuid NOT NULL,
    target      integer NOT NULL,
    reached_at  timestamptz NOT NULL
);

-- ---- Kết quả đồng thuận, MỘT dòng cho mỗi (task, target): mỗi vòng redundancy chỉ xét một lần ----

CREATE TABLE consensus_rounds (
    task_id      uuid NOT NULL,
    target       integer NOT NULL,
    project_id   uuid NOT NULL,
    sample_id    uuid NOT NULL,
    -- agreed | disputed | notApplicable | moreRequested
    status       varchar(20) NOT NULL,
    final        jsonb NULL,
    label_count  integer NOT NULL,
    decided_at   timestamptz NOT NULL,
    CONSTRAINT pk_consensus_rounds PRIMARY KEY (task_id, target)
);
CREATE INDEX ix_consensus_project ON consensus_rounds (project_id);

-- Mỗi nhãn khớp / lệch đồng thuận ở vòng cuối của task (để tính tỉ lệ khớp của labeler).
CREATE TABLE label_agreements (
    annotation_id  uuid PRIMARY KEY,
    project_id     uuid NOT NULL,
    task_id        uuid NOT NULL,
    labeler_id     uuid NOT NULL,
    agrees         boolean NOT NULL,
    decided_at     timestamptz NOT NULL
);
CREATE INDEX ix_label_agreements_labeler ON label_agreements (labeler_id);

-- ---- Câu vàng kiểm tra (từ gold.answered) ----

CREATE TABLE gold_answers (
    assignment_id  uuid PRIMARY KEY,
    project_id     uuid NOT NULL,
    sample_id      uuid NOT NULL,
    labeler_id     uuid NOT NULL,
    correct        boolean NOT NULL,
    answered_at    timestamptz NOT NULL
);
CREATE INDEX ix_gold_answers_labeler ON gold_answers (labeler_id);

-- ---- Độ tin cậy Dawid-Skene theo (labeler, dự án, công cụ) — chạy theo lô ----

CREATE TABLE ds_skills (
    labeler_id   uuid NOT NULL,
    project_id   uuid NOT NULL,
    tool         varchar(40) NOT NULL,
    skill        double precision NOT NULL,
    label_count  integer NOT NULL,
    computed_at  timestamptz NOT NULL,
    CONSTRAINT pk_ds_skills PRIMARY KEY (labeler_id, project_id, tool)
);

-- Chỉ số đồng thuận cả dự án theo công cụ (Krippendorff alpha) — lần chạy gần nhất.
CREATE TABLE project_metrics (
    project_id   uuid NOT NULL,
    tool         varchar(40) NOT NULL,
    alpha        double precision NULL,
    item_count   integer NOT NULL,
    label_count  integer NOT NULL,
    computed_at  timestamptz NOT NULL,
    CONSTRAINT pk_project_metrics PRIMARY KEY (project_id, tool)
);

-- ---- Điểm uy tín đã công bố (reputation.changed) ----

CREATE TABLE reputations (
    labeler_id    uuid PRIMARY KEY,
    reputation    integer NOT NULL,
    gold_total    integer NOT NULL,
    gold_correct  integer NOT NULL,
    agree_total   integer NOT NULL,
    agree_count   integer NOT NULL,
    ds_skill      double precision NULL,
    updated_at    timestamptz NOT NULL
);
