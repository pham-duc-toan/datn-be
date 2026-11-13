using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Cau hoi thay huong dan: bo projects.guideline (huong dan Markdown + vi du) — labeler co cau hoi chung
    /// (label_schema.question) / cau hoi rieng tung mau (samples.question) va man luyen tap cong cu. Chong trung mau
    /// tinh theo (noi dung, cau hoi): cung anh hoi hai cau khac nhau la hai mau. View cua gate them cot question (cau
    /// hien thi — cau rieng, khong co thi cau chung), cot moi o CUOI nen CREATE OR REPLACE duoc.
    /// </summary>
    public partial class CauHoiThayHuongDan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_samples_project_sha256",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "guideline",
                table: "projects");

            migrationBuilder.AddColumn<string>(
                name: "question",
                table: "samples",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_samples_project_sha256_question",
                table: "samples",
                columns: new[] { "project_id", "sha256", "question" },
                unique: true,
                filter: "sha256 IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP VIEW v_gate_samples;
CREATE VIEW v_gate_samples AS
SELECT s.id AS sample_id, s.project_id, s.modality, s.storage_key, s.content, s.metadata
FROM samples s
JOIN v_gate_projects g ON g.project_id = s.project_id
WHERE s.ingest_state = 'ready';
GRANT SELECT ON v_gate_samples TO gate_reader;");

            migrationBuilder.DropIndex(
                name: "ux_samples_project_sha256_question",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "question",
                table: "samples");

            migrationBuilder.AddColumn<string>(
                name: "guideline",
                table: "projects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_samples_project_sha256",
                table: "samples",
                columns: new[] { "project_id", "sha256" },
                unique: true);
        }
    }
}
