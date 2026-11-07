using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChiMucChongGianLanQuaGrpc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_attempts_client_ip",
                table: "attempts",
                columns: new[] { "client_ip", "submitted_at" },
                filter: "client_ip IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_device",
                table: "attempts",
                columns: new[] { "device_id", "submitted_at" },
                filter: "device_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attempts_client_ip",
                table: "attempts");

            migrationBuilder.DropIndex(
                name: "ix_attempts_device",
                table: "attempts");
        }
    }
}
