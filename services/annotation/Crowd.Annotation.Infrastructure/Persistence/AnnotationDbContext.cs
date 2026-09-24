using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext cua annotation-svc, noi toi annotation_db (cong 5404).
    ///
    /// Hien moi co DUY NHAT bang outbox. Cac bang nghiep vu thuc su —
    /// annotations, versions, drafts, reviews, appeals — se them o phase P0.
    /// Muc dich truoc mat la cho outbox mot cho ton tai that de chay thu
    /// duong di cua event tu dau den cuoi.
    ///
    /// Vi sao bang outbox nam TRONG database nghiep vu chu khong phai mot DB
    /// rieng: do la toan bo y nghia cua outbox. Ghi nhan va ghi event phai
    /// nam trong CUNG mot transaction Postgres, ma transaction thi khong
    /// vuot qua ranh gioi database duoc (VD-D-01).
    /// </summary>
    public sealed class AnnotationDbContext : DbContext
    {
        public AnnotationDbContext(DbContextOptions<AnnotationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Hang cho gui event. Code nghiep vu KHONG dung truc tiep DbSet nay —
        /// phai di qua IOutboxWriter.Enqueue de dam bao dung giao keo.
        /// </summary>
        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        /// <summary>
        /// Dau vet cac event da xu ly. Code nghiep vu KHONG dung truc tiep —
        /// phai di qua IIdempotencyGuard.XuLyMotLanAsync.
        /// </summary>
        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            // Dung chung cau hinh bang outbox voi 12 service con lai. Khai bao
            // mot lan o thu vien, moi service chi goi mot dong nhu the nay.
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }
}
