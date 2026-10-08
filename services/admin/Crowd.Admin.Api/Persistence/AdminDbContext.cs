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

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }
}
