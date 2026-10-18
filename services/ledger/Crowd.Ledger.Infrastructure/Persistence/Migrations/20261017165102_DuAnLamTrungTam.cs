using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuAnLamTrungTam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Du an lam trung tam (docs 3.18): ky quy theo SO SUAT x (thuong + phi) + ngan sach cong link;
            // bo tranh chap nhan da duyet. Them cot moi, doi du lieu cu bang SQL, roi bo cot cu (khong rename:
            // nghia khac han — don gia mot nhan khong phai thuong mot bai).
            migrationBuilder.AddColumn<int>(
                name: "slots",
                table: "project_escrows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "reward_vnd",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "gate_budget_vnd",
                table: "project_escrows",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "expected_paid_attempts",
                table: "project_escrows",
                type: "integer",
                nullable: true);

            // Du lieu cu: moi "luot redundancy" ~ mot suat lam het cac mau; thuong / phi mot suat = don gia /
            // phi x so mau; phan ky quy vuot muc danh cho labeler la ngan sach cong link.
            migrationBuilder.Sql(@"
UPDATE project_escrows SET
    slots = GREATEST(redundancy, 0),
    reward_vnd = unit_price_vnd * sample_count,
    platform_fee_vnd = platform_fee_vnd * sample_count,
    gate_budget_vnd = CASE WHEN allow_link_gateway
        THEN GREATEST(reserved_vnd - sample_count::bigint * redundancy * (unit_price_vnd + platform_fee_vnd), 0)
        ELSE 0 END,
    expected_paid_attempts = expected_paid_annotations;");

            migrationBuilder.DropColumn(
                name: "redundancy",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "unit_price_vnd",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "sample_count",
                table: "project_escrows");

            migrationBuilder.DropColumn(
                name: "expected_paid_annotations",
                table: "project_escrows");

            // Tranh chap nhan da duyet (FM-06) bi bo cung duyet tung nhan; khoan dang dong bang tro lai treo.
            migrationBuilder.Sql("UPDATE holds SET state = 'Held' WHERE state = 'Disputed';");

            migrationBuilder.DropTable(
                name: "disputes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gate_budget_vnd",
                table: "project_escrows");

            migrationBuilder.RenameColumn(
                name: "slots",
                table: "project_escrows",
                newName: "sample_count");

            migrationBuilder.RenameColumn(
                name: "reward_vnd",
                table: "project_escrows",
                newName: "unit_price_vnd");

            migrationBuilder.RenameColumn(
                name: "expected_paid_attempts",
                table: "project_escrows",
                newName: "expected_paid_annotations");

            migrationBuilder.AddColumn<int>(
                name: "redundancy",
                table: "project_escrows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    annotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disputes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_business",
                table: "disputes",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_hang_doi",
                table: "disputes",
                columns: new[] { "state", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_disputes_annotation",
                table: "disputes",
                column: "annotation_id",
                unique: true);
        }
    }
}
