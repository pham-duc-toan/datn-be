using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuAnLamTrungTam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropIndex(
                name: "ux_task_reports_assignment",
                table: "task_reports");

            migrationBuilder.DropColumn(
                name: "active_lease_count",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "redundancy_target",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "submitted_count",
                table: "tasks");

            // Bao loi cu tro toi luot giu theo tung cau (bang assignments bi bo) — khong map duoc sang bai lam.
            migrationBuilder.Sql("DELETE FROM task_reports;");
            migrationBuilder.RenameColumn(
                name: "assignment_id",
                table: "task_reports",
                newName: "attempt_id");

            // Ban sao du an: tien theo CA DU AN (docs 3.18). Du an cu quy doi gan dung nhu project-svc:
            // so suat = tran redundancy cu, thuong = don gia cu x so cau, nguong 80%.
            migrationBuilder.AddColumn<long>(name: "reward_vnd", table: "project_snapshots", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<int>(name: "slots", table: "project_snapshots", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "pass_threshold_percent", table: "project_snapshots", type: "integer", nullable: false, defaultValue: 80);
            migrationBuilder.AddColumn<int>(name: "question_count", table: "project_snapshots", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.Sql(@"
UPDATE project_snapshots s SET
    question_count = (SELECT COUNT(*) FROM tasks t WHERE t.project_id = s.project_id AND t.state <> 'Excluded'),
    slots = GREATEST(s.max_redundancy, s.redundancy, 1);
UPDATE project_snapshots SET reward_vnd = unit_price_vnd * GREATEST(question_count, 1);");
            migrationBuilder.DropColumn(name: "unit_price_vnd", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "redundancy", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "max_redundancy", table: "project_snapshots");
            migrationBuilder.DropColumn(name: "gold_check_percent", table: "project_snapshots");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appeal_deadline",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "collection_finished_at",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finish_reason",
                table: "project_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "graded_at",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "passed_count",
                table: "project_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "settled_at",
                table: "project_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gold_total = table.Column<int>(type: "integer", nullable: false),
                    gold_correct = table.Column<int>(type: "integer", nullable: false),
                    client_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    comparable_count = table.Column<int>(type: "integer", nullable: true),
                    matched_count = table.Column<int>(type: "integer", nullable: true),
                    agreement_percent = table.Column<int>(type: "integer", nullable: true),
                    passed = table.Column<bool>(type: "boolean", nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    appeal_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appeal_resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    appeal_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    question_order = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attempt_answers",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_answers", x => new { x.attempt_id, x.sample_id });
                    table.ForeignKey(
                        name: "FK_attempt_answers_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_task_reports_attempt_sample",
                table: "task_reports",
                columns: new[] { "attempt_id", "sample_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_snapshots_cho_chot",
                table: "project_snapshots",
                column: "appeal_deadline",
                filter: "graded_at IS NOT NULL AND settled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_snapshots_cho_dong_thu_bai",
                table: "project_snapshots",
                column: "deadline",
                filter: "collection_finished_at IS NULL AND is_configured");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_het_han",
                table: "attempts",
                column: "expires_at",
                filter: "state = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_khieu_nai_mo",
                table: "attempts",
                column: "appealed_at",
                filter: "appeal_state = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_labeler",
                table: "attempts",
                columns: new[] { "labeler_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_project_state",
                table: "attempts",
                columns: new[] { "project_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_attempts_project_labeler_active",
                table: "attempts",
                columns: new[] { "project_id", "labeler_id" },
                unique: true,
                filter: "state IN ('InProgress','Submitted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_answers");

            migrationBuilder.DropTable(
                name: "attempts");

            migrationBuilder.DropIndex(
                name: "ux_task_reports_attempt_sample",
                table: "task_reports");

            migrationBuilder.DropIndex(
                name: "ix_project_snapshots_cho_chot",
                table: "project_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_project_snapshots_cho_dong_thu_bai",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "appeal_deadline",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "collection_finished_at",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "finish_reason",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "graded_at",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "passed_count",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "project_snapshots");

            migrationBuilder.DropColumn(
                name: "settled_at",
                table: "project_snapshots");

            migrationBuilder.RenameColumn(
                name: "attempt_id",
                table: "task_reports",
                newName: "assignment_id");

            migrationBuilder.RenameColumn(
                name: "slots",
                table: "project_snapshots",
                newName: "redundancy");

            migrationBuilder.RenameColumn(
                name: "reward_vnd",
                table: "project_snapshots",
                newName: "unit_price_vnd");

            migrationBuilder.RenameColumn(
                name: "question_count",
                table: "project_snapshots",
                newName: "max_redundancy");

            migrationBuilder.RenameColumn(
                name: "pass_threshold_percent",
                table: "project_snapshots",
                newName: "gold_check_percent");

            migrationBuilder.AddColumn<int>(
                name: "active_lease_count",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "redundancy_target",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "submitted_count",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_gold = table.Column<bool>(type: "boolean", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft = table.Column<string>(type: "jsonb", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    schema_version = table.Column<int>(type: "integer", nullable: true),
                    task_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.id);
                    table.CheckConstraint("ck_assignments_payload", "(task_type IS NULL) = (payload IS NULL) AND (schema_version IS NULL) = (payload IS NULL) AND (state = 'Submitted') = (payload IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_assignments_tasks_project_id_task_id",
                        columns: x => new { x.project_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_task_reports_assignment",
                table: "task_reports",
                column: "assignment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_labeler_active",
                table: "assignments",
                columns: new[] { "labeler_id", "task_id" },
                filter: "state IN ('Leased','Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_project_id_task_id",
                table: "assignments",
                columns: new[] { "project_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignments_reaper",
                table: "assignments",
                column: "expires_at",
                filter: "state = 'Leased'");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_task",
                table: "assignments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_assignments_one_lease_per_project",
                table: "assignments",
                columns: new[] { "project_id", "labeler_id" },
                unique: true,
                filter: "state = 'Leased'");

            migrationBuilder.CreateIndex(
                name: "ux_assignments_task_labeler_active",
                table: "assignments",
                columns: new[] { "task_id", "labeler_id" },
                unique: true,
                filter: "state IN ('Leased','Submitted')");
        }
    }
}
