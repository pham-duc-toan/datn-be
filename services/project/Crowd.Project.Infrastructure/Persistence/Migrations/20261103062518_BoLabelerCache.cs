using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoLabelerCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "labeler_cache");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_chap_nhan",
                table: "attempts",
                column: "labeler_id",
                filter: "passed = true OR appeal_state = 'Accepted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attempts_chap_nhan",
                table: "attempts");

            migrationBuilder.CreateTable(
                name: "labeler_cache",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: true),
                    level_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reputation = table.Column<int>(type: "integer", nullable: true),
                    reputation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labeler_cache", x => x.user_id);
                });
        }
    }
}
