using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuAnLamTrungTam : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Du an lam trung tam (docs 3.18): tien theo CA DU AN. Du an cu duoc quy doi gan dung:
        /// so suat = tran redundancy cu (it nhat 1), thuong moi nguoi = don gia cu x so cau, nguong 80%,
        /// so cau = so mau khong phai cau vang. Cot cu (don gia, redundancy, ti le cau vang, co duyet,
        /// ngan sach nhap tay) bi bo.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(name: "reward_vnd", table: "projects", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<int>(name: "slots", table: "projects", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "pass_threshold_percent", table: "projects", type: "integer", nullable: false, defaultValue: 80);
            migrationBuilder.AddColumn<int>(name: "question_count", table: "projects", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<long>(name: "gate_unit_price_vnd", table: "projects", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<long>(name: "gate_budget_vnd", table: "projects", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "finish_requested_at", table: "projects", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "grading_started_at", table: "projects", type: "timestamp with time zone", nullable: true);

            migrationBuilder.Sql(@"
UPDATE projects p SET
    question_count = (SELECT COUNT(*) FROM samples s
                      WHERE s.project_id = p.id
                        AND NOT EXISTS (SELECT 1 FROM gold_items g WHERE g.project_id = p.id AND g.sample_id = s.id)),
    slots = GREATEST(p.max_redundancy, p.redundancy, 1),
    pass_threshold_percent = 80;
UPDATE projects SET reward_vnd = unit_price_vnd * GREATEST(question_count, 1);
UPDATE projects SET gate_unit_price_vnd = unit_price_vnd, gate_budget_vnd = GREATEST(budget_vnd - unit_price_vnd * question_count * slots, 0)
    WHERE allow_link_gateway;");

            migrationBuilder.DropColumn(name: "unit_price_vnd", table: "projects");
            migrationBuilder.DropColumn(name: "redundancy", table: "projects");
            migrationBuilder.DropColumn(name: "max_redundancy", table: "projects");
            migrationBuilder.DropColumn(name: "gold_check_percent", table: "projects");
            migrationBuilder.DropColumn(name: "require_review", table: "projects");
            migrationBuilder.DropColumn(name: "budget_vnd", table: "projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(name: "unit_price_vnd", table: "projects", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<int>(name: "redundancy", table: "projects", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "max_redundancy", table: "projects", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "gold_check_percent", table: "projects", type: "integer", nullable: false, defaultValue: 10);
            migrationBuilder.AddColumn<bool>(name: "require_review", table: "projects", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<long>(name: "budget_vnd", table: "projects", type: "bigint", nullable: false, defaultValue: 0L);

            migrationBuilder.Sql(@"
UPDATE projects SET
    unit_price_vnd = reward_vnd / GREATEST(question_count, 1),
    redundancy = GREATEST(slots, 1),
    max_redundancy = GREATEST(slots, 1),
    budget_vnd = reward_vnd * GREATEST(slots, 1) + gate_budget_vnd;");

            migrationBuilder.DropColumn(name: "reward_vnd", table: "projects");
            migrationBuilder.DropColumn(name: "slots", table: "projects");
            migrationBuilder.DropColumn(name: "pass_threshold_percent", table: "projects");
            migrationBuilder.DropColumn(name: "question_count", table: "projects");
            migrationBuilder.DropColumn(name: "gate_unit_price_vnd", table: "projects");
            migrationBuilder.DropColumn(name: "gate_budget_vnd", table: "projects");
            migrationBuilder.DropColumn(name: "finish_requested_at", table: "projects");
            migrationBuilder.DropColumn(name: "grading_started_at", table: "projects");
        }
    }
}
