using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Link.Api.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: gate-svc doc link dang chay qua view v_gate_links (role gate_reader) thay cho ban sao gate_links
    /// cap nhat bang link.activated / link.disabled. "Link nao phuc vu duoc" (da qua kiem duyet, chua bi khoa / xoa)
    /// nam o day, phia link-svc.
    /// </summary>
    public partial class ViewLinkChoCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("gate_reader"));
            migrationBuilder.Sql(@"
CREATE VIEW v_gate_links AS
SELECT l.id AS link_id,
       l.code,
       l.owner_id,
       l.destination_url,
       l.password_hash,
       l.expires_at,
       l.campaign_id,
       l.creator_ip_hash
FROM links l
WHERE l.status = 'Active';");
            migrationBuilder.Sql("GRANT SELECT ON v_gate_links TO gate_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gate_links;");
        }
    }
}
