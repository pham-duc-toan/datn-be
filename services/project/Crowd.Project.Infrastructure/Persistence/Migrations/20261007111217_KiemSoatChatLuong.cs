using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kiem soat chat luong (P3): tran redundancy thich ung + ti le cau vang kiem tra.
    ///
    /// Du an cu: max_redundancy = redundancy (khong thich ung, ky quy cu van dung),
    /// gold_check_percent = 10 (mac dinh moi). Mac dinh SQL chi de lap dong cu, bo
    /// ngay sau do: dong moi bat buoc code ghi tuong minh.
    /// </summary>
    public partial class KiemSoatChatLuong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_redundancy",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "gold_check_percent",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.Sql("UPDATE projects SET max_redundancy = redundancy;");

            migrationBuilder.Sql(
                "ALTER TABLE projects ALTER COLUMN max_redundancy DROP DEFAULT, ALTER COLUMN gold_check_percent DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "gold_check_percent", table: "projects");
            migrationBuilder.DropColumn(name: "max_redundancy", table: "projects");
        }
    }
}
