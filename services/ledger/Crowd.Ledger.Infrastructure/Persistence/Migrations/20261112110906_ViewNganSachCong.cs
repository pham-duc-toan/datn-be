using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: gate-svc doc thang ngan sach cong link con lai qua view v_gate_budgets (role gate_reader) thay
    /// cho event gate.budget_changed + ban sao gate_budgets. Cong thuc o day TRUNG ProjectEscrow.NganSachCongConLai —
    /// ledger van la ben chan cuoi khi chi tien (MoneyFlowService), view chi de gate ngung phat cau cua du an het tien.
    /// Bo cot gate_budget_sequence (so thu tu cua event da bo).
    /// </summary>
    public partial class ViewNganSachCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gate_budget_sequence",
                table: "project_escrows");

            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("gate_reader"));
            migrationBuilder.Sql(@"
CREATE VIEW v_gate_budgets AS
SELECT e.project_id,
       CASE WHEN e.state = 'Active' AND e.slots > 0
            THEN GREATEST(0, LEAST(
                     e.gate_budget_vnd - e.gate_spent_vnd,
                     e.reserved_vnd - e.slots::bigint * (e.reward_vnd + e.platform_fee_vnd) - e.gate_spent_vnd))
            ELSE 0
       END AS remaining_vnd
FROM project_escrows e
WHERE e.allow_link_gateway;");
            migrationBuilder.Sql("GRANT SELECT ON v_gate_budgets TO gate_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gate_budgets;");
            migrationBuilder.AddColumn<long>(
                name: "gate_budget_sequence",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
