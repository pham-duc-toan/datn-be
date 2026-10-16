using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    /// <summary>Bang two_factor (FC-04): mot dong moi user da bat dau cai 2FA.</summary>
    public sealed class HaiLopConfiguration : IEntityTypeConfiguration<HaiLop>
    {
        public void Configure(EntityTypeBuilder<HaiLop> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("two_factor");
            builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            builder.HasKey(x => x.UserId);
            builder.HasOne<User>().WithOne().HasForeignKey<HaiLop>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.Property(x => x.SecretCipher).HasColumnName("secret_cipher").HasMaxLength(200).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.EnabledAt).HasColumnName("enabled_at");
            builder.Property(x => x.LastUsedStep).HasColumnName("last_used_step").IsRequired();

            builder.Ignore(x => x.DangBat);
            builder.Ignore(x => x.RecoveryHashes);
            builder.Property<List<string>>("_recoveryHashes").HasColumnName("recovery_hashes").IsRequired();
        }
    }

    /// <summary>Bang identity_verifications: ho so xac minh danh tinh ca nhan.</summary>
    public sealed class XacMinhDanhTinhConfiguration : IEntityTypeConfiguration<XacMinhDanhTinh>
    {
        public void Configure(EntityTypeBuilder<XacMinhDanhTinh> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("identity_verifications");
            builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            builder.HasKey(x => x.UserId);
            builder.HasOne<User>().WithOne().HasForeignKey<XacMinhDanhTinh>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(XacMinhDanhTinh.DoDaiTenToiDa).IsRequired();
            builder.Property(x => x.DateOfBirth).HasColumnName("date_of_birth");
            builder.Property(x => x.IdNumberHash).HasColumnName("id_number_hash").HasMaxLength(64).IsRequired();
            builder.Property(x => x.IdNumberLast4).HasColumnName("id_number_last4").HasMaxLength(4).IsRequired();
            builder.Property(x => x.FrontKey).HasColumnName("front_key").HasMaxLength(300);
            builder.Property(x => x.BackKey).HasColumnName("back_key").HasMaxLength(300);
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(XacMinhDanhTinh.DoDaiLyDoToiDa);
            builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            builder.HasIndex(x => new { x.Status, x.SubmittedAt }).HasDatabaseName("ix_identity_verifications_hang_doi");

            // VD-T-04: MOT giay to chi gan voi MOT tai khoan da xac minh. Chan o tang database (partial
            // UNIQUE) — hai admin duyet hai ho so cung giay to cung luc thi mot ben bi tu choi.
            builder.HasIndex(x => x.IdNumberHash)
                .IsUnique()
                .HasFilter("status = 'Verified'")
                .HasDatabaseName("ux_identity_verifications_giay_to");
        }
    }
}
