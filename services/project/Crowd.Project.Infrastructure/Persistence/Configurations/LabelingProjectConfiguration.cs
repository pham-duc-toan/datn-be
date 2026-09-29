using System;
using Crowd.Project.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crowd.Project.Infrastructure.Persistence.Configurations
{
    public sealed class LabelingProjectConfiguration : IEntityTypeConfiguration<LabelingProject>
    {
        public void Configure(EntityTypeBuilder<LabelingProject> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("projects");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(LabelingProject.DoDaiTenToiDa).IsRequired();
            builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(LabelingProject.DoDaiMoTaToiDa).IsRequired();

            // Enum luu thanh chuoi: doc psql thay "Running" thay vi so 3, va chen
            // gia tri moi vao giua enum khong lam lech du lieu cu.
            builder.Property(x => x.TaskType).HasColumnName("task_type").HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
            builder.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(20).IsRequired();

            // JSONB: moi loai bai toan mot hinh dang schema rieng ma khong them bang.
            ValueConverter<LabelSchema?, string> doiSchema = new ValueConverter<LabelSchema?, string>(
                v => JsonColumns.VietLabelSchema(v),
                s => JsonColumns.DocLabelSchema(s));

            builder.Property(x => x.LabelSchema)
                .HasColumnName("label_schema")
                .HasColumnType("jsonb")
                .HasConversion(doiSchema);

            ValueConverter<Guideline?, string> doiHuongDan = new ValueConverter<Guideline?, string>(
                v => JsonColumns.VietGuideline(v),
                s => JsonColumns.DocGuideline(s));

            builder.Property(x => x.Guideline)
                .HasColumnName("guideline")
                .HasColumnType("jsonb")
                .HasConversion(doiHuongDan);

            builder.Property(x => x.UnitPriceVnd).HasColumnName("unit_price_vnd").IsRequired();
            builder.Property(x => x.Redundancy).HasColumnName("redundancy").IsRequired();
            builder.Property(x => x.BudgetVnd).HasColumnName("budget_vnd").IsRequired();
            builder.Property(x => x.Deadline).HasColumnName("deadline");

            builder.Property(x => x.AllowProfessional).HasColumnName("allow_professional").IsRequired();
            builder.Property(x => x.AllowLinkGateway).HasColumnName("allow_link_gateway").IsRequired();
            builder.Property(x => x.AllowCollaborative).HasColumnName("allow_collaborative").IsRequired();

            builder.Property(x => x.MinLevel).HasColumnName("min_level");
            builder.Property(x => x.MinReputation).HasColumnName("min_reputation");
            builder.Property(x => x.RequireEntranceTest).HasColumnName("require_entrance_test").IsRequired();
            builder.Property(x => x.EntranceQuestionCount).HasColumnName("entrance_question_count").IsRequired();
            builder.Property(x => x.EntrancePassPercent).HasColumnName("entrance_pass_percent").IsRequired();

            builder.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(1000);
            builder.Property(x => x.WasEscrowed).HasColumnName("was_escrowed").IsRequired();
            builder.Property(x => x.PlatformFeePercent).HasColumnName("platform_fee_percent").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(x => x.SubmittedForApprovalAt).HasColumnName("submitted_for_approval_at");
            builder.Property(x => x.PublishedAt).HasColumnName("published_at");
            builder.Property(x => x.ClosedAt).HasColumnName("closed_at");

            // OPTIMISTIC CONCURRENCY (VD-D-11) bang cot he thong xmin cua Postgres.
            //
            // Moi lan mot dong bi UPDATE, Postgres tu doi xmin. EF doc xmin luc nap,
            // roi sinh:  UPDATE projects SET ... WHERE id = @id AND xmin = @xmin_cu
            // Hai request cung publish: ca hai nap thay Draft, nhung chi MOT cai
            // UPDATE khop xmin. Cai kia nhan 0 dong → EF nem
            // DbUpdateConcurrencyException → Api tra 409. Dieu kien nam TRONG cau
            // UPDATE, dung yeu cau cua D-11, ma luat chuyen trang thai van nam
            // trong aggregate.
            //
            // Shadow property: domain khong biet gi ve xmin.
            builder.Property<uint>("RowVersion").IsRowVersion();

            builder.HasIndex(x => x.OwnerId).HasDatabaseName("ix_projects_owner");

            // Trang duyet du an cua labeler: loc Running + Public.
            builder.HasIndex(x => new { x.Status, x.Visibility }).HasDatabaseName("ix_projects_status_visibility");
        }
    }
}
