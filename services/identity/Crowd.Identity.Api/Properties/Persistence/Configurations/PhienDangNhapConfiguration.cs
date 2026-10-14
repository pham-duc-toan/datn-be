using System;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    public sealed class PhienDangNhapConfiguration : IEntityTypeConfiguration<PhienDangNhap>
    {
        public void Configure(EntityTypeBuilder<PhienDangNhap> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("login_sessions");

            // Tra cuu bang KHOA CHINH (ma phien nam trong refresh token) — khong can index theo hash.
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            // Thu hoi moi phien cua mot user (doi mat khau, khoa tai khoan, dang xuat moi noi).
            builder.HasIndex(x => x.UserId).HasDatabaseName("ix_login_sessions_user");

            builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            builder.Property(x => x.Generation).HasColumnName("generation").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.LastRotatedAt).HasColumnName("last_rotated_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            // Job don xoa theo expires_at — khong co index thi moi lo quet ca bang.
            builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_login_sessions_het_han");
            builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        }
    }
}
