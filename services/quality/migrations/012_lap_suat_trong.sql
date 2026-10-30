-- Lap suat trong (10/2026): so bai DAT it hon so suat thi suat con lai trao cho bai giong da so nhieu nhat
-- (cung % thi nop som hon), dung het cau vang va giong da so >= muc nay. NULL = khong lap (du an cu).
ALTER TABLE projects ADD COLUMN fill_slots_min_percent integer NULL;
