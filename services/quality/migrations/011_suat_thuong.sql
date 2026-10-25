-- So suat = so nguoi DUOC THUONG (10/2026): n bai DAT nop som nhat duoc thuong, bai dat sau do het suat.
-- Du lieu cu: moi bai dat deu duoc thuong (so suat la tran nguoi lam).
ALTER TABLE attempt_grades ADD COLUMN rewarded boolean NOT NULL DEFAULT false;
UPDATE attempt_grades SET rewarded = passed;
