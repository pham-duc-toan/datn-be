using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Reco.Infrastructure.Persistence
{
    /// <summary>
    /// Bang GOC bo dem so thich cua mot labeler (Redis aff:{userId} chi la ban doc nhanh, mat thi nap lai tu day).
    /// Gia tri a, b la so THO da quen toi updated_at; lan cong sau quen tiep roi moi cong (cong thuc trong SQL BoDemStore).
    /// </summary>
    public sealed class LabelerAffinity
    {
        public Guid UserId { get; set; }

        public string TagKind { get; set; } = string.Empty;

        public string TagCode { get; set; } = string.Empty;

        public double A { get; set; }

        public double B { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>Labeler bam "Khong quan tam" tren the cho — du an do khong bao gio goi y lai cho nguoi nay.</summary>
    public sealed class HiddenProject
    {
        public Guid UserId { get; set; }

        public Guid ProjectId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Lich su mo hinh reco-svc da nap (production / candidate) — doi chieu A/B, truy vet.</summary>
    public sealed class ModelVersion
    {
        public string Version { get; set; } = string.Empty;

        /// <summary>formula | logistic | lightgbm</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>production | candidate</summary>
        public string Slot { get; set; } = string.Empty;

        public string FileKey { get; set; } = string.Empty;

        public DateTimeOffset LoadedAt { get; set; }
    }

    /// <summary>
    /// reco_db — DB rieng cua reco-svc (kien truc 3.39). KHONG co bang sao du lieu du an: du an doc thang view
    /// v_reco_* cua project-svc (role reco_reader), nap RAM moi 30 giay.
    /// </summary>
    public sealed class RecoDbContext : DbContext
    {
        public RecoDbContext(DbContextOptions<RecoDbContext> options)
            : base(options)
        {
        }

        public DbSet<LabelerAffinity> Affinities => Set<LabelerAffinity>();

        public DbSet<HiddenProject> HiddenProjects => Set<HiddenProject>();

        public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LabelerAffinity>(b =>
            {
                b.ToTable("labeler_affinity");
                b.HasKey(x => new { x.UserId, x.TagKind, x.TagCode });
                b.Property(x => x.UserId).HasColumnName("user_id");
                b.Property(x => x.TagKind).HasColumnName("tag_kind").HasMaxLength(20).IsRequired();
                b.Property(x => x.TagCode).HasColumnName("tag_code").HasMaxLength(64).IsRequired();
                b.Property(x => x.A).HasColumnName("a").IsRequired();
                b.Property(x => x.B).HasColumnName("b").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            });

            modelBuilder.Entity<HiddenProject>(b =>
            {
                b.ToTable("hidden_projects");
                b.HasKey(x => new { x.UserId, x.ProjectId });
                b.Property(x => x.UserId).HasColumnName("user_id");
                b.Property(x => x.ProjectId).HasColumnName("project_id");
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            });

            modelBuilder.Entity<ModelVersion>(b =>
            {
                b.ToTable("model_versions");
                b.HasKey(x => new { x.Version, x.Slot });
                b.Property(x => x.Version).HasColumnName("version").HasMaxLength(100);
                b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
                b.Property(x => x.Slot).HasColumnName("slot").HasMaxLength(20);
                b.Property(x => x.FileKey).HasColumnName("file_key").HasMaxLength(300).IsRequired();
                b.Property(x => x.LoadedAt).HasColumnName("loaded_at").IsRequired();
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }

    public sealed class RecoDbContextFactory : IDesignTimeDbContextFactory<RecoDbContext>
    {
        public RecoDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<RecoDbContext> b = new DbContextOptionsBuilder<RecoDbContext>();
            b.UseNpgsql("Host=localhost;Port=5412;Database=reco_db;Username=reco_user;Password=dev_reco_pw");
            return new RecoDbContext(b.Options);
        }
    }
}
