using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crowd.Tasking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhanVungTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_assignments_tasks_task_id",
                table: "assignments");

            // T-07: dung lai tasks thanh bang PHAN VUNG HASH(project_id), 16 phan. EF khong tao partition nen
            // lam bang SQL: doi ten bang cu → tao bang phan vung cung cot → chep du lieu → bo bang cu.
            // Khoa chinh (project_id, id): bang phan vung bat buoc khoa chua cot phan vung.
            migrationBuilder.Sql(@"
ALTER TABLE tasks RENAME TO tasks_cu;
ALTER TABLE tasks_cu RENAME CONSTRAINT ""PK_tasks"" TO ""PK_tasks_cu"";
ALTER INDEX ux_tasks_project_sample RENAME TO ux_tasks_cu_project_sample;
ALTER INDEX ix_tasks_pool RENAME TO ix_tasks_cu_pool;

CREATE TABLE tasks (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    storage_key character varying(300) NULL,
    state character varying(20) NOT NULL,
    redundancy_target integer NOT NULL,
    active_lease_count integer NOT NULL,
    submitted_count integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone NULL,
    modality character varying(20) NOT NULL,
    metadata jsonb NOT NULL,
    content jsonb NULL,
    CONSTRAINT ""PK_tasks"" PRIMARY KEY (project_id, id),
    CONSTRAINT ck_tasks_file_hoac_noi_dung CHECK ((storage_key IS NULL) <> (content IS NULL))
) PARTITION BY HASH (project_id);

DO $$
BEGIN
    FOR i IN 0..15 LOOP
        EXECUTE format('CREATE TABLE tasks_p%s PARTITION OF tasks FOR VALUES WITH (MODULUS 16, REMAINDER %s)', lpad(i::text, 2, '0'), i);
    END LOOP;
END $$;

CREATE UNIQUE INDEX ux_tasks_project_sample ON tasks (project_id, sample_id);
CREATE INDEX ix_tasks_pool ON tasks (project_id, state, id);
CREATE INDEX ix_tasks_id ON tasks (id);

INSERT INTO tasks (id, project_id, sample_id, storage_key, state, redundancy_target, active_lease_count, submitted_count, created_at, completed_at, modality, metadata, content)
SELECT id, project_id, sample_id, storage_key, state, redundancy_target, active_lease_count, submitted_count, created_at, completed_at, modality, metadata, content FROM tasks_cu;
DROP TABLE tasks_cu;
");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_project_id_task_id",
                table: "assignments",
                columns: new[] { "project_id", "task_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_assignments_tasks_project_id_task_id",
                table: "assignments",
                columns: new[] { "project_id", "task_id" },
                principalTable: "tasks",
                principalColumns: new[] { "project_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_assignments_tasks_project_id_task_id",
                table: "assignments");

            migrationBuilder.DropIndex(
                name: "IX_assignments_project_id_task_id",
                table: "assignments");

            migrationBuilder.Sql(@"
ALTER TABLE tasks RENAME TO tasks_pv;
ALTER TABLE tasks_pv RENAME CONSTRAINT ""PK_tasks"" TO ""PK_tasks_pv"";
ALTER INDEX ux_tasks_project_sample RENAME TO ux_tasks_pv_project_sample;
ALTER INDEX ix_tasks_pool RENAME TO ix_tasks_pv_pool;
ALTER INDEX ix_tasks_id RENAME TO ix_tasks_pv_id;

CREATE TABLE tasks (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    storage_key character varying(300) NULL,
    state character varying(20) NOT NULL,
    redundancy_target integer NOT NULL,
    active_lease_count integer NOT NULL,
    submitted_count integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone NULL,
    modality character varying(20) NOT NULL,
    metadata jsonb NOT NULL,
    content jsonb NULL,
    CONSTRAINT ""PK_tasks"" PRIMARY KEY (id),
    CONSTRAINT ck_tasks_file_hoac_noi_dung CHECK ((storage_key IS NULL) <> (content IS NULL))
);
CREATE UNIQUE INDEX ux_tasks_project_sample ON tasks (project_id, sample_id);
CREATE INDEX ix_tasks_pool ON tasks (project_id, state, id);

INSERT INTO tasks (id, project_id, sample_id, storage_key, state, redundancy_target, active_lease_count, submitted_count, created_at, completed_at, modality, metadata, content)
SELECT id, project_id, sample_id, storage_key, state, redundancy_target, active_lease_count, submitted_count, created_at, completed_at, modality, metadata, content FROM tasks_pv;
DROP TABLE tasks_pv;
");

            migrationBuilder.AddForeignKey(
                name: "FK_assignments_tasks_task_id",
                table: "assignments",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
