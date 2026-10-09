using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Gate.Domain;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crowd.Gate.Infrastructure.Persistence
{
    /// <summary>
    /// gate_db (Postgres, cong 5408): BAN SAO du an / mau / cau vang / link + outbox +
    /// processed_events. Chi duong NGUOI (consumer, worker, nap bo nho dem) cham DB nay;
    /// duong nong /g va /go doc bo nho dem va Redis.
    ///
    /// Vi sao gate co Postgres (thiet ke ban dau chi co Redis + ClickHouse): outbox va
    /// chong xu ly trung can transaction — Redis va ClickHouse khong cho dieu do.
    /// </summary>
    public sealed class GateDbContext : DbContext
    {
        private static readonly ValueConverter<RawJson, string> RawJsonCot = new ValueConverter<RawJson, string>(
            v => v.Json,
            s => RawJson.Tu(s));

        private static readonly ValueConverter<RawJson?, string> RawJsonCotNull = new ValueConverter<RawJson?, string>(
            v => v!.Json,
            s => RawJson.Tu(s));

        public GateDbContext(DbContextOptions<GateDbContext> options)
            : base(options)
        {
        }

        public DbSet<DuAnCong> Projects => Set<DuAnCong>();

        public DbSet<NganSachCong> Budgets => Set<NganSachCong>();

        public DbSet<MauCong> Samples => Set<MauCong>();

        public DbSet<CauVangCong> GoldItems => Set<CauVangCong>();

        public DbSet<LinkCong> Links => Set<LinkCong>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DuAnCong>(b =>
            {
                b.ToTable("gate_projects");
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();
                b.Property(x => x.LabelSchemaJson).HasColumnName("label_schema").HasColumnType("jsonb").IsRequired();
                b.Property(x => x.UnitPriceVnd).HasColumnName("unit_price_vnd").IsRequired();
                b.Property(x => x.PlatformFeeVnd).HasColumnName("platform_fee_vnd").IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            });

            modelBuilder.Entity<NganSachCong>(b =>
            {
                b.ToTable("gate_budgets");
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.Property(x => x.RemainingVnd).HasColumnName("remaining_vnd").IsRequired();
                b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            });

            modelBuilder.Entity<MauCong>(b =>
            {
                b.ToTable("gate_samples");
                b.HasKey(x => x.SampleId);
                b.Property(x => x.SampleId).HasColumnName("sample_id").ValueGeneratedNever();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();
                b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300);
                b.Property(x => x.Content).HasColumnName("content").HasColumnType("jsonb").HasConversion(RawJsonCotNull);
                b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(RawJsonCot).IsRequired();
                b.HasIndex(x => x.ProjectId).HasDatabaseName("ix_gate_samples_project");
            });

            modelBuilder.Entity<CauVangCong>(b =>
            {
                b.ToTable("gate_gold_items");
                b.HasKey(x => x.SampleId);
                b.Property(x => x.SampleId).HasColumnName("sample_id").ValueGeneratedNever();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(20).IsRequired();
                b.Property(x => x.SchemaVersion).HasColumnName("schema_version").IsRequired();
                b.Property(x => x.DataJson).HasColumnName("expected_payload").HasColumnType("jsonb").IsRequired();
                b.HasIndex(x => x.ProjectId).HasDatabaseName("ix_gate_gold_project");
            });

            modelBuilder.Entity<LinkCong>(b =>
            {
                b.ToTable("gate_links");
                b.HasKey(x => x.LinkId);
                b.Property(x => x.LinkId).HasColumnName("link_id").ValueGeneratedNever();
                b.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.DestinationUrl).HasColumnName("destination_url").HasMaxLength(2048).IsRequired();
                b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(200);
                b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                b.Property(x => x.CampaignId).HasColumnName("campaign_id");
                b.Property(x => x.CreatorIpHash).HasColumnName("creator_ip_hash").HasMaxLength(64);
                b.Property(x => x.Active).HasColumnName("active").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_gate_links_code");
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }
    }

    /// <summary>Chi dung cho `dotnet ef migrations`.</summary>
    public sealed class GateDbContextFactory : IDesignTimeDbContextFactory<GateDbContext>
    {
        public GateDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<GateDbContext> b = new DbContextOptionsBuilder<GateDbContext>();
            b.UseNpgsql("Host=localhost;Port=5408;Database=gate_db;Username=gate_user;Password=dev_gate_pw");
            return new GateDbContext(b.Options);
        }
    }
}
