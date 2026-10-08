-- Bản sao setting hệ thống (admin-svc là chủ) — CÙNG cấu trúc bảng settings_replica của C#.
-- Ghi khi nhận setting.changed / settings.snapshot, chỉ khi version MỚI hơn.

CREATE TABLE settings_replica (
    key        varchar(100) PRIMARY KEY,
    value      jsonb NOT NULL,
    version    bigint NOT NULL,
    updated_at timestamptz NOT NULL
);
