using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext cua ledger-svc, noi toi ledger_db (cong 5405) — database DUY NHAT
    /// cham vao so du (docs 3.3).
    /// </summary>
    public sealed class LedgerDbContext : DbContext
    {
        public LedgerDbContext(DbContextOptions<LedgerDbContext> options)
            : base(options)
        {
        }

        public DbSet<Account> Accounts => Set<Account>();

        public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

        public DbSet<FundsHold> Holds => Set<FundsHold>();

        public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();

        public DbSet<ProjectEscrow> Escrows => Set<ProjectEscrow>();

        public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Account>(b =>
            {
                b.ToTable("accounts");
                b.Property(x => x.Code).HasColumnName("code").HasMaxLength(120);
                b.HasKey(x => x.Code);
                b.Property(x => x.Balance).HasColumnName("balance").IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            });

            modelBuilder.Entity<JournalEntry>(b =>
            {
                b.ToTable("journal_entries");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.Seq).HasColumnName("seq").UseIdentityAlwaysColumn();
                b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40).IsRequired();
                b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(120).IsRequired();
                b.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.PrevHash).HasColumnName("prev_hash").HasMaxLength(64).IsRequired();
                b.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(64).IsRequired();

                // VD-M-02 lop 2: cung mot su viec chi ghi so MOT lan.
                b.HasIndex(x => new { x.Type, x.Reference }).IsUnique().HasDatabaseName("ux_journal_type_reference");
                b.HasIndex(x => x.Seq).IsUnique().HasDatabaseName("ux_journal_seq");

                b.OwnsMany(x => x.Lines, l =>
                {
                    l.ToTable("journal_lines", t => t.HasCheckConstraint("ck_journal_lines_amount_khac_0", "amount <> 0"));
                    l.WithOwner().HasForeignKey("entry_id");
                    l.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
                    l.HasKey(x => x.Id);
                    l.Property(x => x.AccountCode).HasColumnName("account_code").HasMaxLength(120).IsRequired();
                    l.Property(x => x.Amount).HasColumnName("amount").IsRequired();
                    l.HasIndex(x => x.AccountCode).HasDatabaseName("ix_journal_lines_account");
                });
            });

            modelBuilder.Entity<FundsHold>(b =>
            {
                b.ToTable("holds");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.AnnotationId).HasColumnName("annotation_id").IsRequired();
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
                b.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd").IsRequired();
                b.Property(x => x.HeldAt).HasColumnName("held_at").IsRequired();
                b.Property(x => x.ReleaseAt).HasColumnName("release_at").IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.ReleasedAt).HasColumnName("released_at");
                b.HasIndex(x => x.AnnotationId).IsUnique().HasDatabaseName("ux_holds_annotation");
                b.HasIndex(x => x.ReleaseAt).HasFilter("state = 'Held'").HasDatabaseName("ix_holds_den_han");
                b.HasIndex(x => x.TaskId).HasDatabaseName("ix_holds_task");
            });

            modelBuilder.Entity<Withdrawal>(b =>
            {
                b.ToTable("withdrawals");
                b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                b.HasKey(x => x.Id);
                b.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
                b.Property(x => x.AmountVnd).HasColumnName("amount_vnd").IsRequired();
                b.Property(x => x.TaxVnd).HasColumnName("tax_vnd").IsRequired();
                b.Property(x => x.NetVnd).HasColumnName("net_vnd").IsRequired();
                b.Property(x => x.BankAccount).HasColumnName("bank_account").HasMaxLength(100).IsRequired();
                b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(Withdrawal.DoDaiKhoaToiDa).IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
                b.Property(x => x.CompletedAt).HasColumnName("completed_at");
                b.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
                b.Property<uint>("RowVersion").IsRowVersion();
                b.HasIndex(x => new { x.LabelerId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_withdrawals_idempotency");
            });

            modelBuilder.Entity<ProjectEscrow>(b =>
            {
                b.ToTable("project_escrows");
                b.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
                b.HasKey(x => x.ProjectId);
                b.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
                b.Property(x => x.ReservedVnd).HasColumnName("reserved_vnd").IsRequired();
                b.Property(x => x.Redundancy).HasColumnName("redundancy").IsRequired();
                b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
                b.Property(x => x.ReservedAt).HasColumnName("reserved_at").IsRequired();
                b.Property(x => x.ClosedAt).HasColumnName("closed_at");
                b.HasIndex(x => x.OwnerId).HasDatabaseName("ix_project_escrows_owner");
            });

            modelBuilder.Entity<BlockedUser>(b =>
            {
                b.ToTable("blocked_users");
                b.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
                b.HasKey(x => x.UserId);
                b.Property(x => x.BlockedAt).HasColumnName("blocked_at").IsRequired();
            });

            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }

    /// <summary>
    /// Tai khoan bi khoa (user.blocked tu identity) — khong duoc rut tien. Tien van
    /// nam nguyen trong so, chi chan dong ra.
    /// </summary>
    public sealed class BlockedUser
    {
        private BlockedUser()
        {
        }

        public Guid UserId { get; private set; }

        public DateTimeOffset BlockedAt { get; private set; }

        public static BlockedUser Tao(Guid userId, DateTimeOffset luc)
        {
            BlockedUser b = new BlockedUser();
            b.UserId = userId;
            b.BlockedAt = luc;
            return b;
        }
    }
}
