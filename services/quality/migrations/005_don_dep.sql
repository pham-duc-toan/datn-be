-- Index cho job dọn dữ liệu quá hạn (app/cleanup.py): outbox đã gửi, dấu vết đã xử lý, lượt nộp
-- dùng xét gian lận. Không dọn thì ba bảng này lớn hơn cả dữ liệu nghiệp vụ.

CREATE INDEX IF NOT EXISTS ix_outbox_da_gui ON outbox (published_at) WHERE published_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_processed_events_thoi_diem ON processed_events (processed_at);
CREATE INDEX IF NOT EXISTS ix_fraud_sub_thoi_diem ON fraud_submissions (submitted_at);
