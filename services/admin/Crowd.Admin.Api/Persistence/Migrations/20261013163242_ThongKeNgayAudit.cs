using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThongKeNgayAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_counted",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_counted", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "stats_daily",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    fee_vnd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stats_daily", x => new { x.day, x.event_type });
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_counted_thoi_diem",
                table: "audit_counted",
                column: "seen_at");

            // So lieu da co: dem lai tu nhat ky chi tiet (truoc day bao cao quet thang audit_events).
            migrationBuilder.Sql(@"
                INSERT INTO stats_daily (day, event_type, count, amount_vnd, fee_vnd)
                SELECT (occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date, event_type, COUNT(*),
                       COALESCE(SUM(COALESCE((payload->>'amountVnd')::bigint, (payload->>'netAmountVnd')::bigint, 0)), 0),
                       COALESCE(SUM((payload->>'platformFeeVnd')::bigint), 0)
                FROM audit_events
                GROUP BY 1, 2;");

            // Danh sach loai tru: admin CHUA sua (con dung mac dinh cu) thi nang len mac dinh moi, co ghi lich su.
            migrationBuilder.Sql(@"
                WITH doi AS (
                    UPDATE settings
                    SET value = to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh + @"'::text),
                        version = version + 1, updated_at = now(), updated_by = NULL
                    WHERE key = 'admin.audit_excluded_types'
                      AND value = to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinhCu + @"'::text)
                    RETURNING version)
                INSERT INTO setting_history (key, old_value, new_value, version, changed_by, changed_at, reason)
                SELECT 'admin.audit_excluded_types',
                       to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinhCu + @"'::text),
                       to_jsonb('" + Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh + @"'::text),
                       version, NULL, now(), 'Migration: khong luu chi tiet event tan suat cao (van dem cho bao cao)'
                FROM doi;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_counted");

            migrationBuilder.DropTable(
                name: "stats_daily");
        }
    }
}
