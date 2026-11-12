using System;
using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Properties.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: (1) view v_contacts cho notification-svc (role notification_reader) thay ban sao contacts;
    /// (2) bo labeler_stats.reputation / reputation_at — uy tin doc thang view v_reputations cua quality.
    /// </summary>
    public partial class ViewLienHeVaBoUyTinSaoChep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reputation",
                table: "labeler_stats");

            migrationBuilder.DropColumn(
                name: "reputation_at",
                table: "labeler_stats");

            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("notification_reader"));
            migrationBuilder.Sql(@"
CREATE VIEW v_contacts AS
SELECT u.id AS user_id, u.email, u.display_name, u.roles
FROM users u;");
            migrationBuilder.Sql("GRANT SELECT ON v_contacts TO notification_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_contacts;");

            migrationBuilder.AddColumn<int>(
                name: "reputation",
                table: "labeler_stats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reputation_at",
                table: "labeler_stats",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
