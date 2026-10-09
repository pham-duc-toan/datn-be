using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Gate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KhoiTao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gate_budgets",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remaining_vnd = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_budgets", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "gate_gold_items",
                columns: table => new
                {
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    expected_payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_gold_items", x => x.sample_id);
                });

            migrationBuilder.CreateTable(
                name: "gate_links",
                columns: table => new
                {
                    link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creator_ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_links", x => x.link_id);
                });

            migrationBuilder.CreateTable(
                name: "gate_projects",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label_schema = table.Column<string>(type: "jsonb", nullable: false),
                    unit_price_vnd = table.Column<long>(type: "bigint", nullable: false),
                    platform_fee_vnd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_projects", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "gate_samples",
                columns: table => new
                {
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    content = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_samples", x => x.sample_id);
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
                name: "ix_gate_gold_project",
                table: "gate_gold_items",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_gate_links_code",
                table: "gate_links",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gate_samples_project",
                table: "gate_samples",
                column: "project_id");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gate_budgets");

            migrationBuilder.DropTable(
                name: "gate_gold_items");

            migrationBuilder.DropTable(
                name: "gate_links");

            migrationBuilder.DropTable(
                name: "gate_projects");

            migrationBuilder.DropTable(
                name: "gate_samples");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "settings_replica");
        }
    }
}
