using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLoaiTruBaiLam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Du an lam trung tam (kien truc 3.18): event tan suat cao doi tu tung nhan sang ca bai (attempt.submitted).
            // Admin CHUA sua danh sach loai tru (con dung mac dinh 10/2026) thi nang len mac dinh moi, co ghi lich su.
            migrationBuilder.Sql(@"
                WITH doi AS (
                    UPDATE settings
                    SET value = to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh + @"'::text),
                        version = version + 1, updated_at = now(), updated_by = NULL
                    WHERE key = 'admin.audit_excluded_types'
                      AND value = to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh1013 + @"'::text)
                    RETURNING version)
                INSERT INTO setting_history (key, old_value, new_value, version, changed_by, changed_at, reason)
                SELECT 'admin.audit_excluded_types',
                       to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh1013 + @"'::text),
                       to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh + @"'::text),
                       version, NULL, now(), 'Migration: du an lam trung tam — chi dem attempt.submitted, khong luu chi tiet'
                FROM doi;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
