using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditThongBao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    send_email = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    producer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_thoi_gian",
                table: "announcements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_correlation",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_loai",
                table: "audit_events",
                columns: new[] { "event_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_nguoi_lam",
                table: "audit_events",
                columns: new[] { "actor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_thoi_gian",
                table: "audit_events",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
