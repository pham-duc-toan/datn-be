-- ml-svc (kiến trúc 3.19): bộ đệm vector nhúng câu.
--
-- KHÔNG lưu văn bản gốc: khoá là SHA-256(tên mô hình + văn bản đã chuẩn hoá). Câu trả lời của labeler vẫn
-- chỉ nằm ở quality-svc (và bị dọn theo hạn lưu trữ dự án); ở đây chỉ còn vector, không đọc ngược ra chữ được.
-- Cùng một câu (nhiều người chép y hệt, gửi lại sau lỗi mạng) chỉ nhúng một lần.
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE embedding_cache (
    text_hash     bytea PRIMARY KEY,
    model         varchar(200) NOT NULL,
    dim           integer NOT NULL,
    embedding     vector NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    last_used_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_embedding_cache_last_used ON embedding_cache (last_used_at);
