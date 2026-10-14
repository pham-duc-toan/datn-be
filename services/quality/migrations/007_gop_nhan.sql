-- Gộp nhãn cho mọi loại công cụ (kiến trúc mục 3.15).
--
-- Điểm mốc và đường gấp khúc so độ lệch theo ĐƯỜNG CHÉO ẢNH, nên đồng thuận cần kích thước ảnh của
-- mẫu. annotation.submitted mang thêm sample_metadata (vài chục byte); lưu theo từng nhãn để tính
-- đồng thuận không phải gọi service khác. Nhãn nhận trước bản này để NULL: gộp vẫn chạy, các công cụ
-- đó dùng hình bao của chính các điểm làm thước đo (gop.duong_cheo).
ALTER TABLE labels ADD COLUMN sample_metadata jsonb NULL;
