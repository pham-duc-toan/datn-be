using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Notification.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GopThongBao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "amount_vnd",
                table: "notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "merged_count",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amount_vnd",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "merged_count",
                table: "notifications");
        }
    }
}
