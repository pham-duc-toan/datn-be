using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Notification.Api.Entities;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Persistence
{
    /// <summary>
    /// notification_db (cong 5410). Service 2 project — DbContext nam thang trong Api.
    /// Outbox: chua phat event nao, nhung AddCrowdSettings (xin snapshot setting) can.
    /// </summary>
    public sealed class NotificationDbContext : DbContext
    {
        public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ThongBao> ThongBaos => Set<ThongBao>();

        public DbSet<LienHe> LienHes => Set<LienHe>();

        public DbSet<TuyChonEmail> TuyChonEmails => Set<TuyChonEmail>();

        public DbSet<ChuDuAn> ChuDuAns => Set<ChuDuAn>();

        public DbSet<EmailGiaoDich> EmailGiaoDichs => Set<EmailGiaoDich>();

        public DbSet<ChuLenhRut> ChuLenhRuts => Set<ChuLenhRut>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ThongBao>(b =>
            {
                b.ToTable("notifications");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
                b.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
                b.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
                b.Property(x => x.Link).HasColumnName("link").HasMaxLength(300);
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.ReadAt).HasColumnName("read_at");
                b.Property(x => x.EmailState).HasColumnName("email_state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.EmailAttempts).HasColumnName("email_attempts").IsRequired();
                b.Property(x => x.EmailNextAt).HasColumnName("email_next_at");
                b.Property(x => x.EmailSentAt).HasColumnName("email_sent_at");
                b.Property(x => x.EmailError).HasColumnName("email_error").HasMaxLength(500);
                b.Property(x => x.MergedCount).HasColumnName("merged_count").HasDefaultValue(1).IsRequired();
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd");

                // Danh sach cua nguoi dung (moi nhat truoc) + dem chua doc.
                b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_notifications_user");
                b.HasIndex(x => x.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_chua_doc");
                // EmailWorker: chi quet dong dang cho gui.
                b.HasIndex(x => x.EmailNextAt).HasFilter("email_state = 'Pending'").HasDatabaseName("ix_notifications_email_cho");
            });

            modelBuilder.Entity<LienHe>(b =>
            {
                b.ToTable("contacts");
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.HasKey(x => x.UserId);
                b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
                b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
                b.Ignore(x => x.Roles);
                b.Property<List<string>>("_roles").HasColumnName("roles").IsRequired();
            });

            modelBuilder.Entity<EmailGiaoDich>(b =>
            {
                b.ToTable("transactional_emails");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
                b.Property(x => x.ToEmail).HasColumnName("to_email").HasMaxLength(320).IsRequired();
                b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
                b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
                b.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
                b.Property(x => x.NextAt).HasColumnName("next_at");
                b.Property(x => x.SentAt).HasColumnName("sent_at");
                b.Property(x => x.Error).HasColumnName("error").HasMaxLength(500);
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.NextAt).HasFilter("state = 'Pending'").HasDatabaseName("ix_transactional_emails_cho");
            });

            modelBuilder.Entity<TuyChonEmail>(b =>
            {
                b.ToTable("email_preferences");
                b.Property(x => x.UserId).HasColumnName("user_id");
                b.Property(x => x.Type).HasColumnName("type").HasMaxLength(40);
                b.HasKey(x => new { x.UserId, x.Type });
                b.Property(x => x.EmailEnabled).HasColumnName("email_enabled").IsRequired();
            });

            modelBuilder.Entity<ChuDuAn>(b =>
            {
                b.ToTable("project_owners");
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
            });

            modelBuilder.Entity<ChuLenhRut>(b =>
            {
                b.ToTable("withdrawal_owners");
                b.Property(x => x.WithdrawalId).HasColumnName("withdrawal_id").ValueGeneratedNever();
                b.HasKey(x => x.WithdrawalId);
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.NetAmountVnd).HasColumnName("net_amount_vnd").IsRequired();
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }
    }
}
