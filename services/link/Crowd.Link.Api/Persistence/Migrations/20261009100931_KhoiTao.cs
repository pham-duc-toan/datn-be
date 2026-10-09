using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Link.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KhoiTao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "blocked_domains",
                columns: table => new
                {
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_domains", x => x.domain);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "known_users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_known_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "link_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_link_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    destination_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    creator_ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    envelope_json = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => new { x.event_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "referral_codes",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referral_codes", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    referred_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referrer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.referred_id);
                });

            migrationBuilder.CreateTable(
                name: "settings_replica",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings_replica", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ux_api_keys_hash",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_campaigns_owner_name",
                table: "campaigns",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_link_reports_link_ip",
                table: "link_reports",
                columns: new[] { "link_id", "ip_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_links_cho_kiem_duyet",
                table: "links",
                column: "updated_at",
                filter: "needs_review");

            migrationBuilder.CreateIndex(
                name: "ix_links_cho_quet",
                table: "links",
                column: "created_at",
                filter: "status = 'PendingScan'");

            migrationBuilder.CreateIndex(
                name: "ix_links_domain",
                table: "links",
                column: "domain");

            migrationBuilder.CreateIndex(
                name: "ix_links_owner",
                table: "links",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_links_code",
                table: "links",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_cho_gui",
                table: "outbox",
                column: "next_attempt_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_da_gui",
                table: "outbox",
                column: "published_at",
                filter: "published_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_processed_events_thoi_diem",
                table: "processed_events",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ux_referral_codes_code",
                table: "referral_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referrals_referrer",
                table: "referrals",
                column: "referrer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "blocked_domains");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "known_users");

            migrationBuilder.DropTable(
                name: "link_reports");

            migrationBuilder.DropTable(
                name: "links");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "referral_codes");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "settings_replica");
        }
    }
}
