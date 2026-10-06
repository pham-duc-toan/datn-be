using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Dap an cau hoi vang: text[] → dinh dang nhan chung (task_type, schema_version, jsonb).
    ///
    /// VIET TAY, khong dung ban EF sinh: EF sinh "xoa cot cu, them cot moi" —
    /// mat het dap an, va mac dinh "" cho jsonb con khong phai JSON hop le.
    /// O day: them cot (co mac dinh hop le) → CHUYEN du lieu → xoa cot cu → bo mac dinh.
    /// </summary>
    public partial class NhanDinhDangChung : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expected_task_type",
                table: "gold_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "imageClassification");

            migrationBuilder.AddColumn<int>(
                name: "expected_schema_version",
                table: "gold_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "expected_payload",
                table: "gold_items",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // {do} → {"labelIds": ["do"]}, sap xep giong ban chuan hoa cua ImageClassificationV1.
            migrationBuilder.Sql(
                "UPDATE gold_items SET expected_payload = jsonb_build_object('labelIds', " +
                "COALESCE((SELECT to_jsonb(array_agg(x ORDER BY x COLLATE \"C\")) FROM unnest(expected_labels) AS x), '[]'::jsonb));");

            migrationBuilder.DropColumn(
                name: "expected_labels",
                table: "gold_items");

            // Mac dinh chi de lap cot cho dong cu. Dong moi bat buoc code ghi tuong minh.
            migrationBuilder.Sql(
                "ALTER TABLE gold_items " +
                "ALTER COLUMN expected_task_type DROP DEFAULT, " +
                "ALTER COLUMN expected_schema_version DROP DEFAULT, " +
                "ALTER COLUMN expected_payload DROP DEFAULT;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "expected_labels",
                table: "gold_items",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.Sql(
                "UPDATE gold_items SET expected_labels = " +
                "ARRAY(SELECT jsonb_array_elements_text(expected_payload -> 'labelIds'));");

            migrationBuilder.Sql("ALTER TABLE gold_items ALTER COLUMN expected_labels DROP DEFAULT;");

            migrationBuilder.DropColumn(
                name: "expected_payload",
                table: "gold_items");

            migrationBuilder.DropColumn(
                name: "expected_schema_version",
                table: "gold_items");

            migrationBuilder.DropColumn(
                name: "expected_task_type",
                table: "gold_items");
        }
    }
}
