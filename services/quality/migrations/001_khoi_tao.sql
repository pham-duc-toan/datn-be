-- quality-svc: lược đồ khởi tạo.
-- Chạy tự động lúc service khởi động (app/db.py), mỗi file đúng một lần.
--
-- KHÔNG CÓ BẢN SAO DỮ LIỆU CỦA PROJECT-SVC (kiến trúc 3.22). Đề bài, bài nộp và nhãn là dữ liệu của
-- project-svc; quality đọc thẳng qua gRPC (contracts/grpc/project/v1/quality_data.proto) lúc chấm, lúc chạy
-- lô Dawid-Skene, lúc xét gian lận. Ở đây chỉ còn KẾT QUẢ quality tự tính ra (điểm từng bài, đa số từng câu,
-- chỉ số, uy tín, hồ sơ gian lận) và trạng thái công việc của chính nó (hàng chờ, mốc chấm, mốc lưu trữ).
--
-- (Gộp 13 migration cũ thành một: dữ liệu chỉ là seed — dựng lại volume, không cần chuyển dữ liệu.)

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
-- Job dọn outbox đã gửi (app/cleanup.py).
CREATE INDEX ix_outbox_da_gui ON outbox (published_at) WHERE published_at IS NOT NULL;

CREATE TABLE processed_events (
    event_id     uuid NOT NULL,
    handler      varchar(200) NOT NULL,
    processed_at timestamptz NOT NULL,
    CONSTRAINT pk_processed_events PRIMARY KEY (event_id, handler)
);
CREATE INDEX ix_processed_events_thoi_diem ON processed_events (processed_at);

-- Bản sao setting hệ thống (admin-svc là chủ) — CÙNG cấu trúc bảng settings_replica của C#.
-- Ghi khi nhận setting.changed / settings.snapshot, chỉ khi version MỚI hơn.
CREATE TABLE settings_replica (
    key        varchar(100) PRIMARY KEY,
    value      jsonb NOT NULL,
    version    bigint NOT NULL,
    updated_at timestamptz NOT NULL
);

-- ---- Hàng chờ việc (không phải bản sao: dòng bị xoá khi xong việc) ----

-- attempt.submitted chỉ là TÍN HIỆU (v2, không mang câu trả lời). Consumer ghi một dòng ở đây rồi trả lời
-- ngay — không gọi gRPC trong consumer: project-svc tạm sập thì message không dồn vào DLQ, worker thử lại.
--   fraud_checked_at  — đã xét gian lận (app/viec_cho.py);
--   freetext_done_at  — đã nhúng câu trả lời mở qua ml-svc, hoặc dự án không có câu mở (app/phan_tich_mo.py).
-- Xong cả hai thì xoá dòng.
CREATE TABLE pending_attempts (
    attempt_id        uuid PRIMARY KEY,
    project_id        uuid NOT NULL,
    queued_at         timestamptz NOT NULL,
    fraud_checked_at  timestamptz NULL,
    freetext_done_at  timestamptz NULL
);
CREATE INDEX ix_pending_attempts_fraud ON pending_attempts (queued_at) WHERE fraud_checked_at IS NULL;
CREATE INDEX ix_pending_attempts_freetext ON pending_attempts (queued_at) WHERE freetext_done_at IS NULL;

-- ---- Chấm cả dự án (project.collection_finished, kiến trúc 3.18) ----

-- Một dòng mỗi dự án đã đóng thu bài. graded_at NULL = chưa chấm xong (project-svc tạm không gọi được, hoặc
-- chờ ml-svc phân tích câu trả lời mở) → worker chấm lại. correlation_id nối project.graded vào chuỗi gốc.
CREATE TABLE project_gradings (
    project_id       uuid PRIMARY KEY,
    requested_at     timestamptz NOT NULL,
    correlation_id   uuid NOT NULL,
    graded_at        timestamptz NULL,
    majority_used    boolean NULL,
    -- Câu trả lời mở (3.19): pending (chờ ml-svc) → done. method: semantic (so nghĩa) | lexical (so chữ).
    freetext_state   varchar(20) NULL,
    freetext_method  varchar(20) NULL,
    -- Đã chạy Dawid-Skene + chỉ số độ tin cậy (alpha / kappa / STAPLE) cho dự án (app/ds_batch.py): MỘT lần,
    -- SAU khi chấm — lúc đó đủ bài của mọi labeler. Trong lúc thu bài không tính (dữ liệu dở dang làm độ tin cậy
    -- dao động rồi lan sang uy tín). NULL = chưa chạy (hoặc admin yêu cầu tính lại).
    ds_at            timestamptz NULL,
    -- Đã xếp labeler của dự án vào reputation_dirty: SAU khi project-svc ghi xong project.graded (gRPC thấy bài
    -- graded) VÀ đã có ds_at — uy tín tính một lần, đủ cả bằng chứng chấm lẫn độ tin cậy DS của dự án.
    reputation_at    timestamptz NULL
);
CREATE INDEX ix_project_gradings_cho ON project_gradings (requested_at) WHERE graded_at IS NULL;
CREATE INDEX ix_project_gradings_ds ON project_gradings (graded_at) WHERE graded_at IS NOT NULL AND ds_at IS NULL;
CREATE INDEX ix_project_gradings_uy_tin ON project_gradings (graded_at)
    WHERE graded_at IS NOT NULL AND ds_at IS NOT NULL AND reputation_at IS NULL;

-- Labeler cần tính lại uy tín (dự án vừa chấm đã áp dụng, hoặc lô DS đổi độ tin cậy). Worker (app/viec_cho.py)
-- đọc bằng chứng cộng dồn qua gRPC (GetLabelerEvidence — bài đã chấm nằm trên project.attempts, quality không
-- giữ bản sao) rồi xoá dòng.
CREATE TABLE reputation_dirty (
    labeler_id      uuid PRIMARY KEY,
    dirty_at        timestamptz NOT NULL,
    correlation_id  uuid NULL
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

-- Câu trả lời mở (freeText, 3.19): vector nhúng mỗi (bài, mẫu, công cụ) do ml-svc tính (chuẩn hoá L2).
-- vector NULL = công cụ bỏ trống (vẫn ghi dòng để biết câu đó đã xét).
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

-- ---- Dawid-Skene + chỉ số độ tin cậy: chạy MỘT lần sau khi chấm (project_gradings.ds_at) ----

-- Độ tin cậy Dawid-Skene theo (labeler, dự án, công cụ). Uy tín cộng dồn SUM(skill × label_count) qua mọi dự án
-- của labeler (khoá chính mở đầu bằng labeler_id).
CREATE TABLE ds_skills (
    labeler_id   uuid NOT NULL,
    project_id   uuid NOT NULL,
    tool         varchar(40) NOT NULL,
    skill        double precision NOT NULL,
    label_count  integer NOT NULL,
    computed_at  timestamptz NOT NULL,
    CONSTRAINT pk_ds_skills PRIMARY KEY (labeler_id, project_id, tool)
);

-- Chỉ số đồng thuận cả dự án theo công cụ (tính sau khi chấm). metric = hàm khoảng cách của alpha
-- (nominal / interval / cer / kendall / set_f1); kappa = Fleiss' kappa cho công cụ nominal.
CREATE TABLE project_metrics (
    project_id   uuid NOT NULL,
    tool         varchar(40) NOT NULL,
    alpha        double precision NULL,
    kappa        double precision NULL,
    metric       varchar(20) NULL,
    item_count   integer NOT NULL,
    label_count  integer NOT NULL,
    computed_at  timestamptz NOT NULL,
    CONSTRAINT pk_project_metrics PRIMARY KEY (project_id, tool)
);

-- STAPLE (mặt nạ): độ nhạy / độ đặc hiệu từng labeler — chỉ để chủ dự án xem, KHÔNG vào uy tín.
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

-- ---- Điểm uy tín đã công bố (reputation.changed) — quality là CHỦ của điểm này ----

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

-- ---- Chống gian lận (FM-07): hồ sơ nghi vấn cho admin ----
-- Dấu vết từng bài nộp (thời gian, IP, thiết bị, đáp án) KHÔNG chép về đây: worker hỏi project-svc qua gRPC.

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
-- Mỗi khoá chỉ MỘT hồ sơ đang mở (hai worker cùng phát hiện → một bên cập nhật).
CREATE UNIQUE INDEX ux_fraud_cases_mo ON fraud_cases (case_key) WHERE status = 'open';
CREATE INDEX ix_fraud_cases_hang_doi ON fraud_cases (status, created_at);
CREATE INDEX ix_fraud_cases_key ON fraud_cases (case_key, resolved_at);

-- ---- Lưu trữ dự án ĐÃ ĐÓNG (setting project.closed_data_retention) ----

-- Đồng hồ lưu trữ của quality (không phải bản sao trạng thái dự án):
--   closed_at   — nhận project.completed / project.cancelled;
--   archived_at — đã chụp báo cáo vào project_reports (từ đây API đọc bản chụp, việc đến trễ bị bỏ);
--   purged_at   — đã xoá xong kết quả trung gian (đa số từng câu, vector câu mở).
CREATE TABLE project_archives (
    project_id   uuid PRIMARY KEY,
    closed_at    timestamptz NOT NULL,
    archived_at  timestamptz NULL,
    purged_at    timestamptz NULL
);
CREATE INDEX ix_project_archives_cho_don ON project_archives (closed_at) WHERE purged_at IS NULL;

-- Bản chụp đúng hình dạng hai API /quality/projects/{id}/summary và /labelers trả ra.
CREATE TABLE project_reports (
    project_id  uuid PRIMARY KEY,
    summary     jsonb NOT NULL,
    labelers    jsonb NOT NULL,
    created_at  timestamptz NOT NULL
);
