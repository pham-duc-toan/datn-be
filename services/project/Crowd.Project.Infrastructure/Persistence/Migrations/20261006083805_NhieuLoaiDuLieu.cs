using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Nhieu loai du lieu (image / text / audio / video / pair) + tap nhan dang tools.
    ///
    /// VIET TAY, khong dung ban EF sinh (ban EF xoa task_type va them cot voi mac
    /// dinh "" — "" khong phai JSON hop le cho jsonb, va mat thong tin loai nhan).
    ///
    ///   projects.task_type 'ImageClassification' → projects.modality 'image'
    ///   projects.label_schema {"kind":"classification","classes":[..],"allowMultiple":b}
    ///     → {"modality":"image","tools":[{"name":"label","kind":"classification",...}]}
    ///   gold_items: 'imageClassification' → 'image', payload {"labelIds":[..]} → {"label":{"labelIds":[..]}}
    ///   samples: modality 'image', metadata '{}' (anh cu chua do kich thuoc), cho phep
    ///     storage_key / size_bytes / content_type NULL (text / pair khong co file)
    ///   datasets: lo cu da nap xong ngay trong request → status 'Ready'.
    /// </summary>
    public partial class NhieuLoaiDuLieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- projects ----
            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "projects",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "image");

            migrationBuilder.Sql(
                "UPDATE projects SET label_schema = jsonb_build_object(" +
                "'modality', 'image', " +
                "'tools', jsonb_build_array(jsonb_build_object(" +
                "'name', 'label', 'kind', 'classification', 'required', true, " +
                "'classes', COALESCE(label_schema -> 'classes', '[]'::jsonb), " +
                "'allowMultiple', COALESCE(label_schema -> 'allowMultiple', 'false'::jsonb)))) " +
                "WHERE label_schema IS NOT NULL AND label_schema ? 'kind';");

            migrationBuilder.DropColumn(
                name: "task_type",
                table: "projects");

            // ---- gold_items ----
            migrationBuilder.Sql(
                "UPDATE gold_items SET expected_task_type = 'image', " +
                "expected_payload = jsonb_build_object('label', expected_payload) " +
                "WHERE expected_task_type = 'imageClassification';");

            // ---- samples ----
            migrationBuilder.AddColumn<string>(
                name: "modality",
                table: "samples",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "image");

            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "samples",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "samples",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "samples",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<long>(
                name: "size_bytes",
                table: "samples",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "content_type",
                table: "samples",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddCheckConstraint(
                name: "ck_samples_file_hoac_noi_dung",
                table: "samples",
                sql: "(storage_key IS NULL) <> (content IS NULL)");

            // ---- datasets ----
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "datasets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Ready");

            migrationBuilder.AddColumn<string>(
                name: "error_summary",
                table: "datasets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finished_at",
                table: "datasets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manifest",
                table: "datasets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manifest_key",
                table: "datasets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql("UPDATE datasets SET finished_at = created_at;");

            migrationBuilder.CreateIndex(
                name: "ix_datasets_dang_xu_ly",
                table: "datasets",
                column: "status",
                filter: "status IN ('Pending','Ingesting')");

            // Mac dinh chi de lap cot cho dong cu. Dong moi bat buoc code ghi tuong minh.
            migrationBuilder.Sql(
                "ALTER TABLE projects ALTER COLUMN modality DROP DEFAULT; " +
                "ALTER TABLE samples ALTER COLUMN modality DROP DEFAULT, ALTER COLUMN metadata DROP DEFAULT; " +
                "ALTER TABLE datasets ALTER COLUMN status DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chi quay lai dung khi CHI co du lieu anh + mot cong cu phan loai "label".
            migrationBuilder.DropCheckConstraint(
                name: "ck_samples_file_hoac_noi_dung",
                table: "samples");

            migrationBuilder.DropIndex(
                name: "ix_datasets_dang_xu_ly",
                table: "datasets");

            migrationBuilder.Sql("DELETE FROM samples WHERE storage_key IS NULL;");

            migrationBuilder.DropColumn(name: "content", table: "samples");
            migrationBuilder.DropColumn(name: "metadata", table: "samples");
            migrationBuilder.DropColumn(name: "modality", table: "samples");
            migrationBuilder.DropColumn(name: "error_summary", table: "datasets");
            migrationBuilder.DropColumn(name: "finished_at", table: "datasets");
            migrationBuilder.DropColumn(name: "manifest", table: "datasets");
            migrationBuilder.DropColumn(name: "manifest_key", table: "datasets");
            migrationBuilder.DropColumn(name: "status", table: "datasets");

            migrationBuilder.Sql(
                "ALTER TABLE samples " +
                "ALTER COLUMN storage_key SET NOT NULL, " +
                "ALTER COLUMN size_bytes SET NOT NULL, " +
                "ALTER COLUMN content_type TYPE character varying(50), " +
                "ALTER COLUMN content_type SET NOT NULL;");

            migrationBuilder.Sql(
                "UPDATE gold_items SET expected_task_type = 'imageClassification', " +
                "expected_payload = expected_payload -> 'label' WHERE expected_task_type = 'image';");

            migrationBuilder.Sql(
                "UPDATE projects SET label_schema = jsonb_build_object(" +
                "'kind', 'classification', " +
                "'classes', label_schema -> 'tools' -> 0 -> 'classes', " +
                "'allowMultiple', label_schema -> 'tools' -> 0 -> 'allowMultiple') " +
                "WHERE label_schema IS NOT NULL AND label_schema ? 'tools';");

            migrationBuilder.AddColumn<string>(
                name: "task_type",
                table: "projects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "ImageClassification");

            migrationBuilder.Sql("ALTER TABLE projects ALTER COLUMN task_type DROP DEFAULT;");
            migrationBuilder.DropColumn(name: "modality", table: "projects");
        }
    }
}
