using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DonCotThua : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_holds_task",
                table: "holds");

            migrationBuilder.DropIndex(
                name: "ux_holds_source",
                table: "holds");

            migrationBuilder.DropColumn(
                name: "name",
                table: "project_escrows");

            // annotation_id thuc ra la ID NGUON cua khoan treo (bai lam / luot vuot link) → doi ten, giu du lieu.
            // task_id la du lieu cu (luon Guid.Empty) → bo.
            migrationBuilder.DropColumn(
                name: "task_id",
                table: "holds");

            migrationBuilder.RenameColumn(
                name: "annotation_id",
                table: "holds",
                newName: "source_id");

            migrationBuilder.CreateIndex(
                name: "ux_holds_source",
                table: "holds",
                columns: new[] { "source_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_holds_source",
                table: "holds");

            migrationBuilder.RenameColumn(
                name: "source_id",
                table: "holds",
                newName: "annotation_id");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "project_escrows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                table: "holds",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_holds_task",
                table: "holds",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_holds_source",
                table: "holds",
                columns: new[] { "annotation_id", "kind" },
                unique: true);
        }
    }
}
