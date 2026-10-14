using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Properties.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhienDangNhap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.CreateTable(
                name: "login_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_sessions_het_han",
                table: "login_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_sessions_user",
                table: "login_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_sessions");

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_het_han",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }
    }
}
