using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoaDonVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "project_escrows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_counters",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_counters", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    serial = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    buyer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    buyer_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    buyer_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    buyer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seller_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    vat_percent = table.Column<int>(type: "integer", nullable: false),
                    total_net_vnd = table.Column<long>(type: "bigint", nullable: false),
                    total_vat_vnd = table.Column<long>(type: "bigint", nullable: false),
                    total_vnd = table.Column<long>(type: "bigint", nullable: false),
                    amount_in_words = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    pass_through_vnd = table.Column<long>(type: "bigint", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    pass_through = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_period",
                table: "invoices",
                column: "period");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_business_period",
                table: "invoices",
                columns: new[] { "business_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoices_so",
                table: "invoices",
                columns: new[] { "serial", "number" },
                unique: true);

            // FB-05: hoa don da xuat la BAT BIEN (giong so cai, VD-M-04) — sai thi xuat hoa don dieu chinh,
            // khong sua / xoa dong cu. Dung lai ham ledger_cam_sua() cua migration KhoiTao.
            migrationBuilder.Sql(@"
CREATE TRIGGER tg_invoices_cam_sua BEFORE UPDATE OR DELETE ON invoices
    FOR EACH ROW EXECUTE FUNCTION ledger_cam_sua();
CREATE TRIGGER tg_invoices_cam_truncate BEFORE TRUNCATE ON invoices
    FOR EACH STATEMENT EXECUTE FUNCTION ledger_cam_sua();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tg_invoices_cam_truncate ON invoices;
DROP TRIGGER IF EXISTS tg_invoices_cam_sua ON invoices;
");

            migrationBuilder.DropTable(
                name: "invoice_counters");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropColumn(
                name: "name",
                table: "project_escrows");
        }
    }
}
