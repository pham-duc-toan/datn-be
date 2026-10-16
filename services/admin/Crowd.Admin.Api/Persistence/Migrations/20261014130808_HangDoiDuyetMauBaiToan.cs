using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Admin.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HangDoiDuyetMauBaiToan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approval_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label_schema = table.Column<string>(type: "jsonb", nullable: false),
                    guideline_markdown = table.Column<string>(type: "text", nullable: false),
                    suggested_unit_price_vnd = table.Column<long>(type: "bigint", nullable: true),
                    suggested_redundancy = table.Column<int>(type: "integer", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_hang_doi",
                table: "approval_requests",
                columns: new[] { "status", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_approval_requests_don",
                table: "approval_requests",
                columns: new[] { "kind", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_templates_loai",
                table: "workspace_templates",
                columns: new[] { "active", "modality" });

            migrationBuilder.CreateIndex(
                name: "ux_workspace_templates_code",
                table: "workspace_templates",
                column: "code",
                unique: true);

            // P-03 / VD-S-10: nhat ky audit CHI GHI THEM o muc CSDL — ke ca tai khoan ung dung (admin-svc)
            // cung khong sua duoc dong nao, khong TRUNCATE duoc, chi XOA duoc dong cu hon 7 ngay (gioi han
            // duoi cua admin.audit_retention_days — AuditRetentionWorker chi xoa dong qua han luu).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION audit_chi_ghi_them() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' AND OLD.occurred_at < now() - interval '7 days' THEN
        RETURN OLD;
    END IF;
    RAISE EXCEPTION 'audit_events chi ghi them: khong duoc % (event %)', TG_OP, COALESCE(OLD.event_id::text, '*')
        USING ERRCODE = 'insufficient_privilege';
END $$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION audit_cam_truncate() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit_events chi ghi them: khong duoc TRUNCATE' USING ERRCODE = 'insufficient_privilege';
END $$ LANGUAGE plpgsql;

CREATE TRIGGER tg_audit_events_chi_ghi_them BEFORE UPDATE OR DELETE ON audit_events
    FOR EACH ROW EXECUTE FUNCTION audit_chi_ghi_them();
CREATE TRIGGER tg_audit_events_cam_truncate BEFORE TRUNCATE ON audit_events
    FOR EACH STATEMENT EXECUTE FUNCTION audit_cam_truncate();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tg_audit_events_cam_truncate ON audit_events;
DROP TRIGGER IF EXISTS tg_audit_events_chi_ghi_them ON audit_events;
DROP FUNCTION IF EXISTS audit_cam_truncate();
DROP FUNCTION IF EXISTS audit_chi_ghi_them();
");

            migrationBuilder.DropTable(
                name: "approval_requests");

            migrationBuilder.DropTable(
                name: "workspace_templates");
        }
    }
}
