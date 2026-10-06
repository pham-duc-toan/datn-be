using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Nhieu loai du lieu + tap nhan dang tools.
    ///
    /// VIET TAY (ban EF sinh xoa label_classes / allow_multiple — mat tap nhan cua
    /// ban sao, va mac dinh "" cho jsonb khong phai JSON hop le):
    ///
    ///   project_snapshots: label_task_type + label_classes + allow_multiple
    ///     → modality + label_schema {"modality":"image","tools":[{"name":"label",...}]}
    ///   tasks: modality 'image', metadata '{}', storage_key cho phep NULL (text / pair)
    ///   assignments + gold_samples: 'imageClassification' → 'image',
    ///     payload {"labelIds":[..]} → {"label":{"labelIds":[..]}}
    /// </summary>
    public partial class NhieuLoaiDuLieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- project_snapshots ----
            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "project_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "label_schema",
                table: "project_snapshots",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE project_snapshots SET modality = 'image', label_schema = jsonb_build_object(" +
                "'modality', 'image', " +
                "'tools', jsonb_build_array(jsonb_build_object(" +
                "'name', 'label', 'kind', 'classification', 'required', true, " +
                "'classes', to_jsonb(label_classes), 'allowMultiple', allow_multiple))) " +
                "WHERE label_task_type = 'imageClassification';");

            migrationBuilder.DropColumn(name: "allow_multiple", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "label_classes", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "label_task_type", table: "project_snapshots");

            // ---- tasks ----
            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "tasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "image");

            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "tasks",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "tasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_file_hoac_noi_dung",
                table: "tasks",
                sql: "(storage_key IS NULL) <> (content IS NULL)");

            // ---- nhan da nop + cau vang ----
            migrationBuilder.Sql(
                "UPDATE assignments SET task_type = 'image', payload = jsonb_build_object('label', payload) " +
                "WHERE task_type = 'imageClassification';");

            migrationBuilder.Sql(
                "UPDATE gold_samples SET expected_task_type = 'image', " +
                "expected_payload = jsonb_build_object('label', expected_payload) " +
                "WHERE expected_task_type = 'imageClassification';");

            // Mac dinh chi de lap cot cho dong cu.
            migrationBuilder.Sql(
                "ALTER TABLE project_snapshots ALTER COLUMN modality DROP DEFAULT; " +
                "ALTER TABLE tasks ALTER COLUMN modality DROP DEFAULT, ALTER COLUMN metadata DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chi quay lai dung khi CHI co du lieu anh + mot cong cu phan loai "label".
            migrationBuilder.Sql(
                "UPDATE gold_samples SET expected_task_type = 'imageClassification', " +
                "expected_payload = expected_payload -> 'label' WHERE expected_task_type = 'image';");

            migrationBuilder.Sql(
                "UPDATE assignments SET task_type = 'imageClassification', payload = payload -> 'label' " +
                "WHERE task_type = 'image';");

            migrationBuilder.DropCheckConstraint(name: "ck_tasks_file_hoac_noi_dung", table: "tasks");
            migrationBuilder.Sql("DELETE FROM tasks WHERE storage_key IS NULL;");
            migrationBuilder.Sql("ALTER TABLE tasks ALTER COLUMN storage_key SET NOT NULL;");
            migrationBuilder.DropColumn(name: "content", table: "tasks");
            migrationBuilder.DropColumn(name: "metadata", table: "tasks");
            migrationBuilder.DropColumn(name: "modality", table: "tasks");

            migrationBuilder.AddColumn<string>(
                name: "label_task_type",
                table: "project_snapshots",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "label_classes",
                table: "project_snapshots",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "allow_multiple",
                table: "project_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE project_snapshots SET label_task_type = 'imageClassification', " +
                "label_classes = ARRAY(SELECT jsonb_array_elements_text(label_schema -> 'tools' -> 0 -> 'classes')), " +
                "allow_multiple = COALESCE((label_schema -> 'tools' -> 0 ->> 'allowMultiple')::boolean, false) " +
                "WHERE label_schema IS NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE project_snapshots ALTER COLUMN label_task_type DROP DEFAULT, " +
                "ALTER COLUMN label_classes DROP DEFAULT, ALTER COLUMN allow_multiple DROP DEFAULT;");

            migrationBuilder.DropColumn(name: "label_schema", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "modality", table: "project_snapshots");
        }
    }
}
