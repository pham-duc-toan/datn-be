using System;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("refresh_tokens");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

            // Tra cuu bang hash moi lan refresh => phai co index, va phai UNIQUE
            // (hai token trung hash nghia la bo sinh ngau nhien hong).
            builder.Property(x => x.TokenHash)
                .HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");

            // Thu hoi ca ho khi phat hien dung lai.
            builder.Property(x => x.FamilyId).HasColumnName("family_id").IsRequired();
            builder.HasIndex(x => x.FamilyId).HasDatabaseName("ix_refresh_tokens_family");

            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            builder.Property(x => x.ReplacedById).HasColumnName("replaced_by_id");
        }
    }
}
