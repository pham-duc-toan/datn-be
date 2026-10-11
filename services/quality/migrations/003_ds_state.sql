-- Lô Dawid-Skene chỉ tính lại dự án CÓ NHÃN MỚI. Trước đây mỗi lô tính lại MỌI dự án (~1 giây / dự án
-- vì EM của crowdkit): 100 dự án đã xong → lô > 100 giây, nút "chạy ngay" của admin quá timeout gateway.
-- labels chỉ INSERT nên "số nhãn khác lần trước" ⇔ có nhãn mới — kể cả nhãn đến trễ (không so thời gian:
-- received_at là giờ phát sự kiện, có thể cũ hơn lần tính trước).
CREATE TABLE ds_state (
    project_id  uuid PRIMARY KEY,
    label_count integer NOT NULL,     -- số nhãn (có labeler) đã dùng ở lần tính gần nhất
    computed_at timestamptz NOT NULL
);
