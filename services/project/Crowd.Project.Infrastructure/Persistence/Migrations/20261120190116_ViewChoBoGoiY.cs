using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bo goi y reco-svc (kien truc 3.39) doc THANG view cua project bang role reco_reader (3.32) — khong ban sao:
    ///   v_reco_projects        — du an da publish + dac trung cham diem (so bai nop / dang lam / da bat dau, trung vi
    ///                            giay moi cau) + co accepting (dang nhan bai). So nguoi lam CHI dung trong server reco,
    ///                            khong bao gio tra cho labeler.
    ///   v_reco_exclusions      — (labeler, du an) can loai / duoc thay: da nop, bi chan, thanh vien dang hoat dong.
    ///                            reco-svc chi doc khi thieu cache Redis cua MOT nguoi (sau do cap nhat bang event).
    ///   v_reco_owner_blocks    — danh sach chan cap doanh nghiep.
    ///   v_reco_preview_samples — MOT mau xem truoc moi du an (P1) he thong tu lay: mau that da nap xong, khong phai cau
    ///                            vang, tao som nhat; chi du an dang chay, doanh nghiep bat xem truoc va duoc phep.
    /// Them cot projects.preview_enabled (mac dinh bat).
    /// </summary>
    public partial class ViewChoBoGoiY : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "preview_enabled",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("reco_reader"));

            // Du an da publish (ke ca da dong — consumer cham bai can the cua du an da dong). Bo nap 30 s cua reco chi lay
            // accepting = true. Trung vi giay moi cau tinh tren bai DA NOP (null khi chua co bai nao). tools = cong cu cua tap
            // nhan (ten, loai, cau hoi, lop) — the cho du an hien toan bo cau hoi + cong cu.
            migrationBuilder.Sql(@"
CREATE VIEW v_reco_projects AS
SELECT p.id AS project_id,
       p.owner_id,
       p.name,
       left(p.description, 300) AS description,
       p.modality,
       p.domains,
       p.languages,
       ARRAY(SELECT DISTINCT t ->> 'kind'
             FROM jsonb_array_elements(COALESCE(p.label_schema -> 'tools', '[]'::jsonb)) t
             WHERE t ->> 'kind' IS NOT NULL) AS tool_kinds,
       p.reward_vnd,
       p.slots,
       p.question_count,
       p.deadline,
       p.published_at,
       p.visibility,
       p.min_level,
       p.min_reputation,
       p.require_entrance_test,
       p.require_verified_identity,
       p.sensitive_content,
       p.nda_required,
       (p.status = 'Running' AND p.allow_professional AND c.project_id IS NOT NULL AND c.finish_reason IS NULL
        AND (p.deadline IS NULL OR p.deadline > now())) AS accepting,
       COALESCE(st.submitted, 0) AS submitted_count,
       COALESCE(st.in_progress, 0) AS in_progress_count,
       COALESCE(st.started, 0) AS started_count,
       st.median_seconds_per_question,
       COALESCE(p.label_schema -> 'tools', '[]'::jsonb) AS tools
FROM projects p
LEFT JOIN project_collections c ON c.project_id = p.id
LEFT JOIN LATERAL (
    SELECT count(*) FILTER (WHERE a.state = 'Submitted') AS submitted,
           count(*) FILTER (WHERE a.state = 'InProgress' AND a.expires_at > now()) AS in_progress,
           count(*) AS started,
           percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (a.submitted_at - a.started_at)) / GREATEST(p.question_count, 1))
               FILTER (WHERE a.state = 'Submitted' AND a.submitted_at IS NOT NULL) AS median_seconds_per_question
    FROM attempts a
    WHERE a.project_id = p.id) st ON true
WHERE p.published_at IS NOT NULL
  AND p.status IN ('Running', 'Paused', 'Grading', 'Completed');");

            migrationBuilder.Sql(@"
CREATE VIEW v_reco_exclusions AS
SELECT a.labeler_id AS user_id, a.project_id, 'submitted'::text AS kind FROM attempts a WHERE a.state = 'Submitted'
UNION ALL
SELECT m.user_id, m.project_id, 'blocked'::text FROM project_members m WHERE m.state = 'Blocked'
UNION ALL
SELECT m.user_id, m.project_id, 'member'::text FROM project_members m WHERE m.state = 'Active';");

            migrationBuilder.Sql(@"
CREATE VIEW v_reco_owner_blocks AS
SELECT e.labeler_id AS user_id, e.owner_id FROM owner_labeler_lists e WHERE e.kind = 'Blocked';");

            // Luat an toan nam O DAY: khong NDA, khong nhay cam, khong rieng tu, khong phai cau vang, mau da nap xong.
            // Moi du an MOT mau (DISTINCT ON): mau tao som nhat — on dinh giua cac lan nap.
            migrationBuilder.Sql(@"
CREATE VIEW v_reco_preview_samples AS
SELECT DISTINCT ON (s.project_id)
       s.project_id,
       s.id AS sample_id,
       s.modality,
       s.storage_key,
       s.content,
       s.metadata,
       s.question
FROM projects p
JOIN samples s ON s.project_id = p.id
WHERE p.status = 'Running'
  AND p.preview_enabled
  AND NOT p.nda_required
  AND NOT p.sensitive_content
  AND p.visibility <> 'Private'
  AND s.ingest_state = 'ready'
  AND s.excluded_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM gold_items g WHERE g.project_id = s.project_id AND g.sample_id = s.id)
ORDER BY s.project_id, s.created_at, s.id;");

            migrationBuilder.Sql("GRANT SELECT ON v_reco_projects, v_reco_exclusions, v_reco_owner_blocks, v_reco_preview_samples TO reco_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_reco_preview_samples;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_reco_owner_blocks;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_reco_exclusions;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_reco_projects;");

            migrationBuilder.DropColumn(
                name: "preview_enabled",
                table: "projects");
        }
    }
}
