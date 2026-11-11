using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ViecCanXuLyTay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "detail",
                table: "approval_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "approval_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "detail",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "approval_requests");
        }
    }
}
