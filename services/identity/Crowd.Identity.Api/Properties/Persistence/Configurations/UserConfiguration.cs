using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("users");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();

            // UNIQUE o tang database, khong chi o tang code. Hai nguoi dang ky
            // cung mot email cung luc se ca hai lot qua phep kiem "email da ton
            // tai chua" — TOCTOU. Rang buoc nay la thu chan duoc.
            builder.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");

            builder.Property(x => x.PasswordHash)
                .HasColumnName("password_hash").HasMaxLength(200).IsRequired();
            builder.Property(x => x.DisplayName)
                .HasColumnName("display_name").HasMaxLength(100).IsRequired();

            // Luu enum thanh chuoi: doc bang tay trong psql thay "active" thay
            // vi so 0, va chen them trang thai moi khong lam lech du lieu cu.
            builder.Property(x => x.Status)
                .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            // Roles la thuoc tinh CHI DOC; thu that su luu xuong database la
            // truong private _roles. Postgres luu thanh mang text[].
            builder.Ignore(x => x.Roles);
            builder.Property<List<string>>("_roles").HasColumnName("roles").IsRequired();
        }
    }
}
