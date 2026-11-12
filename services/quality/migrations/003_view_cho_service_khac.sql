-- Kien truc 3.32: identity-svc doc THANG diem uy tin qua view nay (role identity_reader chi SELECT duoc view nay),
-- thay cho cot ban sao identity.labeler_stats.reputation cap nhat bang reputation.changed. Mat khau role dat luc
-- khoi dong (db.dam_bao_role_doc, cau hinh QUALITY_READ_ACCESS_READERS).
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_reader') THEN
        CREATE ROLE identity_reader NOLOGIN;
    END IF;
END $$;

CREATE VIEW v_reputations AS
SELECT r.labeler_id, r.reputation
FROM reputations r;

GRANT SELECT ON v_reputations TO identity_reader;
