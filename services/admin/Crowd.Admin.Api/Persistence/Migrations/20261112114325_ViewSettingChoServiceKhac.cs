using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <summary>
    /// Kien truc 3.32: moi service (ca gateway, quality, media) doc setting THANG qua view v_settings bang role
    /// settings_reader (chi SELECT view nay) — thay cho bang settings_replica o tung service + event
    /// settings.snapshot / settings.snapshot_requested. Chi tra key / value / version (khong lo ai doi, luc nao).
    /// </summary>
    public partial class ViewSettingChoServiceKhac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReaderRoles.SqlTaoRole("settings_reader"));
            migrationBuilder.Sql(@"
CREATE VIEW v_settings AS
SELECT s.key, s.value, s.version
FROM settings s;");
            migrationBuilder.Sql("GRANT SELECT ON v_settings TO settings_reader;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_settings;");
        }
    }
}
