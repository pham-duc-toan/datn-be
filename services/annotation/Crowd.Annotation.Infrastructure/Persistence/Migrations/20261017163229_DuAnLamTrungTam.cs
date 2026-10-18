using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Annotation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuAnLamTrungTam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Du an lam trung tam (docs 3.18): bo duyet / khieu nai / dong thuan tung nhan. Trang thai cu:
            //   Approved → Accepted (da vao ket qua), PendingReview → Submitted (cho cham),
            //   Rejected / Appealed → Excluded (khong vao ket qua).
            migrationBuilder.Sql(@"
UPDATE annotations SET status = CASE status
    WHEN 'Approved' THEN 'Accepted'
    WHEN 'PendingReview' THEN 'Submitted'
    ELSE 'Excluded' END
WHERE status NOT IN ('Submitted', 'Accepted', 'Excluded');");

            migrationBuilder.DropTable(
                name: "task_consensus");

            migrationBuilder.DropIndex(
                name: "ix_annotations_appeals",
                table: "annotations");

            migrationBuilder.DropIndex(
                name: "ux_annotations_assignment",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "platform_fee_vnd",
                table: "project_terms");

            migrationBuilder.DropColumn(
                name: "require_review",
                table: "project_terms");

            migrationBuilder.DropColumn(
                name: "unit_price_vnd",
                table: "project_terms");

            migrationBuilder.DropColumn(
                name: "appeal_message",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "appeal_resolved_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "appealed_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "consensus_agrees",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "consensus_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "disputed_at",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "reject_reason",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "reviewer_id",
                table: "annotations");

            // Luc duyet cu = luc chot vao / loai khoi ket qua.
            migrationBuilder.RenameColumn(
                name: "reviewed_at",
                table: "annotations",
                newName: "decided_at");

            // Moi luot lease cu dung mot nhan → (attempt_id, sample_id) van duy nhat.
            migrationBuilder.RenameColumn(
                name: "assignment_id",
                table: "annotations",
                newName: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_annotations_attempt_sample",
                table: "annotations",
                columns: new[] { "attempt_id", "sample_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_annotations_attempt_sample",
                table: "annotations");

            migrationBuilder.RenameColumn(
                name: "decided_at",
                table: "annotations",
                newName: "reviewed_at");

            migrationBuilder.RenameColumn(
                name: "attempt_id",
                table: "annotations",
                newName: "assignment_id");

            migrationBuilder.AddColumn<long>(
                name: "platform_fee_vnd",
                table: "project_terms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "require_review",
                table: "project_terms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "unit_price_vnd",
                table: "project_terms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "appeal_message",
                table: "annotations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appeal_resolved_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appealed_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: true);

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

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disputed_at",
                table: "annotations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reject_reason",
                table: "annotations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewer_id",
                table: "annotations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_consensus",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    final = table.Column<string>(type: "jsonb", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_consensus", x => x.task_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_annotations_appeals",
                table: "annotations",
                column: "status",
                filter: "status = 'Appealed'");

            migrationBuilder.CreateIndex(
                name: "ux_annotations_assignment",
                table: "annotations",
                column: "assignment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_consensus_project_sample",
                table: "task_consensus",
                columns: new[] { "project_id", "sample_id" });
        }
    }
}
