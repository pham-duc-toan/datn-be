using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// annotations.labels text[] → dinh dang nhan chung (task_type, schema_version,
    /// payload jsonb) + GIN index tren payload.
    ///
    /// VIET TAY: ban EF sinh xoa cot labels truoc — mat het nhan da nop.
    /// Thu tu o day: them cot → chuyen du lieu → xoa cot cu → bo mac dinh → tao index.
    /// </summary>
    public partial class NhanDinhDangChung : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "task_type",
                table: "annotations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "imageClassification");

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                table: "annotations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "payload",
                table: "annotations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // {do} → {"labelIds": ["do"]}, sap xep giong ban chuan hoa cua ImageClassificationV1.
            migrationBuilder.Sql(
                "UPDATE annotations SET payload = jsonb_build_object('labelIds', " +
                "COALESCE((SELECT to_jsonb(array_agg(x ORDER BY x COLLATE \"C\")) FROM unnest(labels) AS x), '[]'::jsonb));");

            migrationBuilder.DropColumn(
                name: "labels",
                table: "annotations");

            migrationBuilder.Sql(
                "ALTER TABLE annotations " +
                "ALTER COLUMN task_type DROP DEFAULT, " +
                "ALTER COLUMN schema_version DROP DEFAULT, " +
                "ALTER COLUMN payload DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_annotations_payload",
                table: "annotations",
                column: "payload")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_annotations_payload",
                table: "annotations");

            migrationBuilder.AddColumn<List<string>>(
                name: "labels",
                table: "annotations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.Sql(
                "UPDATE annotations SET labels = ARRAY(SELECT jsonb_array_elements_text(payload -> 'labelIds'));");
            migrationBuilder.Sql("ALTER TABLE annotations ALTER COLUMN labels DROP DEFAULT;");

            migrationBuilder.DropColumn(name: "payload", table: "annotations");
            migrationBuilder.DropColumn(name: "schema_version", table: "annotations");
            migrationBuilder.DropColumn(name: "task_type", table: "annotations");
        }
    }
}
