using System;
using System.Collections.Generic;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Members;
using Crowd.Annotation.Domain.Projects;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crowd.Annotation.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext cua annotation-svc, noi toi annotation_db (cong 5404).
    ///
    ///   SO HUU:  annotations (+ annotation_history) — nguon su that cua moi nhan.
    ///   BAN SAO: project_terms, project_members_cache — dung tu event.
    ///
    /// Bang outbox nam TRONG database nghiep vu: ghi nhan va ghi event phai cung
    /// MOT transaction Postgres (VD-D-01).
    /// </summary>
    public sealed class AnnotationDbContext : DbContext
    {
        public AnnotationDbContext(DbContextOptions<AnnotationDbContext> options)
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

        public DbSet<LabelAnnotation> Annotations => Set<LabelAnnotation>();

        public DbSet<ProjectTerms> ProjectTerms => Set<ProjectTerms>();

        public DbSet<TaskConsensus> Consensus => Set<TaskConsensus>();

        public DbSet<MemberCache> Members => Set<MemberCache>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LabelAnnotation>(b =>
            {
                b.ToTable("annotations");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.AssignmentId).HasColumnName("assignment_id").IsRequired();
                b.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300);
                b.Property(x => x.SampleContent).HasColumnName("sample_content").HasColumnType("jsonb").HasConversion(RawJsonCotNull);
                b.Property(x => x.SampleMetadata).HasColumnName("sample_metadata").HasColumnType("jsonb").HasConversion(RawJsonCot).IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id");
                // Nhan o dinh dang chung (Crowd.Labeling): ba cot. payload la jsonb —
                // nhan "da hinh" theo loai bai toan (docs muc 3.2), them loai moi
                // khong phai doi bang.
                b.Ignore(x => x.Payload);
                b.Property<string>("_payloadTaskType").HasColumnName("task_type").HasMaxLength(50).IsRequired();
                b.Property<int>("_payloadSchemaVersion").HasColumnName("schema_version").IsRequired();
                b.Property<string>("_payloadJson").HasColumnName("payload").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.SubmittedAt).HasColumnName("submitted_at").IsRequired();
                b.Property(x => x.ReviewerId).HasColumnName("reviewer_id");
                b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
                b.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(LabelAnnotation.CotDbLyDo);
                b.Property(x => x.AppealMessage).HasColumnName("appeal_message").HasMaxLength(LabelAnnotation.CotDbLyDo);
                b.Property(x => x.AppealedAt).HasColumnName("appealed_at");
                b.Property(x => x.AppealResolvedAt).HasColumnName("appeal_resolved_at");
                b.Property(x => x.ConsensusAgrees).HasColumnName("consensus_agrees");
                b.Property(x => x.ConsensusAt).HasColumnName("consensus_at");

                // Duyet va tu choi cung luc (hai reviewer): chi MOT UPDATE khop xmin,
                // ben kia nhan 409 (VD-D-11). Khong the vua Approved vua Rejected.
                b.Property<uint>("RowVersion").IsRowVersion();

                // Nhat ky vong doi: bang con, EF tu nap cung annotation.
                b.OwnsMany(x => x.History, h =>
                {
                    h.ToTable("annotation_history");
                    h.WithOwner().HasForeignKey("annotation_id");
                    h.Property<long>("id").HasColumnName("id").UseIdentityAlwaysColumn();
                    h.HasKey("id");
                    h.Property(x => x.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
                    h.Property(x => x.ActorId).HasColumnName("actor_id");
                    h.Property(x => x.Note).HasColumnName("note").HasMaxLength(LabelAnnotation.CotDbLyDo);
                    h.Property(x => x.At).HasColumnName("at").IsRequired();
                });

                // Moi luot lease thanh DUNG MOT nhan: assignment.submitted giao lai
                // (at-least-once) khong sinh nhan thu hai — lop chan thu hai sau
                // processed_events.
                b.HasIndex(x => x.AssignmentId).IsUnique().HasDatabaseName("ux_annotations_assignment");
                b.HasIndex(x => new { x.ProjectId, x.Status }).HasDatabaseName("ix_annotations_project_status");
                b.HasIndex(x => new { x.ProjectId, x.SampleId }).HasDatabaseName("ix_annotations_project_sample");
                b.HasIndex(x => new { x.LabelerId, x.SubmittedAt }).HasDatabaseName("ix_annotations_labeler");
                b.HasIndex(x => x.Status).HasFilter("status = 'Appealed'").HasDatabaseName("ix_annotations_appeals");

                // GIN tren payload (docs muc 3.2: "JSONB + GIN"): truy van ben trong
                // JSON co index, vd "nhan nao chon lop do":
                //     WHERE payload @> '{"labelIds":["do"]}'
                // jsonb_path_ops: nho hon ban mac dinh, du cho toan tu @>.
                b.HasIndex("_payloadJson")
                    .HasMethod("gin")
                    .HasOperators("jsonb_path_ops")
                    .HasDatabaseName("ix_annotations_payload");
            });

            modelBuilder.Entity<TaskConsensus>(b =>
            {
                b.ToTable("task_consensus");
                b.Property(x => x.TaskId).HasColumnName("task_id").ValueGeneratedNever();
                b.HasKey(x => x.TaskId);
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
                b.Property(x => x.Final).HasColumnName("final").HasColumnType("jsonb").HasConversion(RawJsonCotNull);
                b.Property(x => x.DecidedAt).HasColumnName("decided_at").IsRequired();
                b.HasIndex(x => new { x.ProjectId, x.SampleId }).HasDatabaseName("ix_task_consensus_project_sample");
            });

            modelBuilder.Entity<ProjectTerms>(b =>
            {
                b.ToTable("project_terms");
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.UnitPriceVnd).HasColumnName("unit_price_vnd").IsRequired();
                b.Property(x => x.PlatformFeeVnd).HasColumnName("platform_fee_vnd").IsRequired();
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();

                // Tap nhan dang chuan, chep nguyen tu project.published.
                b.Ignore(x => x.LabelSchema);
                b.Property<string>("_labelSchemaJson").HasColumnName("label_schema").HasColumnType("jsonb").IsRequired();
            });

            modelBuilder.Entity<MemberCache>(b =>
            {
                b.ToTable("project_members_cache");
                b.Property(x => x.ProjectId).HasColumnName("project_id");
                b.Property(x => x.UserId).HasColumnName("user_id");
                b.HasKey(x => new { x.ProjectId, x.UserId });
                b.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }
    }
}
