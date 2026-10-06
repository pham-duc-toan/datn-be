using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext cua task-svc, noi toi task_db (cong 5403).
    ///
    /// Hai loai bang:
    ///   - SO HUU: tasks, assignments — task-svc la nguon su that.
    ///   - BAN SAO READ-ONLY: project_snapshots, project_members_cache,
    ///     labeler_cache, gold_samples — dung tu event, KHONG BAO GIO sua tu API.
    /// </summary>
    public sealed class TaskDbContext : DbContext
    {
        public TaskDbContext(DbContextOptions<TaskDbContext> options)
            : base(options)
        {
        }

        public DbSet<LabelingTask> Tasks => Set<LabelingTask>();

        public DbSet<Assignment> Assignments => Set<Assignment>();

        public DbSet<ProjectSnapshot> ProjectSnapshots => Set<ProjectSnapshot>();

        public DbSet<MemberCache> Members => Set<MemberCache>();

        public DbSet<LabelerProfile> Labelers => Set<LabelerProfile>();

        public DbSet<GoldSample> GoldSamples => Set<GoldSample>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            CauHinhTasks(modelBuilder);
            CauHinhAssignments(modelBuilder);
            CauHinhBanSao(modelBuilder);

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }

        private static void CauHinhTasks(ModelBuilder mb)
        {
            mb.Entity<LabelingTask>(b =>
            {
                b.ToTable("tasks");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.RedundancyTarget).HasColumnName("redundancy_target").IsRequired();
                b.Property(x => x.ActiveLeaseCount).HasColumnName("active_lease_count").IsRequired();
                b.Property(x => x.SubmittedCount).HasColumnName("submitted_count").IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.CompletedAt).HasColumnName("completed_at");

                // Mot mau chi thanh MOT task — dataset.ingested giao lai khong nhan doi.
                b.HasIndex(x => new { x.ProjectId, x.SampleId }).IsUnique().HasDatabaseName("ux_tasks_project_sample");

                // Index cho cau chon ung vien: loc theo du an + trang thai, sap theo id.
                b.HasIndex(x => new { x.ProjectId, x.State, x.Id }).HasDatabaseName("ix_tasks_pool");
            });
        }

        private static void CauHinhAssignments(ModelBuilder mb)
        {
            mb.Entity<Assignment>(b =>
            {
                // Ba cot nhan di cung nhau: hoac ca ba null (chua nop) hoac ca ba
                // co gia tri, va CHI luot da nop moi co nhan. Database tu chan
                // trang thai nua voi, khong chi trong cay vao code.
                b.ToTable("assignments", t => t.HasCheckConstraint(
                    "ck_assignments_payload",
                    "(task_type IS NULL) = (payload IS NULL) " +
                    "AND (schema_version IS NULL) = (payload IS NULL) " +
                    "AND (state = 'Submitted') = (payload IS NOT NULL)"));
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.LeasedAt).HasColumnName("leased_at").IsRequired();
                b.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
                b.Property(x => x.EndedAt).HasColumnName("ended_at");

                // Nhan da nop o dinh dang chung (Crowd.Labeling): ba cot, null khi
                // chua nop. payload la jsonb — moi loai nhan mot hinh dang.
                b.Ignore(x => x.Payload);
                b.Property<string?>("_payloadTaskType").HasColumnName("task_type").HasMaxLength(50);
                b.Property<int?>("_payloadSchemaVersion").HasColumnName("schema_version");
                b.Property<string?>("_payloadJson").HasColumnName("payload").HasColumnType("jsonb");

                // Khoa lac quan: reaper va nguoi nop cham cung mot dong — ben den sau
                // nhan 0 dong thay vi ghi de (VD-D-11).
                b.Property<uint>("RowVersion").IsRowVersion();

                b.HasOne<LabelingTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);

                // MOT NGUOI KHONG GAN CUNG MOT TASK HAI LAN — neu khong redundancy=3
                // co the la cung mot nguoi 3 lan, dong thuan gia. Chi tinh luot dang
                // giu hoac da nop; bo qua/het han thi duoc nhan lai.
                b.HasIndex(x => new { x.TaskId, x.LabelerId })
                    .IsUnique()
                    .HasFilter("state IN ('Leased','Submitted')")
                    .HasDatabaseName("ux_assignments_task_labeler_active");

                // Moi labeler giu toi da MOT task moi du an cung luc.
                b.HasIndex(x => new { x.ProjectId, x.LabelerId })
                    .IsUnique()
                    .HasFilter("state = 'Leased'")
                    .HasDatabaseName("ux_assignments_one_lease_per_project");

                // Reaper tim lease qua han.
                b.HasIndex(x => x.ExpiresAt).HasFilter("state = 'Leased'").HasDatabaseName("ix_assignments_reaper");
            });
        }

        private static void CauHinhBanSao(ModelBuilder mb)
        {
            mb.Entity<ProjectSnapshot>(b =>
            {
                b.ToTable("project_snapshots");
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.OwnerId).HasColumnName("owner_id");
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.StatusChangedAt).HasColumnName("status_changed_at").IsRequired();
                b.Property(x => x.IsConfigured).HasColumnName("is_configured").IsRequired();
                b.Property(x => x.LabelTaskType).HasColumnName("label_task_type").HasMaxLength(50).IsRequired();
                b.Ignore(x => x.LabelClasses);
                b.Property<List<string>>("_labelClasses").HasColumnName("label_classes").IsRequired();
                b.Property(x => x.AllowMultiple).HasColumnName("allow_multiple").IsRequired();
                b.Property(x => x.UnitPriceVnd).HasColumnName("unit_price_vnd").IsRequired();
                b.Property(x => x.Redundancy).HasColumnName("redundancy").IsRequired();
                b.Property(x => x.Deadline).HasColumnName("deadline");
                b.Property(x => x.AllowProfessional).HasColumnName("allow_professional").IsRequired();
                b.Property(x => x.IsPrivate).HasColumnName("is_private").IsRequired();
                b.Property(x => x.MinLevel).HasColumnName("min_level");
                b.Property(x => x.MinReputation).HasColumnName("min_reputation");
                b.Property(x => x.GoldSetAt).HasColumnName("gold_set_at").IsRequired();
            });

            mb.Entity<MemberCache>(b =>
            {
                b.ToTable("project_members_cache");
                b.Property(x => x.ProjectId).HasColumnName("project_id");
                b.Property(x => x.UserId).HasColumnName("user_id");
                b.HasKey(x => new { x.ProjectId, x.UserId });
                b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            });

            mb.Entity<LabelerProfile>(b =>
            {
                b.ToTable("labeler_cache");
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.HasKey(x => x.UserId);
                b.Property(x => x.Level).HasColumnName("level");
                b.Property(x => x.LevelAt).HasColumnName("level_at").IsRequired();
                b.Property(x => x.Reputation).HasColumnName("reputation");
                b.Property(x => x.ReputationAt).HasColumnName("reputation_at").IsRequired();
                b.Property(x => x.Blocked).HasColumnName("blocked").IsRequired();
                b.Property(x => x.BlockedAt).HasColumnName("blocked_at").IsRequired();
            });

            mb.Entity<GoldSample>(b =>
            {
                b.ToTable("gold_samples");
                b.Property(x => x.ProjectId).HasColumnName("project_id");
                b.Property(x => x.SampleId).HasColumnName("sample_id");
                b.HasKey(x => new { x.ProjectId, x.SampleId });
                b.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(20).IsRequired();
                b.Ignore(x => x.ExpectedPayload);
                b.Property<string>("_expectedTaskType").HasColumnName("expected_task_type").HasMaxLength(50).IsRequired();
                b.Property<int>("_expectedSchemaVersion").HasColumnName("expected_schema_version").IsRequired();
                b.Property<string>("_expectedPayloadJson").HasColumnName("expected_payload").HasColumnType("jsonb").IsRequired();
            });
        }
    }
}
