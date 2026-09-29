using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crowd.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KhoiTao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "blocked_users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocked_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    annotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    release_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    envelope_json = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => new { x.event_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "project_escrows",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserved_vnd = table.Column<long>(type: "bigint", nullable: false),
                    redundancy = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_escrows", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "withdrawals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    tax_vnd = table.Column<long>(type: "bigint", nullable: false),
                    net_vnd = table.Column<long>(type: "bigint", nullable: false),
                    bank_account = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.id);
                    table.CheckConstraint("ck_journal_lines_amount_khac_0", "amount <> 0");
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holds_den_han",
                table: "holds",
                column: "release_at",
                filter: "state = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ix_holds_task",
                table: "holds",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ux_holds_annotation",
                table: "holds",
                column: "annotation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_journal_seq",
                table: "journal_entries",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_journal_type_reference",
                table: "journal_entries",
                columns: new[] { "type", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_account",
                table: "journal_lines",
                column: "account_code");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_entry_id",
                table: "journal_lines",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_cho_gui",
                table: "outbox",
                column: "next_attempt_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_da_gui",
                table: "outbox",
                column: "published_at",
                filter: "published_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_processed_events_thoi_diem",
                table: "processed_events",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_project_escrows_owner",
                table: "project_escrows",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ux_withdrawals_idempotency",
                table: "withdrawals",
                columns: new[] { "labeler_id", "idempotency_key" },
                unique: true);

            // VD-M-04: so cai CHI THEM. Trigger chan UPDATE/DELETE/TRUNCATE ngay ca
            // khi ket noi bang tai khoan CHU database (REVOKE khong chan duoc chu
            // so huu). Sua sai = ghi but toan DAO, khong bao gio sua dong cu.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION ledger_cam_sua() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'So cai chi duoc THEM (VD-M-04): % bi tu choi tren bang %', TG_OP, TG_TABLE_NAME;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_journal_entries_cam_sua BEFORE UPDATE OR DELETE ON journal_entries
    FOR EACH ROW EXECUTE FUNCTION ledger_cam_sua();
CREATE TRIGGER tg_journal_lines_cam_sua BEFORE UPDATE OR DELETE ON journal_lines
    FOR EACH ROW EXECUTE FUNCTION ledger_cam_sua();
CREATE TRIGGER tg_journal_entries_cam_truncate BEFORE TRUNCATE ON journal_entries
    FOR EACH STATEMENT EXECUTE FUNCTION ledger_cam_sua();
CREATE TRIGGER tg_journal_lines_cam_truncate BEFORE TRUNCATE ON journal_lines
    FOR EACH STATEMENT EXECUTE FUNCTION ledger_cam_sua();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tg_journal_lines_cam_truncate ON journal_lines;
DROP TRIGGER IF EXISTS tg_journal_entries_cam_truncate ON journal_entries;
DROP TRIGGER IF EXISTS tg_journal_lines_cam_sua ON journal_lines;
DROP TRIGGER IF EXISTS tg_journal_entries_cam_sua ON journal_entries;
DROP FUNCTION IF EXISTS ledger_cam_sua();
");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "blocked_users");

            migrationBuilder.DropTable(
                name: "holds");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "project_escrows");

            migrationBuilder.DropTable(
                name: "withdrawals");

            migrationBuilder.DropTable(
                name: "journal_entries");
        }
    }
}
