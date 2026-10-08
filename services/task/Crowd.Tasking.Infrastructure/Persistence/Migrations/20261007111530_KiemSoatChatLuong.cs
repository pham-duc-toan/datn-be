using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kiem soat chat luong (P3): ban sao du an co tran redundancy + ti le cau vang
    /// kiem tra; assignments danh dau luot tra loi cau vang.
    ///
    /// Ban sao cu: max_redundancy = redundancy (khong thich ung), gold_check_percent =
    /// 10 cho du an da cau hinh (khop migration KiemSoatChatLuong cua project-svc).
    /// </summary>
    public partial class KiemSoatChatLuong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_redundancy",
                table: "project_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "gold_check_percent",
                table: "project_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE project_snapshots SET max_redundancy = redundancy, " +
                "gold_check_percent = CASE WHEN is_configured THEN 10 ELSE 0 END;");

            migrationBuilder.AddColumn<bool>(
                name: "is_gold",
                table: "assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "ALTER TABLE project_snapshots ALTER COLUMN max_redundancy DROP DEFAULT, ALTER COLUMN gold_check_percent DROP DEFAULT; " +
                "ALTER TABLE assignments ALTER COLUMN is_gold DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "is_gold", table: "assignments");
            migrationBuilder.DropColumn(name: "gold_check_percent", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "max_redundancy", table: "project_snapshots");
        }
    }
}
