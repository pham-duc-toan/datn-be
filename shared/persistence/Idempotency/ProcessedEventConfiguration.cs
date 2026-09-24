using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.BuildingBlocks.Persistence.Idempotency
{
    /// <summary>
    /// Anh xa ProcessedEvent sang bang. Moi service co consumer deu goi cau
    /// hinh nay trong OnModelCreating.
    /// </summary>
    public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
    {
        public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("processed_events");

            // KHOA CHINH GHEP (event_id, handler) — day la toan bo co che chong
            // trung. Khong phai code kiem "da xu ly chua" roi moi ghi, ma la
            // DATABASE tu choi dong thu hai.
            //
            // Vi sao quan trong: kiem-roi-ghi la TOCTOU. Hai message giong nhau
            // toi cung luc, ca hai cung doc "chua xu ly", ca hai cung ghi but
            // toan. Rang buoc duy nhat thi khong co ke ho do — mot trong hai
            // se nhan loi 23505 luc commit va toan bo transaction cua no bi
            // quay lui, ke ca phan nghiep vu.
            builder.HasKey(x => new { x.EventId, x.Handler });

            builder.Property(x => x.EventId).HasColumnName("event_id");
            builder.Property(x => x.Handler).HasColumnName("handler").HasMaxLength(200);
            builder.Property(x => x.ProcessedAt).HasColumnName("processed_at").IsRequired();

            // Phuc vu job don bang: xoa dau vet cu hon N ngay. Giu mai thi bang
            // nay phinh vo han, ma dau vet cu khong con gia tri — message qua
            // han tu lau da khong con nam trong queue nao.
            builder.HasIndex(x => x.ProcessedAt).HasDatabaseName("ix_processed_events_thoi_diem");
        }
    }
}
