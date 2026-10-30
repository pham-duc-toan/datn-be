using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
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
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(CotDb.TenDuAn).IsRequired();
            builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(CotDb.MoTaDuAn).IsRequired();

            // Enum luu thanh chuoi: doc psql thay "Running" thay vi so 3, va chen
            // gia tri moi vao giua enum khong lam lech du lieu cu.
            builder.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();
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

            // Tien theo CA DU AN (docs 3.18): thuong moi nguoi dat x so suat; ky quy tinh tu dong.
            builder.Property(x => x.RewardVnd).HasColumnName("reward_vnd").IsRequired();
            builder.Property(x => x.Slots).HasColumnName("slots").IsRequired();
            builder.Property(x => x.PassThresholdPercent).HasColumnName("pass_threshold_percent").IsRequired();
            builder.Property(x => x.FillSlotsMinPercent).HasColumnName("fill_slots_min_percent");
            builder.Property(x => x.QuestionCount).HasColumnName("question_count").IsRequired();
            builder.Property(x => x.Deadline).HasColumnName("deadline");
            builder.Property(x => x.GateUnitPriceVnd).HasColumnName("gate_unit_price_vnd").IsRequired();
            builder.Property(x => x.GateBudgetVnd).HasColumnName("gate_budget_vnd").IsRequired();

            builder.Property(x => x.AllowProfessional).HasColumnName("allow_professional").IsRequired();
            builder.Property(x => x.AllowLinkGateway).HasColumnName("allow_link_gateway").IsRequired();
            builder.Property(x => x.AllowCollaborative).HasColumnName("allow_collaborative").IsRequired();

            builder.Property(x => x.MinLevel).HasColumnName("min_level");
            builder.Property(x => x.MinReputation).HasColumnName("min_reputation");
            builder.Property(x => x.RequireEntranceTest).HasColumnName("require_entrance_test").IsRequired();
            builder.Property(x => x.EntranceQuestionCount).HasColumnName("entrance_question_count").IsRequired();
            builder.Property(x => x.EntrancePassPercent).HasColumnName("entrance_pass_percent").IsRequired();

            // Bao ve (muc 2.9, FP-01, FP-05).
            builder.Property(x => x.SensitiveContent).HasColumnName("sensitive_content").IsRequired().HasDefaultValue(false);
            builder.Property(x => x.ContentWarning).HasColumnName("content_warning").HasMaxLength(CotDb.CanhBaoNoiDung);
            builder.Property(x => x.NdaRequired).HasColumnName("nda_required").IsRequired().HasDefaultValue(false);
            builder.Property(x => x.NdaText).HasColumnName("nda_text").HasMaxLength(CotDb.VanBanNda);
            builder.Property(x => x.NdaVersion).HasColumnName("nda_version").IsRequired().HasDefaultValue(0);
            builder.Property(x => x.RequireVerifiedIdentity).HasColumnName("require_verified_identity").IsRequired().HasDefaultValue(false);

            // Nhan goi y (FL-02): mang text[].
            builder.Ignore(x => x.Domains);
            builder.Ignore(x => x.Languages);
            builder.Property<List<string>>("_domains").HasColumnName("domains").IsRequired().HasDefaultValueSql("'{}'::text[]");
            builder.Property<List<string>>("_languages").HasColumnName("languages").IsRequired().HasDefaultValueSql("'{}'::text[]");

            builder.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(1000);
            builder.Property(x => x.WasEscrowed).HasColumnName("was_escrowed").IsRequired();
            builder.Property(x => x.PlatformFeePercent).HasColumnName("platform_fee_percent").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(x => x.SubmittedForApprovalAt).HasColumnName("submitted_for_approval_at");
            builder.Property(x => x.PublishedAt).HasColumnName("published_at");
            builder.Property(x => x.ClosedAt).HasColumnName("closed_at");
            builder.Property(x => x.FinishRequestedAt).HasColumnName("finish_requested_at");
            builder.Property(x => x.GradingStartedAt).HasColumnName("grading_started_at");
            builder.Property(x => x.DeleteFilesAfterClose).HasColumnName("delete_files_after_close").IsRequired().HasDefaultValue(false);
            builder.Property(x => x.FilesPurgedAt).HasColumnName("files_purged_at");

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
