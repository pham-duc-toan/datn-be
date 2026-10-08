using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence.Configurations;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Persistence
{
    /// <summary>
    /// DbContext cua identity-svc, noi toi identity_db (cong 5401).
    ///
    /// identity-svc la service 2 project, nen DbContext nam thang trong Api —
    /// khong co tang Infrastructure rieng.
    /// </summary>
    public sealed class IdentityDbContext : DbContext
    {
        public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();

        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            // Nghiep vu cua rieng identity-svc
            modelBuilder.ApplyConfiguration(new UserConfiguration());
            modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());

            // Ha tang dung chung — giong het annotation-svc
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
            modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration());
        }
    }
}
