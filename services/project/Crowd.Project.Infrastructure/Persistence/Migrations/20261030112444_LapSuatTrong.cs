using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LapSuatTrong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fill_slots_min_percent",
                table: "projects",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fill_slots_min_percent",
                table: "projects");
        }
    }
}
