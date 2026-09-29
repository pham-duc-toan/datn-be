using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Payment.Infrastructure.Persistence
{
    /// <summary>DbContext cua payment-svc, noi toi payment_db (cong 5406).</summary>
    public sealed class PaymentDbContext : DbContext
    {
        public PaymentDbContext(DbContextOptions<PaymentDbContext> options)
            : base(options)
        {
        }

        public DbSet<PaymentIntent> Intents => Set<PaymentIntent>();

        public DbSet<Payout> Payouts => Set<Payout>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PaymentIntent>(b =>
            {
                b.ToTable("payment_intents");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.BusinessId).HasColumnName("business_id").IsRequired();
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd").IsRequired();
                b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
                b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.ProviderTxnId).HasColumnName("provider_txn_id").HasMaxLength(100);
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.CompletedAt).HasColumnName("completed_at");
                b.Property<uint>("RowVersion").IsRowVersion();

                b.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_intents_idempotency");

                // Webhook den 3 lan chi ghi MOT lan (docs 3.3).
                b.HasIndex(x => new { x.Provider, x.ProviderTxnId })
                    .IsUnique()
                    .HasFilter("provider_txn_id IS NOT NULL")
                    .HasDatabaseName("ux_intents_provider_txn");
            });

            modelBuilder.Entity<Payout>(b =>
            {
                b.ToTable("payouts");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.NetVnd).HasColumnName("net_vnd").IsRequired();
                b.Property(x => x.BankAccount).HasColumnName("bank_account").HasMaxLength(100).IsRequired();
                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
                b.Property(x => x.ProviderTxnId).HasColumnName("provider_txn_id").HasMaxLength(100);
                b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.SentAt).HasColumnName("sent_at");
                b.Property(x => x.CompletedAt).HasColumnName("completed_at");
                b.HasIndex(x => x.Status).HasFilter("status IN ('Requested','Sending')").HasDatabaseName("ix_payouts_dang_xu_ly");
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }

    /// <summary>CHI phuc vu `dotnet ef` sinh migration.</summary>
    public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
    {
        public PaymentDbContext CreateDbContext(string[] args)
        {
            string? tuBienMoiTruong = Environment.GetEnvironmentVariable("PAYMENT_DB");
            string chuoiKetNoi;
            if (string.IsNullOrWhiteSpace(tuBienMoiTruong))
            {
                chuoiKetNoi = "Host=localhost;Port=5406;Database=payment_db;Username=payment_user;Password=dev_payment_pw";
            }
            else
            {
                chuoiKetNoi = tuBienMoiTruong;
            }

            DbContextOptionsBuilder<PaymentDbContext> builder = new DbContextOptionsBuilder<PaymentDbContext>();
            builder.UseNpgsql(chuoiKetNoi);
            return new PaymentDbContext(builder.Options);
        }
    }
}
