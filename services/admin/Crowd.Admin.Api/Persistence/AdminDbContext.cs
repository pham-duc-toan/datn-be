using System;
using Crowd.Admin.Api.Entities;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Persistence
{
    /// <summary>DbContext cua admin-svc, noi toi admin_db (cong 5411).</summary>
    public sealed class AdminDbContext : DbContext
    {
        public AdminDbContext(DbContextOptions<AdminDbContext> options)
            : base(options)
        {
        }

        public DbSet<Setting> Settings => Set<Setting>();

        public DbSet<SettingHistory> SettingHistory => Set<SettingHistory>();

        public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

        public DbSet<ThongKeNgay> ThongKeNgay => Set<ThongKeNgay>();

        public DbSet<Announcement> Announcements => Set<Announcement>();

        public DbSet<DonDuyet> DonDuyets => Set<DonDuyet>();

        public DbSet<MauBaiToan> MauBaiToans => Set<MauBaiToan>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Setting>(b =>
            {
                b.ToTable("settings");
                b.HasKey(x => x.Key);
                b.Property(x => x.Key).HasColumnName("key").HasMaxLength(100);
                b.Property(x => x.ValueJson).HasColumnName("value").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.Version).HasColumnName("version").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            });

            modelBuilder.Entity<SettingHistory>(b =>
            {
                b.ToTable("setting_history");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                b.Property(x => x.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
                b.Property(x => x.OldValueJson).HasColumnName("old_value").HasColumnType("jsonb");
                b.Property(x => x.NewValueJson).HasColumnName("new_value").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.Version).HasColumnName("version").IsRequired();
                b.Property(x => x.ChangedBy).HasColumnName("changed_by");
                b.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
                b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
                b.HasIndex(x => new { x.Key, x.Version }).IsUnique().HasDatabaseName("ux_setting_history_key_version");
            });

            modelBuilder.Entity<AuditEvent>(b =>
            {
                b.ToTable("audit_events");
                b.HasKey(x => x.EventId);
                b.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
                b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
                b.Property(x => x.Producer).HasColumnName("producer").HasMaxLength(50).IsRequired();
                b.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
                b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
                b.Property(x => x.CausationId).HasColumnName("causation_id");
                b.Property(x => x.ActorId).HasColumnName("actor_id");
                b.Property(x => x.ActorRole).HasColumnName("actor_role").HasMaxLength(20);
                b.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();

                // Tra cuu: moi nhat truoc; theo nguoi lam; theo loai (thong ke); theo chuoi thao tac.
                b.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_audit_thoi_gian");
                b.HasIndex(x => new { x.ActorId, x.OccurredAt }).HasDatabaseName("ix_audit_nguoi_lam");
                b.HasIndex(x => new { x.EventType, x.OccurredAt }).HasDatabaseName("ix_audit_loai");
                b.HasIndex(x => x.CorrelationId).HasDatabaseName("ix_audit_correlation");
            });

            modelBuilder.Entity<ThongKeNgay>(b =>
            {
                b.ToTable("stats_daily");
                b.HasKey(x => new { x.Day, x.EventType });
                b.Property(x => x.Day).HasColumnName("day");
                b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100);
                b.Property(x => x.Count).HasColumnName("count").IsRequired();
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd").IsRequired();
                b.Property(x => x.FeeVnd).HasColumnName("fee_vnd").IsRequired();
            });

            modelBuilder.Entity<AuditDaDem>(b =>
            {
                b.ToTable("audit_counted");
                b.HasKey(x => x.EventId);
                b.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
                b.Property(x => x.SeenAt).HasColumnName("seen_at").IsRequired();
                b.HasIndex(x => x.SeenAt).HasDatabaseName("ix_audit_counted_thoi_diem");
            });

            modelBuilder.Entity<Announcement>(b =>
            {
                b.ToTable("announcements");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.Title).HasColumnName("title").HasMaxLength(Announcement.DoDaiTieuDeToiDa).IsRequired();
                b.Property(x => x.Body).HasColumnName("body").HasMaxLength(Announcement.DoDaiNoiDungToiDa).IsRequired();
                b.Property(x => x.Audience).HasColumnName("audience").HasMaxLength(20).IsRequired();
                b.Property(x => x.SendEmail).HasColumnName("send_email").IsRequired();
                b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_announcements_thoi_gian");
            });

            modelBuilder.Entity<DonDuyet>(b =>
            {
                b.ToTable("approval_requests");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
                b.Property(x => x.SubjectId).HasColumnName("subject_id").IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
                b.Property(x => x.Title).HasColumnName("title").HasMaxLength(300);
                b.Property(x => x.RequesterId).HasColumnName("requester_id");
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd");
                b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
                b.Property(x => x.OpenedAt).HasColumnName("opened_at").IsRequired();
                b.Property(x => x.DecidedAt).HasColumnName("decided_at");
                b.Property(x => x.DecidedBy).HasColumnName("decided_by");
                b.Property(x => x.LastEventAt).HasColumnName("last_event_at").IsRequired();
                b.HasIndex(x => new { x.Kind, x.SubjectId }).IsUnique().HasDatabaseName("ux_approval_requests_don");
                b.HasIndex(x => new { x.Status, x.OpenedAt }).HasDatabaseName("ix_approval_requests_hang_doi");
            });

            modelBuilder.Entity<MauBaiToan>(b =>
            {
                b.ToTable("workspace_templates");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.Code).HasColumnName("code").HasMaxLength(MauBaiToan.DoDaiMaToiDa).IsRequired();
                b.Property(x => x.Name).HasColumnName("name").HasMaxLength(MauBaiToan.DoDaiTenToiDa).IsRequired();
                b.Property(x => x.Description).HasColumnName("description").HasMaxLength(MauBaiToan.DoDaiMoTaToiDa).IsRequired();
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();
                b.Property(x => x.LabelSchemaJson).HasColumnName("label_schema").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.GuidelineMarkdown).HasColumnName("guideline_markdown").IsRequired();
                b.Property(x => x.SuggestedUnitPriceVnd).HasColumnName("suggested_unit_price_vnd");
                b.Property(x => x.SuggestedRedundancy).HasColumnName("suggested_redundancy");
                b.Property(x => x.Active).HasColumnName("active").IsRequired();
                b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.Property(x => x.UpdatedBy).HasColumnName("updated_by").IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_workspace_templates_code");
                b.HasIndex(x => new { x.Active, x.Modality }).HasDatabaseName("ix_workspace_templates_loai");
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }
}
