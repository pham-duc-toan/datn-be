using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuatThuongNhanhNhat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_participants",
                table: "project_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "rewarded",
                table: "attempts",
                type: "boolean",
                nullable: true);

            // Du lieu cu: truoc 10/2026 so suat = tran nguoi lam va MOI bai dat deu duoc thuong.
            migrationBuilder.Sql("UPDATE project_snapshots SET max_participants = slots;");
            migrationBuilder.Sql(
                "UPDATE attempts SET rewarded = (passed = true OR appeal_state = 'Accepted') WHERE graded_at IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_participants",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "rewarded",
                table: "attempts");
        }
    }
}
