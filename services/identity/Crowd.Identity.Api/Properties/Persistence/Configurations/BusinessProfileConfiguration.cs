using System;
using Crowd.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Identity.Api.Persistence.Configurations
{
    public sealed class BusinessProfileConfiguration : IEntityTypeConfiguration<BusinessProfile>
    {
        public void Configure(EntityTypeBuilder<BusinessProfile> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("business_profiles");
            builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            builder.HasKey(x => x.UserId);
            builder.HasOne<User>().WithOne().HasForeignKey<BusinessProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.Property(x => x.CompanyName).HasColumnName("company_name").HasMaxLength(BusinessProfile.DoDaiTenToiDa).IsRequired();
            builder.Property(x => x.TaxCode).HasColumnName("tax_code").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(BusinessProfile.DoDaiDiaChiToiDa).IsRequired();
            builder.Property(x => x.LicenseKey).HasColumnName("license_key").HasMaxLength(300);
            builder.Property(x => x.LicenseFileName).HasColumnName("license_file_name").HasMaxLength(200);
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(BusinessProfile.DoDaiLyDoToiDa);
            builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Hang doi duyet cua admin: loc theo trang thai, cu nhat truoc.
            builder.HasIndex(x => new { x.Status, x.SubmittedAt }).HasDatabaseName("ix_business_profiles_hang_doi");
        }
    }
}
