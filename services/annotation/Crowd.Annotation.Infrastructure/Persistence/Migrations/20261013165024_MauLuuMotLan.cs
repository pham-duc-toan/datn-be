using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MauLuuMotLan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "annotation_samples",
                columns: table => new
                {
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    content = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annotation_samples", x => x.sample_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_annotation_samples_du_an",
                table: "annotation_samples",
                column: "project_id");

            // THU TU QUAN TRONG: tao bang → chep thong tin mau tu cac nhan da co (moi mau mot dong, lay tu
            // nhan nop som nhat) → MOI xoa ba cot cu. Xoa truoc la mat du lieu.
            migrationBuilder.Sql(@"
                INSERT INTO annotation_samples (sample_id, project_id, storage_key, content, metadata)
                SELECT DISTINCT ON (sample_id) sample_id, project_id, storage_key, sample_content, sample_metadata
                FROM annotations
                ORDER BY sample_id, submitted_at;");

            migrationBuilder.DropColumn(
                name: "sample_content",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "sample_metadata",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "storage_key",
                table: "annotations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annotation_samples");

            migrationBuilder.AddColumn<string>(
                name: "sample_content",
                table: "annotations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sample_metadata",
                table: "annotations",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "storage_key",
                table: "annotations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }
    }
}
