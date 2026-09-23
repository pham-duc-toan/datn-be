using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Ánh xạ OutboxMessage sang bảng SQL. Mọi service có database đều gọi cấu
    /// hình này trong OnModelCreating, nên 13 bảng outbox giống hệt nhau mà chỉ
    /// khai báo một lần.
    ///
    /// File này là thứ sinh ra câu CREATE TABLE khi chạy migration.
    /// </summary>
    public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("outbox");

            // ValueGeneratedNever: KHÔNG để database tự sinh khóa chính.
            // Id chính là EventId (UUIDv7) của envelope, ta tự gán.
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Version).IsRequired();
            builder.Property(x => x.CorrelationId).IsRequired();
            builder.Property(x => x.OccurredAt).IsRequired();

            // jsonb chứ không phải text: cho phép truy vấn vào bên trong khi
            // điều tra, ví dụ tìm mọi event của một dự án mà không phải parse
            // ở tầng ứng dụng.
            builder.Property(x => x.EnvelopeJson).HasColumnType("jsonb").IsRequired();

            builder.Property(x => x.PublishedAt);
            builder.Property(x => x.AttemptCount).IsRequired();
            builder.Property(x => x.LastError).HasMaxLength(2000);
            builder.Property(x => x.NextAttemptAt).IsRequired();

            // INDEX BỘ PHẬN — chi tiết quan trọng nhất của cả file này.
            //
            // Sau vài tháng bảng có hàng triệu dòng ĐÃ gửi, nhưng index chỉ chứa
            // những dòng CHƯA gửi (thường vài chục). Dispatcher quét index tí hon
            // đó thay vì cả bảng, nên tốc độ không giảm theo thời gian.
            //
            // Sinh ra SQL:
            //   CREATE INDEX ix_outbox_cho_gui ON outbox (next_attempt_at)
            //       WHERE published_at IS NULL;
            builder.HasIndex(x => x.NextAttemptAt)
                .HasDatabaseName("ix_outbox_cho_gui")
                .HasFilter("published_at IS NULL");

            // Phục vụ job dọn dẹp: xóa dòng đã gửi quá N ngày.
            builder.HasIndex(x => x.PublishedAt)
                .HasDatabaseName("ix_outbox_da_gui")
                .HasFilter("published_at IS NOT NULL");
        }
    }
}
