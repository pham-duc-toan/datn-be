using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChuyenKhoanThuCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reject_reason",
                table: "payment_intents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                table: "payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_code",
                table: "payment_intents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transferred_at",
                table: "payment_intents",
                type: "timestamp with time zone",
                nullable: true);

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
                name: "ix_intents_cho_duyet",
                table: "payment_intents",
                column: "created_at",
                filter: "status = 'AwaitingApproval'");

            migrationBuilder.CreateIndex(
                name: "ux_intents_transfer_code",
                table: "payment_intents",
                column: "transfer_code",
                unique: true,
                filter: "transfer_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings_replica");

            migrationBuilder.DropIndex(
                name: "ix_intents_cho_duyet",
                table: "payment_intents");

            migrationBuilder.DropIndex(
                name: "ux_intents_transfer_code",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "reject_reason",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "transfer_code",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "transferred_at",
                table: "payment_intents");
        }
    }
}
