using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BoDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_samples_datasets_dataset_id",
                table: "samples");

            migrationBuilder.DropTable(
                name: "datasets");

            migrationBuilder.DropIndex(
                name: "ix_samples_dataset",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "dataset_id",
                table: "samples");

            migrationBuilder.AlterColumn<string>(
                name: "sha256",
                table: "samples",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "ingest_error",
                table: "samples",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ingest_source",
                table: "samples",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ingest_state",
                table: "samples",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ready");

            migrationBuilder.CreateIndex(
                name: "ix_samples_dang_nap",
                table: "samples",
                column: "ingest_state",
                filter: "ingest_state IN ('queued','processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_samples_dang_nap",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "ingest_error",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "ingest_source",
                table: "samples");

            migrationBuilder.DropColumn(
                name: "ingest_state",
                table: "samples");

            migrationBuilder.AlterColumn<string>(
                name: "sha256",
                table: "samples",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "dataset_id",
                table: "samples",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error_summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    import_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    import_spec = table.Column<string>(type: "jsonb", nullable: true),
                    manifest = table.Column<string>(type: "jsonb", nullable: true),
                    manifest_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_datasets", x => x.id);
                    table.ForeignKey(
                        name: "FK_datasets_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_samples_dataset",
                table: "samples",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "ix_datasets_dang_xu_ly",
                table: "datasets",
                column: "status",
                filter: "status IN ('Pending','Ingesting')");

            migrationBuilder.CreateIndex(
                name: "ix_datasets_project",
                table: "datasets",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_samples_datasets_dataset_id",
                table: "samples",
                column: "dataset_id",
                principalTable: "datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
