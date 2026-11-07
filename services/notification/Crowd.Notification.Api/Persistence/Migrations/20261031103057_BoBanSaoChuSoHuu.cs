using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Notification.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoBanSaoChuSoHuu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_owners");

            migrationBuilder.DropTable(
                name: "withdrawal_owners");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_owners",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_owners", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_owners",
                columns: table => new
                {
                    withdrawal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    net_amount_vnd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawal_owners", x => x.withdrawal_id);
                });
        }
    }
}
