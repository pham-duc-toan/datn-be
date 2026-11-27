using System;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Exports;
using Crowd.Project.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Project.Infrastructure.Persistence.Configurations
{
    // Viec chay nen cua doanh nghiep (kien truc 3.41): phieu giai nen ZIP anh, lan tao file ket qua. Ca hai xoa theo
    // du an (cascade) — xoa du an nhap thi khong con phieu treo.

    /// <summary>Ten rang buoc UNIQUE ma code nghiep vu bat loi theo ten (PostgresErrors.IsUniqueViolation).</summary>
    public static class TenRangBuoc
    {
        public const string XuatDangCho = "ux_export_jobs_dang_cho";

        public const string ZipTheoKhoa = "ux_zip_imports_storage_key";
    }

    public sealed class ZipImportConfiguration : IEntityTypeConfiguration<ZipImport>
    {
        public void Configure(EntityTypeBuilder<ZipImport> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("zip_imports");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
            builder.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(CotDb.TenMau).IsRequired();
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            builder.Property(x => x.State).HasColumnName("state").HasMaxLength(20).IsRequired();
            builder.Property(x => x.ProcessedCount).HasColumnName("processed_count").IsRequired();
            builder.Property(x => x.ImageCount).HasColumnName("image_count").IsRequired();
            builder.Property(x => x.SkippedCount).HasColumnName("skipped_count").IsRequired();
            builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(CotDb.TomTatLoi);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.StartedAt).HasColumnName("started_at");
            builder.Property(x => x.FinishedAt).HasColumnName("finished_at");

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // Mot file ZIP chi dang ky MOT lan: gui lai cung key (bam hai lan, mang chap chon) khong giai nen hai lan.
            builder.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName(TenRangBuoc.ZipTheoKhoa);

            // Trang thai nap cua du an (giao dien + readiness publish) + worker tim phieu cho.
            builder.HasIndex(x => new { x.ProjectId, x.CreatedAt }).HasDatabaseName("ix_zip_imports_project");
            builder.HasIndex(x => x.State).HasFilter("state IN ('queued','processing')").HasDatabaseName("ix_zip_imports_dang_cho");
        }
    }

    public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
    {
        public void Configure(EntityTypeBuilder<ExportJob> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("export_jobs");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Format).HasColumnName("format").HasMaxLength(30).IsRequired();
            builder.Property(x => x.RequestedBy).HasColumnName("requested_by");
            builder.Property(x => x.State).HasColumnName("state").HasMaxLength(20).IsRequired();
            builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300);
            builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(200);
            builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            builder.Property(x => x.ResultFingerprint).HasColumnName("result_fingerprint").HasMaxLength(64);
            builder.Property(x => x.SampleCount).HasColumnName("sample_count");
            builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(ExportJob.DoDaiLoiToiDa);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.StartedAt).HasColumnName("started_at");
            builder.Property(x => x.FinishedAt).HasColumnName("finished_at");

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // Danh sach file cua du an (moi nhat truoc) + tim lan xuat cung dinh dang (dung lai file, chan bam trung).
            builder.HasIndex(x => new { x.ProjectId, x.Format, x.CreatedAt }).HasDatabaseName("ix_export_jobs_project_format");

            // Moi (du an, dinh dang) chi MOT lan dang tao: doanh nghiep bam hai lan (hai tab, mang chap chon) cung luc thi
            // lan sau dung rang buoc nay va nhan lai lan dang chay — khong gop ket qua hai lan.
            builder.HasIndex(x => new { x.ProjectId, x.Format })
                .IsUnique()
                .HasFilter("state IN ('queued','processing')")
                .HasDatabaseName(TenRangBuoc.XuatDangCho);

            builder.ToTable(t => t.HasCheckConstraint(
                "ck_export_jobs_file_khi_xong",
                "(state = 'ready') = (storage_key IS NOT NULL)"));
        }
    }
}
