using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuyetRutTien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                table: "withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                table: "withdrawals",
                type: "uuid",
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
                name: "ix_withdrawals_cho_duyet",
                table: "withdrawals",
                column: "created_at",
                filter: "state = 'PendingApproval'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings_replica");

            migrationBuilder.DropIndex(
                name: "ix_withdrawals_cho_duyet",
                table: "withdrawals");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "withdrawals");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "withdrawals");
        }
    }
}
