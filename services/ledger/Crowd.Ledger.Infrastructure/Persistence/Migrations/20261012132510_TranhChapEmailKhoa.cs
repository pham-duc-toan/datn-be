using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TranhChapEmailKhoa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "blocked_at",
                table: "blocked_users",
                newName: "changed_at");

            // Moi dong co tu truoc deu la tai khoan DANG BI KHOA (truoc day mo khoa khong xoa dong).
            migrationBuilder.AddColumn<bool>(
                name: "blocked",
                table: "blocked_users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    annotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disputes", x => x.id);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_holds_thu_nhap",
                table: "holds",
                columns: new[] { "labeler_id", "held_at" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_business",
                table: "disputes",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_hang_doi",
                table: "disputes",
                columns: new[] { "state", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_disputes_annotation",
                table: "disputes",
                column: "annotation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disputes");

            migrationBuilder.DropTable(
                name: "email_status");

            migrationBuilder.DropIndex(
                name: "ix_holds_thu_nhap",
                table: "holds");

            migrationBuilder.DropColumn(
                name: "blocked",
                table: "blocked_users");

            migrationBuilder.RenameColumn(
                name: "changed_at",
                table: "blocked_users",
                newName: "blocked_at");
        }
    }
}
