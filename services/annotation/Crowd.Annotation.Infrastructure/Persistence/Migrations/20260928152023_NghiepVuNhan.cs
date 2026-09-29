using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NghiepVuNhan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "annotations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appeal_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    labels = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annotations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_members_cache",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members_cache", x => new { x.project_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "project_terms",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price_vnd = table.Column<long>(type: "bigint", nullable: false),
                    platform_fee_vnd = table.Column<long>(type: "bigint", nullable: false),
                    allow_multiple = table.Column<bool>(type: "boolean", nullable: false),
                    label_classes = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_terms", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "annotation_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    annotation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annotation_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_annotation_history_annotations_annotation_id",
                        column: x => x.annotation_id,
                        principalTable: "annotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annotation_history_annotation_id",
                table: "annotation_history",
                column: "annotation_id");

            migrationBuilder.CreateIndex(
                name: "ix_annotations_appeals",
                table: "annotations",
                column: "status",
                filter: "status = 'Appealed'");

            migrationBuilder.CreateIndex(
                name: "ix_annotations_labeler",
                table: "annotations",
                columns: new[] { "labeler_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_annotations_project_sample",
                table: "annotations",
                columns: new[] { "project_id", "sample_id" });

            migrationBuilder.CreateIndex(
                name: "ix_annotations_project_status",
                table: "annotations",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_annotations_assignment",
                table: "annotations",
                column: "assignment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "annotation_history");

            migrationBuilder.DropTable(
                name: "project_members_cache");

            migrationBuilder.DropTable(
                name: "project_terms");

            migrationBuilder.DropTable(
                name: "annotations");
        }
    }
}
