using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Gate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: bo 5 bang ban sao (du an, mau, cau vang, ngan sach, link). gate doc thang view v_gate_* cua
    /// project / ledger / link de nap bo nho; gate_db chi con outbox, processed_events, settings_replica.
    /// </summary>
    public partial class BoBanSaoCongLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                    expected_payload = table.Column<string>(type: "jsonb", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    task_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
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
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    creator_ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    destination_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    content_loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    label_schema = table.Column<string>(type: "jsonb", nullable: false),
                    modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_fee_vnd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price_vnd = table.Column<long>(type: "bigint", nullable: false),
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
                    content = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_samples", x => x.sample_id);
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
                name: "ix_gate_projects_cho_nap",
                table: "gate_projects",
                column: "content_loaded_at",
                filter: "content_loaded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gate_samples_project",
                table: "gate_samples",
                column: "project_id");
        }
    }
}
