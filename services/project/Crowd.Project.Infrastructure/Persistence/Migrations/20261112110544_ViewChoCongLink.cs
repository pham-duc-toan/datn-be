using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// View cho gate-svc doc thang (kien truc 3.32) thay cho ban sao gate_projects / gate_samples / gate_gold_items.
    /// Luat "du an nao len cong, mau nao, cau vang nao" nam O DAY (phia project) — gate chi SELECT, khong biet bang
    /// that trong ra sao. Role gate_reader chi SELECT duoc ba view nay; mat khau dat luc khoi dong (ReaderRoles).
    /// </summary>
    public partial class ViewChoCongLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("gate_reader"));

            // Du an da publish bat cong link, chua dong. Dang cham (Grading) van phuc vu nhu truoc (gate cu chi
            // ngung khi tam dung / huy / hoan thanh). Phi cong = don gia x % phi chot luc publish (TinhPhiVnd).
            migrationBuilder.Sql(@"
CREATE VIEW v_gate_projects AS
SELECT p.id AS project_id,
       p.owner_id,
       p.modality,
       p.label_schema,
       p.gate_unit_price_vnd AS unit_price_vnd,
       (p.gate_unit_price_vnd * p.platform_fee_percent) / 100 AS platform_fee_vnd,
       CASE WHEN p.status = 'Paused' THEN 'Paused' ELSE 'Running' END AS status
FROM projects p
WHERE p.allow_link_gateway
  AND p.published_at IS NOT NULL
  AND p.status IN ('Running', 'Paused', 'Grading');");

            // Mau da nap xong (ready) cua cac du an tren — gom ca mau lam cau vang (gate tu tach cau that).
            migrationBuilder.Sql(@"
CREATE VIEW v_gate_samples AS
SELECT s.id AS sample_id,
       s.project_id,
       s.modality,
       s.storage_key,
       s.content,
       s.metadata
FROM samples s
JOIN v_gate_projects g ON g.project_id = s.project_id
WHERE s.ingest_state = 'ready';");

            // Chi cau vang KIEM TRA: cau cua bai test dau vao khong len trang cong khai. Dap an KHONG BAO GIO ra client.
            migrationBuilder.Sql(@"
CREATE VIEW v_gate_gold AS
SELECT gi.sample_id,
       gi.project_id,
       gi.expected_task_type AS task_type,
       gi.expected_schema_version AS schema_version,
       gi.expected_payload
FROM gold_items gi
JOIN v_gate_projects g ON g.project_id = gi.project_id
WHERE gi.purpose = 'QualityCheck';");

            migrationBuilder.Sql("GRANT SELECT ON v_gate_projects, v_gate_samples, v_gate_gold TO gate_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gate_gold;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gate_samples;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gate_projects;");
        }
    }
}
