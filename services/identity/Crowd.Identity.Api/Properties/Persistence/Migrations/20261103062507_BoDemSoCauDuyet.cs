using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Properties.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoDemSoCauDuyet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "labeler_daily_stats");

            migrationBuilder.DropIndex(
                name: "ix_labeler_stats_xep_hang",
                table: "labeler_stats");

            migrationBuilder.DropColumn(
                name: "approved_count",
                table: "labeler_stats");

            migrationBuilder.CreateTable(
                name: "level_dirty",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dirty_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_level_dirty", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "level_dirty");

            migrationBuilder.AddColumn<int>(
                name: "approved_count",
                table: "labeler_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "labeler_daily_stats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    approved_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labeler_daily_stats", x => new { x.user_id, x.day });
                });

            migrationBuilder.CreateIndex(
                name: "ix_labeler_stats_xep_hang",
                table: "labeler_stats",
                column: "approved_count");

            migrationBuilder.CreateIndex(
                name: "ix_labeler_daily_stats_ngay",
                table: "labeler_daily_stats",
                column: "day");
        }
    }
}
