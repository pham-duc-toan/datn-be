using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutovacuumHangDoiTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bang tasks la HANG DOI: task chuyen Open → Completed lien tuc, de lai muc chi muc
            // chet DON O DAU HANG (task cu xong truoc) — dung cho cau lay task bat dau quet.
            // Mac dinh autovacuum chi don khi dong chet > 20% bang (20.000 / 100.000 task).
            // NC-B-06: 14.722 dong chet lam cau lay task tu ~3 ms len 7–20 ms (doc 1.133 trang
            // thay vi 10). Don som khi dong chet vuot 1% bang + 1.000 dong.
            migrationBuilder.Sql(
                "ALTER TABLE tasks SET (autovacuum_vacuum_scale_factor = 0.01, autovacuum_vacuum_threshold = 1000, "
                + "autovacuum_analyze_scale_factor = 0.02, autovacuum_vacuum_cost_delay = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE tasks RESET (autovacuum_vacuum_scale_factor, autovacuum_vacuum_threshold, "
                + "autovacuum_analyze_scale_factor, autovacuum_vacuum_cost_delay)");
        }
    }
}
