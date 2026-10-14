using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DonDuAnDaDong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "data_purged_at",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_progress",
                table: "project_snapshots",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_snapshots_cho_don",
                table: "project_snapshots",
                column: "status_changed_at",
                filter: "status = 'Closed' AND data_purged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_task",
                table: "assignments",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_snapshots_cho_don",
                table: "project_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_assignments_task",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "data_purged_at",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "final_progress",
                table: "project_snapshots");
        }
    }
}
