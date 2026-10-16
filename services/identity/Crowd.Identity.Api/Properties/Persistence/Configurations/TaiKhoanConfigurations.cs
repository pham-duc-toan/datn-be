using System;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    public sealed class MaTaiKhoanConfiguration : IEntityTypeConfiguration<MaTaiKhoan>
    {
        public void Configure(EntityTypeBuilder<MaTaiKhoan> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("account_tokens");
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            builder.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.UsedAt).HasColumnName("used_at");
            builder.Property(x => x.FailedAttempts).HasColumnName("failed_attempts").IsRequired().HasDefaultValue(0);

            // Tra theo hash khi nguoi dung bam link; huy cac ma cu cung muc dich khi phat ma moi.
            builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_account_tokens_hash");
            builder.HasIndex(x => new { x.UserId, x.Purpose }).HasDatabaseName("ix_account_tokens_user");
            builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_account_tokens_het_han");
            builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public sealed class ThongKeLabelerConfiguration : IEntityTypeConfiguration<ThongKeLabeler>
    {
        public void Configure(EntityTypeBuilder<ThongKeLabeler> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("labeler_stats");
            builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            builder.HasKey(x => x.UserId);
            builder.Property(x => x.ApprovedCount).HasColumnName("approved_count").IsRequired();
            builder.Property(x => x.Reputation).HasColumnName("reputation");
            builder.Property(x => x.ReputationAt).HasColumnName("reputation_at");
            builder.Property(x => x.Level).HasColumnName("level").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Bang xep hang toan thoi gian.
            builder.HasIndex(x => x.ApprovedCount).HasDatabaseName("ix_labeler_stats_xep_hang");
        }
    }

    public sealed class ThongKeNgayConfiguration : IEntityTypeConfiguration<ThongKeNgay>
    {
        public void Configure(EntityTypeBuilder<ThongKeNgay> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("labeler_daily_stats");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.Property(x => x.Ngay).HasColumnName("day");
            builder.HasKey(x => new { x.UserId, x.Ngay });
            builder.Property(x => x.ApprovedCount).HasColumnName("approved_count").IsRequired();

            // Bang xep hang theo khoang ngay.
            builder.HasIndex(x => x.Ngay).HasDatabaseName("ix_labeler_daily_stats_ngay");
        }
    }

    public sealed class HuyHieuDaDatConfiguration : IEntityTypeConfiguration<HuyHieuDaDat>
    {
        public void Configure(EntityTypeBuilder<HuyHieuDaDat> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("user_badges");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.Property(x => x.Badge).HasColumnName("badge").HasMaxLength(40);
            builder.HasKey(x => new { x.UserId, x.Badge });
            builder.Property(x => x.AwardedAt).HasColumnName("awarded_at").IsRequired();
        }
    }

    public sealed class MocThuHoiPhienConfiguration : IEntityTypeConfiguration<MocThuHoiPhien>
    {
        public void Configure(EntityTypeBuilder<MocThuHoiPhien> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("session_revocations");
            builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            builder.HasKey(x => x.UserId);
            builder.Property(x => x.RevokedAt).HasColumnName("revoked_at").IsRequired();

            // Gateway xin cac moc con tac dung: revoked_at > bay gio - tuoi access token.
            builder.HasIndex(x => x.RevokedAt).HasDatabaseName("ix_session_revocations_moc");
        }
    }
}
