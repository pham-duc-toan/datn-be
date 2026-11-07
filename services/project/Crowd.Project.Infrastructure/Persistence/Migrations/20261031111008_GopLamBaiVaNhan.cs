using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crowd.Project.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GopLamBaiVaNhan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gold_items_samples_sample_id",
                table: "gold_items");

            migrationBuilder.DropIndex(
                name: "IX_gold_items_sample_id",
                table: "gold_items");

            // PHAN VUNG samples: HASH(project_id), 64 phan. EF khong tao partition nen lam bang SQL: doi ten bang cu →
            // tao bang phan vung cung cot (+ excluded_at / excluded_reason) → chep du lieu → bo bang cu. Bang phan vung
            // bat buoc khoa chinh (va moi UNIQUE) chua cot phan vung → (project_id, id). jsonb nen lz4 (nhanh hon pglz) —
            // khai NGAY trong cot truoc khi tao phan vung thi phan vung con moi ke thua (ALTER sau chi doi bang cha).
            migrationBuilder.Sql(@"
ALTER TABLE samples RENAME TO samples_cu;
ALTER TABLE samples_cu RENAME CONSTRAINT ""PK_samples"" TO ""PK_samples_cu"";
ALTER TABLE samples_cu RENAME CONSTRAINT ""FK_samples_datasets_dataset_id"" TO ""FK_samples_cu_datasets"";
ALTER TABLE samples_cu RENAME CONSTRAINT ""FK_samples_projects_project_id"" TO ""FK_samples_cu_projects"";
ALTER TABLE samples_cu RENAME CONSTRAINT ck_samples_file_hoac_noi_dung TO ck_samples_cu_file_hoac_noi_dung;
ALTER INDEX ix_samples_dataset RENAME TO ix_samples_cu_dataset;
ALTER INDEX ux_samples_project_sha256 RENAME TO ux_samples_cu_project_sha256;
CREATE TABLE samples (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    dataset_id uuid NOT NULL,
    storage_key character varying(300) NULL,
    original_name character varying(500) NOT NULL,
    content_type character varying(100) NULL,
    size_bytes bigint NULL,
    sha256 character varying(64) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    modality character varying(20) NOT NULL,
    metadata jsonb COMPRESSION lz4 NOT NULL,
    content jsonb COMPRESSION lz4 NULL,
    media_error character varying(500) NULL,
    media_state character varying(20) NULL,
    excluded_at timestamp with time zone NULL,
    excluded_reason character varying(20) NULL,
    CONSTRAINT ""PK_samples"" PRIMARY KEY (project_id, id),
    CONSTRAINT ck_samples_file_hoac_noi_dung CHECK ((storage_key IS NULL) <> (content IS NULL)),
    CONSTRAINT ""FK_samples_datasets_dataset_id"" FOREIGN KEY (dataset_id) REFERENCES datasets (id) ON DELETE CASCADE,
    CONSTRAINT ""FK_samples_projects_project_id"" FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
) PARTITION BY HASH (project_id);
DO $$
BEGIN
    FOR i IN 0..63 LOOP
        EXECUTE format('CREATE TABLE samples_p%s PARTITION OF samples FOR VALUES WITH (MODULUS 64, REMAINDER %s)', lpad(i::text, 2, '0'), i);
    END LOOP;
END $$;
CREATE UNIQUE INDEX ux_samples_project_sha256 ON samples (project_id, sha256);
CREATE INDEX ix_samples_dataset ON samples (dataset_id);
INSERT INTO samples (id, project_id, dataset_id, storage_key, original_name, content_type, size_bytes, sha256, created_at, modality, metadata, content, media_error, media_state)
SELECT id, project_id, dataset_id, storage_key, original_name, content_type, size_bytes, sha256, created_at, modality, metadata, content, media_error, media_state FROM samples_cu;
DROP TABLE samples_cu;
");

            // PHAN VUNG annotations: bang LON NHAT ve lau dai (moi nhan da nop). HASH(project_id) 64 phan — moi truy van
            // (nop, cham, ket qua, xuat file) deu theo du an nen chi cham MOT phan vung. KHONG chi muc GIN tren payload:
            // khong truy van nao loc ben trong JSON. payload nen lz4.
            migrationBuilder.Sql(@"
CREATE TABLE annotations (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    labeler_id uuid NULL,
    source character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    submitted_at timestamp with time zone NOT NULL,
    decided_at timestamp with time zone NULL,
    payload jsonb COMPRESSION lz4 NOT NULL,
    schema_version integer NOT NULL,
    task_type character varying(50) NOT NULL,
    CONSTRAINT ""PK_annotations"" PRIMARY KEY (project_id, id)
) PARTITION BY HASH (project_id);
DO $$
BEGIN
    FOR i IN 0..63 LOOP
        EXECUTE format('CREATE TABLE annotations_p%s PARTITION OF annotations FOR VALUES WITH (MODULUS 64, REMAINDER %s)', lpad(i::text, 2, '0'), i);
    END LOOP;
END $$;
CREATE INDEX ix_annotations_project_sample ON annotations (project_id, sample_id);
CREATE INDEX ix_annotations_project_status ON annotations (project_id, status);
CREATE UNIQUE INDEX ux_annotations_attempt_sample ON annotations (project_id, attempt_id, sample_id);
");

            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gold_total = table.Column<int>(type: "integer", nullable: false),
                    gold_correct = table.Column<int>(type: "integer", nullable: false),
                    answered_count = table.Column<int>(type: "integer", nullable: true),
                    gold_answers = table.Column<string>(type: "jsonb", nullable: true),
                    client_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    comparable_count = table.Column<int>(type: "integer", nullable: true),
                    matched_count = table.Column<int>(type: "integer", nullable: true),
                    agreement_percent = table.Column<int>(type: "integer", nullable: true),
                    passed = table.Column<bool>(type: "boolean", nullable: true),
                    rewarded = table.Column<bool>(type: "boolean", nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    appeal_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    appeal_resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    appeal_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    question_order = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_attempts_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "labeler_cache",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: true),
                    level_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reputation = table.Column<int>(type: "integer", nullable: true),
                    reputation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labeler_cache", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "project_collections",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_participants = table.Column<int>(type: "integer", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finish_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appeal_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    passed_count = table.Column<int>(type: "integer", nullable: true),
                    final_progress = table.Column<string>(type: "jsonb", nullable: true),
                    data_purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_collections", x => x.project_id);
                    table.ForeignKey(
                        name: "FK_project_collections_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_results",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tools = table.Column<string>(type: "jsonb", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sample_results", x => new { x.project_id, x.sample_id });
                });

            migrationBuilder.CreateTable(
                name: "attempt_drafts",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_drafts", x => new { x.attempt_id, x.sample_id });
                    table.ForeignKey(
                        name: "FK_attempt_drafts_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Duong NONG nhat (luu nhap): chua 30% trang cho HOT update (khong cham chi muc), autovacuum don som vi bang
            // sua lien tuc nhung luon nho (nhap bi xoa luc nop).
            migrationBuilder.Sql("ALTER TABLE attempt_drafts SET (fillfactor = 70, autovacuum_vacuum_scale_factor = 0.01);");

            migrationBuilder.CreateTable(
                name: "attempt_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_attempt_history_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sample_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labeler_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sample_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_sample_reports_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sample_reports_samples_project_id_sample_id",
                        columns: x => new { x.project_id, x.sample_id },
                        principalTable: "samples",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempt_history_attempt",
                table: "attempt_history",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_het_han",
                table: "attempts",
                column: "expires_at",
                filter: "state = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_khieu_nai_mo",
                table: "attempts",
                column: "appealed_at",
                filter: "appeal_state = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_labeler",
                table: "attempts",
                columns: new[] { "labeler_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_project_state",
                table: "attempts",
                columns: new[] { "project_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_attempts_project_labeler_active",
                table: "attempts",
                columns: new[] { "project_id", "labeler_id" },
                unique: true,
                filter: "state IN ('InProgress','Submitted')");

            migrationBuilder.CreateIndex(
                name: "ix_project_collections_cho_chot",
                table: "project_collections",
                column: "appeal_deadline",
                filter: "graded_at IS NOT NULL AND settled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sample_reports_du_an",
                table: "sample_reports",
                columns: new[] { "project_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_reports_mau",
                table: "sample_reports",
                columns: new[] { "project_id", "sample_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sample_reports_attempt_sample",
                table: "sample_reports",
                columns: new[] { "attempt_id", "sample_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_gold_items_samples_project_id_sample_id",
                table: "gold_items",
                columns: new[] { "project_id", "sample_id" },
                principalTable: "samples",
                principalColumns: new[] { "project_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gold_items_samples_project_id_sample_id",
                table: "gold_items");

            migrationBuilder.DropTable(
                name: "annotations");

            migrationBuilder.DropTable(
                name: "attempt_drafts");

            migrationBuilder.DropTable(
                name: "attempt_history");

            migrationBuilder.DropTable(
                name: "labeler_cache");

            migrationBuilder.DropTable(
                name: "project_collections");

            migrationBuilder.DropTable(
                name: "sample_reports");

            migrationBuilder.DropTable(
                name: "sample_results");

            migrationBuilder.DropTable(
                name: "attempts");

            // samples ve bang THUONG (khong phan vung), bo excluded_*.
            migrationBuilder.Sql(@"
ALTER TABLE samples RENAME TO samples_pv;
ALTER TABLE samples_pv RENAME CONSTRAINT ""PK_samples"" TO ""PK_samples_pv"";
ALTER TABLE samples_pv RENAME CONSTRAINT ""FK_samples_datasets_dataset_id"" TO ""FK_samples_pv_datasets"";
ALTER TABLE samples_pv RENAME CONSTRAINT ""FK_samples_projects_project_id"" TO ""FK_samples_pv_projects"";
ALTER TABLE samples_pv RENAME CONSTRAINT ck_samples_file_hoac_noi_dung TO ck_samples_pv_file_hoac_noi_dung;
ALTER INDEX ix_samples_dataset RENAME TO ix_samples_pv_dataset;
ALTER INDEX ux_samples_project_sha256 RENAME TO ux_samples_pv_project_sha256;
CREATE TABLE samples (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    dataset_id uuid NOT NULL,
    storage_key character varying(300) NULL,
    original_name character varying(500) NOT NULL,
    content_type character varying(100) NULL,
    size_bytes bigint NULL,
    sha256 character varying(64) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    modality character varying(20) NOT NULL,
    metadata jsonb NOT NULL,
    content jsonb NULL,
    media_error character varying(500) NULL,
    media_state character varying(20) NULL,
    CONSTRAINT ""PK_samples"" PRIMARY KEY (id),
    CONSTRAINT ck_samples_file_hoac_noi_dung CHECK ((storage_key IS NULL) <> (content IS NULL)),
    CONSTRAINT ""FK_samples_datasets_dataset_id"" FOREIGN KEY (dataset_id) REFERENCES datasets (id) ON DELETE CASCADE,
    CONSTRAINT ""FK_samples_projects_project_id"" FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX ux_samples_project_sha256 ON samples (project_id, sha256);
CREATE INDEX ix_samples_dataset ON samples (dataset_id);
INSERT INTO samples (id, project_id, dataset_id, storage_key, original_name, content_type, size_bytes, sha256, created_at, modality, metadata, content, media_error, media_state)
SELECT id, project_id, dataset_id, storage_key, original_name, content_type, size_bytes, sha256, created_at, modality, metadata, content, media_error, media_state FROM samples_pv;
DROP TABLE samples_pv;
");

            migrationBuilder.CreateIndex(
                name: "IX_gold_items_sample_id",
                table: "gold_items",
                column: "sample_id");

            migrationBuilder.AddForeignKey(
                name: "FK_gold_items_samples_sample_id",
                table: "gold_items",
                column: "sample_id",
                principalTable: "samples",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
