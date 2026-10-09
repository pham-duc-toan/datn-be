using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Link.Api.Entities;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Link.Api.Persistence
{
    /// <summary>DbContext cua link-svc, noi toi link_db (cong 5407).</summary>
    public sealed class LinkDbContext : DbContext
    {
        public LinkDbContext(DbContextOptions<LinkDbContext> options)
            : base(options)
        {
        }

        public DbSet<ShortLink> Links => Set<ShortLink>();

        public DbSet<Campaign> Campaigns => Set<Campaign>();

        public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

        public DbSet<BlockedDomain> BlockedDomains => Set<BlockedDomain>();

        public DbSet<LinkReport> Reports => Set<LinkReport>();

        public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();

        public DbSet<Referral> Referrals => Set<Referral>();

        public DbSet<KnownUser> KnownUsers => Set<KnownUser>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ShortLink>(b =>
            {
                b.ToTable("links");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.Code).HasColumnName("code").HasMaxLength(ShortLink.CotMa).IsRequired();
                b.Property(x => x.DestinationUrl).HasColumnName("destination_url").HasMaxLength(ShortLink.CotUrl).IsRequired();
                b.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(ShortLink.CotTenMien).IsRequired();
                b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(200);
                b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                b.Property(x => x.CampaignId).HasColumnName("campaign_id");
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(ShortLink.CotLyDo);
                b.Property(x => x.CreatorIpHash).HasColumnName("creator_ip_hash").HasMaxLength(64);
                b.Property(x => x.ReportCount).HasColumnName("report_count").IsRequired();
                b.Property(x => x.NeedsReview).HasColumnName("needs_review").IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
                b.Property<uint>("RowVersion").IsRowVersion();
                b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_links_code");
                b.HasIndex(x => new { x.OwnerId, x.CreatedAt }).HasDatabaseName("ix_links_owner");
                b.HasIndex(x => x.CreatedAt).HasFilter("status = 'PendingScan'").HasDatabaseName("ix_links_cho_quet");
                b.HasIndex(x => x.UpdatedAt).HasFilter("needs_review").HasDatabaseName("ix_links_cho_kiem_duyet");
                b.HasIndex(x => x.Domain).HasDatabaseName("ix_links_domain");
            });

            modelBuilder.Entity<Campaign>(b =>
            {
                b.ToTable("campaigns");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.Name).HasColumnName("name").HasMaxLength(Campaign.CotTen).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique().HasDatabaseName("ux_campaigns_owner_name");
            });

            modelBuilder.Entity<ApiKey>(b =>
            {
                b.ToTable("api_keys");
                b.HasKey(x => x.UserId);
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
                b.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(16).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.KeyHash).IsUnique().HasDatabaseName("ux_api_keys_hash");
            });

            modelBuilder.Entity<BlockedDomain>(b =>
            {
                b.ToTable("blocked_domains");
                b.HasKey(x => x.Domain);
                b.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(ShortLink.CotTenMien);
                b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
                b.Property(x => x.CreatedBy).HasColumnName("created_by");
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            });

            modelBuilder.Entity<LinkReport>(b =>
            {
                b.ToTable("link_reports");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.Property(x => x.LinkId).HasColumnName("link_id").IsRequired();
                b.Property(x => x.IpHash).HasColumnName("ip_hash").HasMaxLength(64).IsRequired();
                b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(LinkReport.CotLyDo).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => new { x.LinkId, x.IpHash }).IsUnique().HasDatabaseName("ux_link_reports_link_ip");
            });

            modelBuilder.Entity<ReferralCode>(b =>
            {
                b.ToTable("referral_codes");
                b.HasKey(x => x.UserId);
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.Property(x => x.Code).HasColumnName("code").HasMaxLength(16).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_referral_codes_code");
            });

            modelBuilder.Entity<Referral>(b =>
            {
                b.ToTable("referrals");
                b.HasKey(x => x.ReferredId);
                b.Property(x => x.ReferredId).HasColumnName("referred_id").ValueGeneratedNever();
                b.Property(x => x.ReferrerId).HasColumnName("referrer_id").IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.HasIndex(x => x.ReferrerId).HasDatabaseName("ix_referrals_referrer");
            });

            modelBuilder.Entity<KnownUser>(b =>
            {
                b.ToTable("known_users");
                b.HasKey(x => x.UserId);
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.Property(x => x.RegisteredAt).HasColumnName("registered_at").IsRequired();
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }
    }
}
