using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoBanSaoEmailKyb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_status");

            migrationBuilder.DropTable(
                name: "verified_businesses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_status",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_status", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "verified_businesses",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verified_businesses", x => x.business_id);
                });
        }
    }
}
