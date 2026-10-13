using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NhapBaoLoi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "draft",
                table: "assignments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "draft_saved_at",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_reports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_reports_du_an",
                table: "task_reports",
                columns: new[] { "project_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_reports_labeler_task",
                table: "task_reports",
                columns: new[] { "labeler_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_reports_task",
                table: "task_reports",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_reports_assignment",
                table: "task_reports",
                column: "assignment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_reports");

            migrationBuilder.DropColumn(
                name: "draft",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "draft_saved_at",
                table: "assignments");
        }
    }
}
