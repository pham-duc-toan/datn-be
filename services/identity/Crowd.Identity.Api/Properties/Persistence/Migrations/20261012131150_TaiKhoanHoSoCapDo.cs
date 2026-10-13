using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Identity.Api.Properties.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaiKhoanHoSoCapDo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_key",
                table: "users",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "skill_domains",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "skill_languages",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "skill_modalities",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Tai khoan co tu truoc khi co xac thuc email: coi nhu da xac thuc (khong chan rut tien /
            // publish cua nguoi dang dung). Chi tai khoan dang ky tu nay moi phai bam link.
            migrationBuilder.Sql("UPDATE users SET email_verified_at = created_at WHERE email_verified_at IS NULL;");

            migrationBuilder.CreateTable(
                name: "account_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "labeler_stats",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_count = table.Column<int>(type: "integer", nullable: false),
                    reputation = table.Column<int>(type: "integer", nullable: true),
                    reputation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labeler_stats", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "session_revocations",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_revocations", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_badges", x => new { x.user_id, x.badge });
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_het_han",
                table: "account_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_user",
                table: "account_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ux_account_tokens_hash",
                table: "account_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_labeler_daily_stats_ngay",
                table: "labeler_daily_stats",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_labeler_stats_xep_hang",
                table: "labeler_stats",
                column: "approved_count");

            migrationBuilder.CreateIndex(
                name: "ix_session_revocations_moc",
                table: "session_revocations",
                column: "revoked_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "labeler_daily_stats");

            migrationBuilder.DropTable(
                name: "labeler_stats");

            migrationBuilder.DropTable(
                name: "session_revocations");

            migrationBuilder.DropTable(
                name: "user_badges");

            migrationBuilder.DropColumn(
                name: "avatar_key",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "skill_domains",
                table: "users");

            migrationBuilder.DropColumn(
                name: "skill_languages",
                table: "users");

            migrationBuilder.DropColumn(
                name: "skill_modalities",
                table: "users");
        }
    }
}
