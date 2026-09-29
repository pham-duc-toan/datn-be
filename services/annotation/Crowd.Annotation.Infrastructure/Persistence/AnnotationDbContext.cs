using System;
using System.Collections.Generic;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Members;
using Crowd.Annotation.Domain.Projects;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

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

        public DbSet<LabelAnnotation> Annotations => Set<LabelAnnotation>();

        public DbSet<ProjectTerms> ProjectTerms => Set<ProjectTerms>();

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
                b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id");
                b.Ignore(x => x.Labels);
                b.Property<List<string>>("_labels").HasColumnName("labels").IsRequired();
                b.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.SubmittedAt).HasColumnName("submitted_at").IsRequired();
                b.Property(x => x.ReviewerId).HasColumnName("reviewer_id");
                b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
                b.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(LabelAnnotation.DoDaiLyDoToiDa);
                b.Property(x => x.AppealMessage).HasColumnName("appeal_message").HasMaxLength(LabelAnnotation.DoDaiLyDoToiDa);
                b.Property(x => x.AppealedAt).HasColumnName("appealed_at");
                b.Property(x => x.AppealResolvedAt).HasColumnName("appeal_resolved_at");

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
                    h.Property(x => x.Note).HasColumnName("note").HasMaxLength(LabelAnnotation.DoDaiLyDoToiDa);
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
            });

            modelBuilder.Entity<ProjectTerms>(b =>
            {
                b.ToTable("project_terms");
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.UnitPriceVnd).HasColumnName("unit_price_vnd").IsRequired();
                b.Property(x => x.PlatformFeeVnd).HasColumnName("platform_fee_vnd").IsRequired();
                b.Property(x => x.AllowMultiple).HasColumnName("allow_multiple").IsRequired();
                b.Ignore(x => x.LabelClasses);
                b.Property<List<string>>("_labelClasses").HasColumnName("label_classes").IsRequired();
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
        }
    }
}
