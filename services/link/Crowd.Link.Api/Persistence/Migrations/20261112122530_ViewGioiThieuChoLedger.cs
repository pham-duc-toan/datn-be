using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Link.Api.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: ledger-svc doc quan he gioi thieu qua view v_referrals (role ledger_reader) thay cho ban sao
    /// ledger.referrals cap nhat bang referral.registered. Chi quan he DANG HIEU LUC (active) — nghi gian lan chua duyet
    /// hoac bi tu choi thi khong duoc hoa hong; luat nay nam o day, phia link-svc.
    /// </summary>
    public partial class ViewGioiThieuChoLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("ledger_reader"));
            migrationBuilder.Sql(@"
CREATE VIEW v_referrals AS
SELECT r.referred_id, r.referrer_id
FROM referrals r
WHERE r.status = 'active';");
            migrationBuilder.Sql("GRANT SELECT ON v_referrals TO ledger_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_referrals;");
        }
    }
}
