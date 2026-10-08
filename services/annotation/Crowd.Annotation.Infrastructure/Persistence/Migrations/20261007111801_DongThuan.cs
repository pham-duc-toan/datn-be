using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DongThuan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "consensus_agrees",
                table: "annotations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consensus_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_consensus",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    final = table.Column<string>(type: "jsonb", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_consensus", x => x.task_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_consensus_project_sample",
                table: "task_consensus",
                columns: new[] { "project_id", "sample_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_consensus");

            migrationBuilder.DropColumn(
                name: "consensus_agrees",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "consensus_at",
                table: "annotations");
        }
    }
}
