using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <summary>Bo huong dan Markdown cua mau bai toan: cau hoi chung nam trong tap nhan (label_schema.question).</summary>
    public partial class BoHuongDanMauBaiToan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guideline_markdown",
                table: "workspace_templates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guideline_markdown",
                table: "workspace_templates",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
