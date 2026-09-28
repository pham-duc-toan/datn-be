using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.EntranceTests;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Project.Infrastructure.Persistence.Configurations
{
    // Khoa ngoai khai bang HasOne<T>().WithMany() KHONG co navigation property:
    // database van giu toan ven tham chieu, con code domain chi tham chieu nhau
    // bang Id — dung nguyen tac aggregate khong om aggregate khac.

    public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
    {
        public void Configure(EntityTypeBuilder<ProjectMember> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("project_members");

            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.HasKey(x => new { x.ProjectId, x.UserId });

            builder.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.JoinedAt).HasColumnName("joined_at").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // "Du an nao toi la thanh vien" — dung khi loc danh sach (docs 3.8).
            builder.HasIndex(x => new { x.UserId, x.State }).HasDatabaseName("ix_project_members_user");
        }
    }

    public sealed class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
    {
        public void Configure(EntityTypeBuilder<Dataset> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("datasets");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(Dataset.DoDaiTenToiDa).IsRequired();
            builder.Property(x => x.SampleCount).HasColumnName("sample_count").IsRequired();
            builder.Property(x => x.SkippedCount).HasColumnName("skipped_count").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_datasets_project");
        }
    }

    public sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
    {
        public void Configure(EntityTypeBuilder<Sample> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("samples");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.DatasetId).HasColumnName("dataset_id").IsRequired();
            builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
            builder.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(500).IsRequired();
            builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(50).IsRequired();
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Dataset>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Cascade);

            // Cung mot anh chi la MOT mau trong du an. UNIQUE o database de hai
            // lan nap song song cung khong lot duoc.
            builder.HasIndex(x => new { x.ProjectId, x.Sha256 }).IsUnique().HasDatabaseName("ux_samples_project_sha256");
            builder.HasIndex(x => x.DatasetId).HasDatabaseName("ix_samples_dataset");
        }
    }

    public sealed class GoldItemConfiguration : IEntityTypeConfiguration<GoldItem>
    {
        public void Configure(EntityTypeBuilder<GoldItem> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("gold_items");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
            builder.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            // ExpectedLabels la thuoc tinh chi doc; thu luu xuong DB la truong
            // private _expectedLabels. Postgres luu thanh mang text[].
            builder.Ignore(x => x.ExpectedLabels);
            builder.Property<List<string>>("_expectedLabels").HasColumnName("expected_labels").IsRequired();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Sample>().WithMany().HasForeignKey(x => x.SampleId).OnDelete(DeleteBehavior.Cascade);

            // Mot mau chi lam cau hoi vang mot lan trong du an.
            builder.HasIndex(x => new { x.ProjectId, x.SampleId }).IsUnique().HasDatabaseName("ux_gold_items_project_sample");
        }
    }

    public sealed class EntranceAttemptConfiguration : IEntityTypeConfiguration<EntranceAttempt>
    {
        public void Configure(EntityTypeBuilder<EntranceAttempt> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("entrance_attempts");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

            builder.Ignore(x => x.QuestionSampleIds);
            builder.Property<List<Guid>>("_questionSampleIds").HasColumnName("question_sample_ids").IsRequired();

            builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            builder.Property(x => x.ScorePercent).HasColumnName("score_percent");
            builder.Property(x => x.Passed).HasColumnName("passed");

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.ProjectId, x.UserId }).HasDatabaseName("ix_entrance_attempts_project_user");
        }
    }
}
