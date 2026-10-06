using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Nhan text[] → dinh dang nhan chung (task_type, schema_version, payload jsonb):
    ///   - assignments.labels            → task_type / schema_version / payload (null khi chua nop)
    ///   - gold_samples.expected_labels  → expected_task_type / expected_schema_version / expected_payload
    ///   - project_snapshots             + label_task_type
    ///
    /// VIET TAY: ban EF sinh xoa cot cu truoc roi moi them cot moi — mat du lieu.
    /// Thu tu o day: them cot → chuyen du lieu → xoa cot cu → bo mac dinh → them CHECK.
    /// </summary>
    public partial class NhanDinhDangChung : Migration
    {
        // {do,vang} → {"labelIds": ["do", "vang"]}, sap xep giong ban chuan hoa cua ImageClassificationV1.
        private const string MangThanhPayload =
            "jsonb_build_object('labelIds', COALESCE((SELECT to_jsonb(array_agg(x ORDER BY x COLLATE \"C\")) FROM unnest({0}) AS x), '[]'::jsonb))";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- project_snapshots ----
            migrationBuilder.AddColumn<string>(
                name: "label_task_type",
                table: "project_snapshots",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Du an da publish truoc migration nay deu la phan loai anh — loai duy nhat tung ho tro.
            migrationBuilder.Sql("UPDATE project_snapshots SET label_task_type = 'imageClassification' WHERE is_configured;");
            migrationBuilder.Sql("ALTER TABLE project_snapshots ALTER COLUMN label_task_type DROP DEFAULT;");

            // ---- assignments ----
            migrationBuilder.AddColumn<string>(
                name: "task_type",
                table: "assignments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payload",
                table: "assignments",
                type: "jsonb",
                nullable: true);

            // Chi luot DA NOP moi co nhan; luot khac giu null.
            migrationBuilder.Sql(
                "UPDATE assignments SET task_type = 'imageClassification', schema_version = 1, payload = "
                + string.Format(System.Globalization.CultureInfo.InvariantCulture, MangThanhPayload, "labels")
                + " WHERE state = 'Submitted';");

            migrationBuilder.DropColumn(
                name: "labels",
                table: "assignments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_payload",
                table: "assignments",
                sql: "(task_type IS NULL) = (payload IS NULL) AND (schema_version IS NULL) = (payload IS NULL) AND (state = 'Submitted') = (payload IS NOT NULL)");

            // ---- gold_samples ----
            migrationBuilder.AddColumn<string>(
                name: "expected_task_type",
                table: "gold_samples",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "imageClassification");

            migrationBuilder.AddColumn<int>(
                name: "expected_schema_version",
                table: "gold_samples",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "expected_payload",
                table: "gold_samples",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.Sql(
                "UPDATE gold_samples SET expected_payload = "
                + string.Format(System.Globalization.CultureInfo.InvariantCulture, MangThanhPayload, "expected_labels")
                + ";");

            migrationBuilder.DropColumn(
                name: "expected_labels",
                table: "gold_samples");

            migrationBuilder.Sql(
                "ALTER TABLE gold_samples " +
                "ALTER COLUMN expected_task_type DROP DEFAULT, " +
                "ALTER COLUMN expected_schema_version DROP DEFAULT, " +
                "ALTER COLUMN expected_payload DROP DEFAULT;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_payload",
                table: "assignments");

            migrationBuilder.AddColumn<List<string>>(
                name: "labels",
                table: "assignments",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.Sql(
                "UPDATE assignments SET labels = ARRAY(SELECT jsonb_array_elements_text(payload -> 'labelIds')) WHERE payload IS NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN labels DROP DEFAULT;");

            migrationBuilder.AddColumn<List<string>>(
                name: "expected_labels",
                table: "gold_samples",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.Sql(
                "UPDATE gold_samples SET expected_labels = ARRAY(SELECT jsonb_array_elements_text(expected_payload -> 'labelIds'));");
            migrationBuilder.Sql("ALTER TABLE gold_samples ALTER COLUMN expected_labels DROP DEFAULT;");

            migrationBuilder.DropColumn(name: "payload", table: "assignments");
            migrationBuilder.DropColumn(name: "schema_version", table: "assignments");
            migrationBuilder.DropColumn(name: "task_type", table: "assignments");
            migrationBuilder.DropColumn(name: "expected_payload", table: "gold_samples");
            migrationBuilder.DropColumn(name: "expected_schema_version", table: "gold_samples");
            migrationBuilder.DropColumn(name: "expected_task_type", table: "gold_samples");
            migrationBuilder.DropColumn(name: "label_task_type", table: "project_snapshots");
        }
    }
}
