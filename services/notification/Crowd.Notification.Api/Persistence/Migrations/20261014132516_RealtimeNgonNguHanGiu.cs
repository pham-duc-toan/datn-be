using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Notification.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealtimeNgonNguHanGiu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "language_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_language_preferences", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "realtime_tickets",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_realtime_tickets", x => x.token_hash);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_thoi_gian",
                table: "notifications",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "language_preferences");

            migrationBuilder.DropTable(
                name: "realtime_tickets");

            migrationBuilder.DropIndex(
                name: "ix_notifications_thoi_gian",
                table: "notifications");
        }
    }
}
