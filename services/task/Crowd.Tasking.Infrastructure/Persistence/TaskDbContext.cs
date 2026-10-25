using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Labeling;
using Crowd.Settings;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Reports;
using Crowd.Tasking.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crowd.Tasking.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext cua task-svc, noi toi task_db (cong 5403).
    ///
    /// Hai loai bang:
    ///   - SO HUU: tasks (cau hoi), attempts + attempt_answers (bai lam, docs 3.18) — task-svc la
    ///     nguon su that.
    ///   - BAN SAO READ-ONLY: project_snapshots, project_members_cache,
    ///     labeler_cache, gold_samples — dung tu event, KHONG BAO GIO sua tu API.
    /// </summary>
    public sealed class TaskDbContext : DbContext
    {
        public TaskDbContext(DbContextOptions<TaskDbContext> options)
            : base(options)
        {
        }

        /// <summary>RawJson &lt;-&gt; cot jsonb. EF khong goi converter voi null nen "v!" an toan.</summary>
        private static readonly ValueConverter<RawJson, string> RawJsonCot = new ValueConverter<RawJson, string>(
            v => v.Json,
            s => RawJson.Tu(s));

        private static readonly ValueConverter<RawJson?, string> RawJsonCotNull = new ValueConverter<RawJson?, string>(
            v => v!.Json,
            s => RawJson.Tu(s));

        public DbSet<LabelingTask> Tasks => Set<LabelingTask>();

        public DbSet<Attempt> Attempts => Set<Attempt>();

        public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();

        public DbSet<ProjectSnapshot> ProjectSnapshots => Set<ProjectSnapshot>();

        public DbSet<MemberCache> Members => Set<MemberCache>();

        public DbSet<LabelerProfile> Labelers => Set<LabelerProfile>();

        public DbSet<GoldSample> GoldSamples => Set<GoldSample>();

        public DbSet<TaskReport> TaskReports => Set<TaskReport>();

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
            CauHinhBaiLam(modelBuilder);
            CauHinhBanSao(modelBuilder);
            CauHinhBaoLoi(modelBuilder);

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }

        private static void CauHinhTasks(ModelBuilder mb)
        {
            mb.Entity<LabelingTask>(b =>
            {
                // Moi task hoac la file (storage_key) hoac la noi dung (content), khong ca hai.
                b.ToTable("tasks", t => t.HasCheckConstraint(
                    "ck_tasks_file_hoac_noi_dung",
                    "(storage_key IS NULL) <> (content IS NULL)"));
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

                // T-07: bang tasks PHAN VUNG HASH(project_id) 16 phan (migration PhanVungTasks, SQL tay — EF khong
                // tu tao partition). Khoa chinh cua bang phan vung phai chua cot phan vung → (project_id, id).
                // Cau lay task luon loc theo du an nen chi cham MOT phan vung; tra theo id (it gap) di qua ix_tasks_id.
                b.HasKey(x => new { x.ProjectId, x.Id });
                b.HasIndex(x => x.Id).HasDatabaseName("ix_tasks_id");
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();
                b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300);
                b.Property(x => x.Content).HasColumnName("content").HasColumnType("jsonb").HasConversion(RawJsonCotNull);
                b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(RawJsonCot).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.CompletedAt).HasColumnName("completed_at");

                // Mot mau chi thanh MOT task — dataset.ingested giao lai khong nhan doi.
                b.HasIndex(x => new { x.ProjectId, x.SampleId }).IsUnique().HasDatabaseName("ux_tasks_project_sample");

                // Lay de cho bai lam: loc theo du an + trang thai.
                b.HasIndex(x => new { x.ProjectId, x.State, x.Id }).HasDatabaseName("ix_tasks_pool");
            });
        }

        private static void CauHinhBaiLam(ModelBuilder mb)
        {
            mb.Entity<Attempt>(b =>
            {
                b.ToTable("attempts");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
                b.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
                b.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
                b.Property(x => x.EndedAt).HasColumnName("ended_at");

                // Thu tu cau hoi rieng cua bai (uuid[]).
                b.Ignore(x => x.QuestionOrder);
                b.Property<System.Collections.Generic.List<Guid>>("_questionOrder").HasColumnName("question_order").IsRequired();

                b.Property(x => x.GoldTotal).HasColumnName("gold_total").IsRequired();
                b.Property(x => x.GoldCorrect).HasColumnName("gold_correct").IsRequired();
                b.Property(x => x.ClientIp).HasColumnName("client_ip").HasMaxLength(64);
                b.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(100);

                b.Property(x => x.ComparableCount).HasColumnName("comparable_count");
                b.Property(x => x.MatchedCount).HasColumnName("matched_count");
                b.Property(x => x.AgreementPercent).HasColumnName("agreement_percent");
                b.Property(x => x.Passed).HasColumnName("passed");
                b.Property(x => x.Rewarded).HasColumnName("rewarded");
                b.Property(x => x.GradedAt).HasColumnName("graded_at");

                b.Property(x => x.AppealState).HasColumnName("appeal_state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.AppealMessage).HasColumnName("appeal_message").HasMaxLength(Attempt.DoDaiKhieuNaiToiDa);
                b.Property(x => x.AppealedAt).HasColumnName("appealed_at");
                b.Property(x => x.AppealNote).HasColumnName("appeal_note").HasMaxLength(Attempt.DoDaiKhieuNaiToiDa);
                b.Property(x => x.AppealResolvedBy).HasColumnName("appeal_resolved_by");
                b.Property(x => x.AppealResolvedAt).HasColumnName("appeal_resolved_at");

                b.Ignore(x => x.DangLam);
                b.Ignore(x => x.DatCuoiCung);

                // Khoa lac quan: worker het han va nguoi nop cham cung mot dong — ben den sau nhan 0 dong
                // thay vi ghi de (VD-D-11). Moi duong ghi deu khoa FOR UPDATE truoc nen hiem khi gap.
                b.Property<uint>("RowVersion").IsRowVersion();

                // MOT NGUOI MOT BAI moi du an (dang lam hoac da nop). Bai het han / bo thi lam lai duoc.
                b.HasIndex(x => new { x.ProjectId, x.LabelerId })
                    .IsUnique()
                    .HasFilter("state IN ('InProgress','Submitted')")
                    .HasDatabaseName("ux_attempts_project_labeler_active");

                // Dem suat + liet ke bai cua du an.
                b.HasIndex(x => new { x.ProjectId, x.State }).HasDatabaseName("ix_attempts_project_state");

                // "Bai cua toi".
                b.HasIndex(x => new { x.LabelerId, x.StartedAt }).HasDatabaseName("ix_attempts_labeler");

                // Worker tim bai qua han giu.
                b.HasIndex(x => x.ExpiresAt).HasFilter("state = 'InProgress'").HasDatabaseName("ix_attempts_het_han");

                // Hang doi khieu nai cua admin.
                b.HasIndex(x => x.AppealedAt).HasFilter("appeal_state = 'Open'").HasDatabaseName("ix_attempts_khieu_nai_mo");
            });

            mb.Entity<AttemptAnswer>(b =>
            {
                b.ToTable("attempt_answers");
                b.Property(x => x.AttemptId).HasColumnName("attempt_id");
                b.Property(x => x.SampleId).HasColumnName("sample_id");
                b.HasKey(x => new { x.AttemptId, x.SampleId });
                b.Property(x => x.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void CauHinhBaoLoi(ModelBuilder mb)
        {
            mb.Entity<TaskReport>(b =>
            {
                b.ToTable("task_reports");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
                b.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(20).IsRequired();
                b.Property(x => x.Note).HasColumnName("note").HasMaxLength(TaskReport.DoDaiGhiChuToiDa);
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
                b.Property(x => x.ResolvedBy).HasColumnName("resolved_by");

                // Moi bai bao mot cau mot lan.
                b.HasIndex(x => new { x.AttemptId, x.SampleId }).IsUnique().HasDatabaseName("ux_task_reports_attempt_sample");
                b.HasIndex(x => new { x.LabelerId, x.TaskId }).HasDatabaseName("ix_task_reports_labeler_task");
                // Chu du an xem bao loi cua du an; dem so nguoi bao moi task.
                b.HasIndex(x => new { x.ProjectId, x.State, x.CreatedAt }).HasDatabaseName("ix_task_reports_du_an");
                b.HasIndex(x => x.TaskId).HasDatabaseName("ix_task_reports_task");
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
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();

                // Tap nhan dang chuan, chep nguyen tu project.published. null khi chua publish.
                b.Ignore(x => x.LabelSchema);
                b.Property<string?>("_labelSchemaJson").HasColumnName("label_schema").HasColumnType("jsonb");
                b.Property(x => x.RewardVnd).HasColumnName("reward_vnd").IsRequired();
                b.Property(x => x.Slots).HasColumnName("slots").IsRequired();
                b.Property(x => x.MaxParticipants).HasColumnName("max_participants").IsRequired();
                b.Property(x => x.PassThresholdPercent).HasColumnName("pass_threshold_percent").IsRequired();
                b.Property(x => x.QuestionCount).HasColumnName("question_count").IsRequired();
                b.Property(x => x.CollectionFinishedAt).HasColumnName("collection_finished_at");
                b.Property(x => x.FinishReason).HasColumnName("finish_reason").HasMaxLength(20);
                b.Property(x => x.GradedAt).HasColumnName("graded_at");
                b.Property(x => x.AppealDeadline).HasColumnName("appeal_deadline");
                b.Property(x => x.SettledAt).HasColumnName("settled_at");
                b.Property(x => x.PassedCount).HasColumnName("passed_count");
                b.Property(x => x.PausedAt).HasColumnName("paused_at");
                b.Property(x => x.Deadline).HasColumnName("deadline");
                b.Property(x => x.AllowProfessional).HasColumnName("allow_professional").IsRequired();
                b.Property(x => x.IsPrivate).HasColumnName("is_private").IsRequired();
                b.Property(x => x.MinLevel).HasColumnName("min_level");
                b.Property(x => x.MinReputation).HasColumnName("min_reputation");
                b.Property(x => x.SensitiveContent).HasColumnName("sensitive_content").IsRequired().HasDefaultValue(false);
                b.Property(x => x.GoldSetAt).HasColumnName("gold_set_at").IsRequired();

                // Don du lieu van hanh cua du an da dong (project.closed_data_retention).
                b.Property(x => x.FinalProgressJson).HasColumnName("final_progress").HasColumnType("jsonb");
                b.Property(x => x.DataPurgedAt).HasColumnName("data_purged_at");
                b.HasIndex(x => x.StatusChangedAt)
                    .HasFilter("status = 'Closed' AND data_purged_at IS NULL")
                    .HasDatabaseName("ix_project_snapshots_cho_don");

                // Worker: du an het han chot chua dong thu bai; du an da cham cho chot.
                b.HasIndex(x => x.Deadline)
                    .HasFilter("collection_finished_at IS NULL AND is_configured")
                    .HasDatabaseName("ix_project_snapshots_cho_dong_thu_bai");
                b.HasIndex(x => x.AppealDeadline)
                    .HasFilter("graded_at IS NOT NULL AND settled_at IS NULL")
                    .HasDatabaseName("ix_project_snapshots_cho_chot");
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
