using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Nhieu loai du lieu + tap nhan dang tools.
    ///
    /// VIET TAY (ban EF sinh xoa label_classes / allow_multiple — mat tap nhan, va
    /// mac dinh "" cho jsonb khong phai JSON hop le):
    ///
    ///   project_terms: label_classes + allow_multiple
    ///     → modality 'image' + label_schema {"modality":"image","tools":[{"name":"label",...}]}
    ///   annotations: sample_metadata '{}', sample_content NULL, storage_key cho phep NULL;
    ///     'imageClassification' → 'image', payload {"labelIds":[..]} → {"label":{"labelIds":[..]}}
    /// </summary>
    public partial class NhieuLoaiDuLieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- project_terms ----
            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "project_terms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "image");

            migrationBuilder.AddColumn<string>(
                name: "label_schema",
                table: "project_terms",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.Sql(
                "UPDATE project_terms SET label_schema = jsonb_build_object(" +
                "'modality', 'image', " +
                "'tools', jsonb_build_array(jsonb_build_object(" +
                "'name', 'label', 'kind', 'classification', 'required', true, " +
                "'classes', to_jsonb(label_classes), 'allowMultiple', allow_multiple)));");

            migrationBuilder.DropColumn(name: "allow_multiple", table: "project_terms");
            migrationBuilder.DropColumn(name: "label_classes", table: "project_terms");

            // ---- annotations ----
            migrationBuilder.AddColumn<string>(
                name: "sample_metadata",
                table: "annotations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "sample_content",
                table: "annotations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "annotations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.Sql(
                "UPDATE annotations SET task_type = 'image', payload = jsonb_build_object('label', payload) " +
                "WHERE task_type = 'imageClassification';");

            // Mac dinh chi de lap cot cho dong cu.
            migrationBuilder.Sql(
                "ALTER TABLE project_terms ALTER COLUMN modality DROP DEFAULT, ALTER COLUMN label_schema DROP DEFAULT; " +
                "ALTER TABLE annotations ALTER COLUMN sample_metadata DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chi quay lai dung khi CHI co du lieu anh + mot cong cu phan loai "label".
            migrationBuilder.Sql(
                "UPDATE annotations SET task_type = 'imageClassification', payload = payload -> 'label' " +
                "WHERE task_type = 'image';");

            migrationBuilder.Sql("DELETE FROM annotations WHERE storage_key IS NULL;");
            migrationBuilder.Sql("ALTER TABLE annotations ALTER COLUMN storage_key SET NOT NULL;");
            migrationBuilder.DropColumn(name: "sample_content", table: "annotations");
            migrationBuilder.DropColumn(name: "sample_metadata", table: "annotations");

            migrationBuilder.AddColumn<List<string>>(
                name: "label_classes",
                table: "project_terms",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "allow_multiple",
                table: "project_terms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE project_terms SET " +
                "label_classes = ARRAY(SELECT jsonb_array_elements_text(label_schema -> 'tools' -> 0 -> 'classes')), " +
                "allow_multiple = COALESCE((label_schema -> 'tools' -> 0 ->> 'allowMultiple')::boolean, false);");

            migrationBuilder.Sql(
                "ALTER TABLE project_terms ALTER COLUMN label_classes DROP DEFAULT, ALTER COLUMN allow_multiple DROP DEFAULT;");

            migrationBuilder.DropColumn(name: "label_schema", table: "project_terms");
            migrationBuilder.DropColumn(name: "modality", table: "project_terms");
        }
    }
}
