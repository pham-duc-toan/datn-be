using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CongLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_holds_annotation",
                table: "holds");

            migrationBuilder.AddColumn<bool>(
                name: "allow_link_gateway",
                table: "project_escrows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "gate_budget_sequence",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "gate_spent_vnd",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "platform_fee_vnd",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "sample_count",
                table: "project_escrows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "unit_price_vnd",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "holds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Annotation");

            migrationBuilder.AddColumn<Guid>(
                name: "link_id",
                table: "holds",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    referred_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referrer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.referred_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holds_link_dang_treo",
                table: "holds",
                column: "link_id",
                filter: "link_id IS NOT NULL AND state = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ux_holds_source",
                table: "holds",
                columns: new[] { "annotation_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropIndex(
                name: "ix_holds_link_dang_treo",
                table: "holds");

            migrationBuilder.DropIndex(
                name: "ux_holds_source",
                table: "holds");

            migrationBuilder.DropColumn(
                name: "allow_link_gateway",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "gate_budget_sequence",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "gate_spent_vnd",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "platform_fee_vnd",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "sample_count",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "unit_price_vnd",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "holds");

            migrationBuilder.DropColumn(
                name: "link_id",
                table: "holds");

            migrationBuilder.CreateIndex(
                name: "ux_holds_annotation",
                table: "holds",
                column: "annotation_id",
                unique: true);
        }
    }
}
