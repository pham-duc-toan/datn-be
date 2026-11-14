using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bo cau hoi chung cua tap nhan (label_schema.question) — moi cong cu co cau hoi rieng (tools[].question, kien
    /// truc 3.35). Cot question cua view gate chi con cau rieng cua mau (ngu canh tuy chon); cau hoi cua cong cu di
    /// cung tap nhan trong v_gate_projects. Giu nguyen ten / thu tu / KIEU cot (COALESCE cu ra varchar khong gioi han, cot
    /// samples.question la varchar(1000) — ep ::character varying) nen CREATE OR REPLACE duoc, grant con nguyen.
    /// </summary>
    public partial class ViewGateBoCauHoiChung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR REPLACE VIEW v_gate_samples AS
SELECT s.id AS sample_id,
       s.project_id,
       s.modality,
       s.storage_key,
       s.content,
       s.metadata,
       s.question::character varying AS question
FROM samples s
JOIN v_gate_projects g ON g.project_id = s.project_id
WHERE s.ingest_state = 'ready';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR REPLACE VIEW v_gate_samples AS
SELECT s.id AS sample_id,
       s.project_id,
       s.modality,
       s.storage_key,
       s.content,
       s.metadata,
       COALESCE(s.question, g.label_schema ->> 'question') AS question
FROM samples s
JOIN v_gate_projects g ON g.project_id = s.project_id
WHERE s.ingest_state = 'ready';");
        }
    }
}
