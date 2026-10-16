using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoiMoiTheoEmailHanMuc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_project_invitations_cho",
                table: "project_invitations");

            // Loi moi cu khong biet duoc moi bang email hay userId → coi la email (an userId voi chu du an,
            // phia an toan cua VD-S-16). Dong moi luon do code ghi gia tri.
            migrationBuilder.AddColumn<bool>(
                name: "by_email",
                table: "project_invitations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_han_muc",
                table: "project_invitations",
                columns: new[] { "invited_by", "created_at" },
                filter: "by_email");

            migrationBuilder.CreateIndex(
                name: "ux_project_invitations_cho",
                table: "project_invitations",
                columns: new[] { "project_id", "email", "by_email" },
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_invitations_han_muc",
                table: "project_invitations");

            migrationBuilder.DropIndex(
                name: "ux_project_invitations_cho",
                table: "project_invitations");

            migrationBuilder.DropColumn(
                name: "by_email",
                table: "project_invitations");

            migrationBuilder.CreateIndex(
                name: "ux_project_invitations_cho",
                table: "project_invitations",
                columns: new[] { "project_id", "email" },
                unique: true,
                filter: "status = 'Pending'");
        }
    }
}
