using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Gate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NapMauQuaGrpc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_loaded_at",
                table: "gate_projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_gate_projects_cho_nap",
                table: "gate_projects",
                column: "content_loaded_at",
                filter: "content_loaded_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gate_projects_cho_nap",
                table: "gate_projects");

            migrationBuilder.DropColumn(
                name: "content_loaded_at",
                table: "gate_projects");
        }
    }
}
