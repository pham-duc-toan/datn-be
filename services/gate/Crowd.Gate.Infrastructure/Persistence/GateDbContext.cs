using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Gate.Infrastructure.Persistence
{
    /// <summary>
    /// gate_db (Postgres, cong 5408): chi outbox + processed_events + settings_replica. Du lieu cong link (du an, mau,
    /// cau vang, ngan sach, link) KHONG con chep ve day — GateCatalog doc thang view v_gate_* cua project / ledger /
    /// link (kien truc 3.32) va giu trong bo nho; duong nong /g, /go doc bo nho va Redis.
    ///
    /// Vi sao gate van co Postgres: outbox va chong xu ly trung can transaction — Redis va ClickHouse khong cho dieu do.
    /// </summary>
    public sealed class GateDbContext : DbContext
    {
        public GateDbContext(DbContextOptions<GateDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
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
