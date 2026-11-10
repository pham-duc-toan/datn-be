using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Toi gian luu cau tra loi:
    ///   - bo attempt_drafts: nhap nam o localStorage cua frontend, nop thi gui ca bai;
    ///   - annotations chi con project_id, attempt_id, sample_id, labeler_id, source, payload. Khoa chinh
    ///     (project_id, attempt_id, sample_id) thay cho id + UNIQUE cu. status / submitted_at / decided_at suy ra tu
    ///     attempts; task_type / schema_version suy ra tu projects.modality.
    /// Bang phan vung HASH(project_id): Postgres cho DROP COLUMN / doi khoa chinh tren bang cha (lan xuong 64 phan
    /// vung) vi khoa moi van chua cot phan vung. Du lieu chi la seed — khong can chuyen.
    /// </summary>
    public partial class NhanToiGian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_drafts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_annotations",
                table: "annotations");

            migrationBuilder.DropIndex(
                name: "ix_annotations_project_sample",
                table: "annotations");

            migrationBuilder.DropIndex(
                name: "ix_annotations_project_status",
                table: "annotations");

            migrationBuilder.DropIndex(
                name: "ux_annotations_attempt_sample",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "id",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "schema_version",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "status",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "task_type",
                table: "annotations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_annotations",
                table: "annotations",
                columns: new[] { "project_id", "attempt_id", "sample_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_annotations",
                table: "annotations");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "annotations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                table: "annotations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "annotations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "task_type",
                table: "annotations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_annotations",
                table: "annotations",
                columns: new[] { "project_id", "id" });

            migrationBuilder.CreateTable(
                name: "attempt_drafts",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_drafts", x => new { x.attempt_id, x.sample_id });
                    table.ForeignKey(
                        name: "FK_attempt_drafts_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_annotations_project_sample",
                table: "annotations",
                columns: new[] { "project_id", "sample_id" });

            migrationBuilder.CreateIndex(
                name: "ix_annotations_project_status",
                table: "annotations",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_annotations_attempt_sample",
                table: "annotations",
                columns: new[] { "project_id", "attempt_id", "sample_id" },
                unique: true);
        }
    }
}
