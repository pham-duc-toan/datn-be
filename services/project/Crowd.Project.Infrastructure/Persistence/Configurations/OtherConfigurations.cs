using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Common;
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
            builder.Property(x => x.BlockedByOwnerList).HasColumnName("blocked_by_owner_list").IsRequired().HasDefaultValue(false);

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // "Du an nao toi la thanh vien" — dung khi loc danh sach (docs 3.8).
            builder.HasIndex(x => new { x.UserId, x.State }).HasDatabaseName("ix_project_members_user");
        }
    }

    public sealed class InviteCodeConfiguration : IEntityTypeConfiguration<InviteCode>
    {
        public void Configure(EntityTypeBuilder<InviteCode> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("invite_codes");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(InviteCode.DoDaiMa).IsRequired();
            builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(InviteCode.DoDaiGhiChuToiDa);
            builder.Property(x => x.MaxUses).HasColumnName("max_uses");
            builder.Property(x => x.UsedCount).HasColumnName("used_count").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

            // Hai nguoi cung dung LUOT CUOI cua ma: UPDATE ... WHERE xmin = cu chi khop mot
            // dong, nguoi kia nhan DbUpdateConcurrencyException va thu lai (thay ma da het luot).
            builder.Property<uint>("RowVersion").IsRowVersion();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_invite_codes_code");
            builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_invite_codes_project");
        }
    }

    public sealed class JoinRequestConfiguration : IEntityTypeConfiguration<JoinRequest>
    {
        public void Configure(EntityTypeBuilder<JoinRequest> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("join_requests");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(JoinRequest.DoDaiLoiNhanToiDa);
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.EntranceAttemptId).HasColumnName("entrance_attempt_id");
            builder.Property(x => x.ScorePercent).HasColumnName("score_percent");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
            builder.Property(x => x.DecidedBy).HasColumnName("decided_by");
            builder.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(JoinRequest.DoDaiLoiNhanToiDa);

            // Duyet / tu choi / rut don cung luc: chi mot thao tac thang.
            builder.Property<uint>("RowVersion").IsRowVersion();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // Moi nguoi toi da MOT don dang cho moi du an — hai request gui don cung luc, DB chon mot.
            builder.HasIndex(x => new { x.ProjectId, x.UserId })
                .IsUnique()
                .HasFilter("status = 'Pending'")
                .HasDatabaseName("ux_join_requests_dang_cho");

            // Hang doi duyet cua chu du an + "don cua toi".
            builder.HasIndex(x => new { x.ProjectId, x.Status, x.CreatedAt }).HasDatabaseName("ix_join_requests_project_status");
            builder.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_join_requests_user");
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
            builder.Property(x => x.ImportSource).HasColumnName("import_source").HasMaxLength(20);
            builder.Property(x => x.ImportSpec).HasColumnName("import_spec").HasColumnType("jsonb");

            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(CotDb.TenLo).IsRequired();
            builder.Property(x => x.SampleCount).HasColumnName("sample_count").IsRequired();
            builder.Property(x => x.SkippedCount).HasColumnName("skipped_count").IsRequired();
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.ErrorSummary).HasColumnName("error_summary").HasMaxLength(CotDb.TomTatLoi);
            builder.Property(x => x.Manifest).HasColumnName("manifest").HasColumnType("jsonb").HasConversion(JsonColumns.RawJsonCotNull);
            builder.Property(x => x.ManifestKey).HasColumnName("manifest_key").HasMaxLength(300);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.FinishedAt).HasColumnName("finished_at");

            // Worker tim lo cho xu ly.
            builder.HasIndex(x => x.Status).HasFilter("status IN ('Pending','Ingesting')").HasDatabaseName("ix_datasets_dang_xu_ly");

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
            builder.Property(x => x.Modality).HasColumnName("modality").HasMaxLength(20).IsRequired();

            // File (image/audio/video) co storage_key; noi dung (text/pair) co content.
            builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(300);
            builder.Property(x => x.Content).HasColumnName("content").HasColumnType("jsonb").HasConversion(JsonColumns.RawJsonCotNull);
            builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasConversion(JsonColumns.RawJsonCot).IsRequired();
            builder.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(CotDb.TenMau).IsRequired();
            builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Dataset>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Cascade);

            // Cung mot anh chi la MOT mau trong du an. UNIQUE o database de hai
            // lan nap song song cung khong lot duoc.
            builder.HasIndex(x => new { x.ProjectId, x.Sha256 }).IsUnique().HasDatabaseName("ux_samples_project_sha256");
            builder.HasIndex(x => x.DatasetId).HasDatabaseName("ix_samples_dataset");

            // Moi mau la file HOAC noi dung, khong bao gio ca hai hay khong co gi.
            builder.ToTable(t => t.HasCheckConstraint("ck_samples_file_hoac_noi_dung", "(storage_key IS NULL) <> (content IS NULL)"));
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

            // Dap an o dinh dang nhan chung: ba cot. payload la jsonb — moi loai
            // nhan mot hinh dang JSON, them loai moi khong phai doi bang.
            // ExpectedPayload chi la cach doc ba cot do thanh mot LabelPayload.
            builder.Ignore(x => x.ExpectedPayload);
            builder.Property<string>("_expectedTaskType").HasColumnName("expected_task_type").HasMaxLength(50).IsRequired();
            builder.Property<int>("_expectedSchemaVersion").HasColumnName("expected_schema_version").IsRequired();
            builder.Property<string>("_expectedPayloadJson").HasColumnName("expected_payload").HasColumnType("jsonb").IsRequired();

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

    /// <summary>Dong y canh bao / chu ky NDA (muc 2.9, FP-01).</summary>
    public sealed class ProjectConsentConfiguration : IEntityTypeConfiguration<ProjectConsent>
    {
        public void Configure(EntityTypeBuilder<ProjectConsent> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("project_consents");
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.HasKey(x => new { x.ProjectId, x.UserId });
            builder.Property(x => x.ContentWarningAcceptedAt).HasColumnName("content_warning_accepted_at");
            builder.Property(x => x.NdaVersion).HasColumnName("nda_version");
            builder.Property(x => x.NdaTextHash).HasColumnName("nda_text_hash").HasMaxLength(64);
            builder.Property(x => x.NdaSignedName).HasColumnName("nda_signed_name").HasMaxLength(ProjectConsent.DoDaiTenToiDa);
            builder.Property(x => x.NdaSignedAt).HasColumnName("nda_signed_at");
            builder.Property(x => x.Ip).HasColumnName("ip").HasMaxLength(64);
            builder.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(300);
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Loi moi cho (email chua co tai khoan / du an can dong y rieng).</summary>
    public sealed class ProjectInvitationConfiguration : IEntityTypeConfiguration<ProjectInvitation>
    {
        public void Configure(EntityTypeBuilder<ProjectInvitation> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("project_invitations");
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.InvitedBy).HasColumnName("invited_by").IsRequired();
            builder.Property(x => x.ByEmail).HasColumnName("by_email").IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // Moi (du an, email, cach moi) mot loi moi dang cho. Tach theo cach moi (VD-S-16): loi moi theo email
            // va theo userId cua cung mot nguoi la hai dong — gop lai thi trung invitationId lo email ↔ tai khoan.
            builder.HasIndex(x => new { x.ProjectId, x.Email, x.ByEmail }).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_project_invitations_cho");
            builder.HasIndex(x => x.Email).HasFilter("status = 'Pending'").HasDatabaseName("ix_project_invitations_email");
            builder.HasIndex(x => x.UserId).HasFilter("status = 'Pending'").HasDatabaseName("ix_project_invitations_user");
            builder.HasIndex(x => x.ExpiresAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_project_invitations_het_han");
            // Han muc loi moi email moi nguoi / 24 gio (VD-S-16).
            builder.HasIndex(x => new { x.InvitedBy, x.CreatedAt }).HasFilter("by_email").HasDatabaseName("ix_project_invitations_han_muc");
        }
    }

    /// <summary>Danh sach uu tien / chan cua doanh nghiep (FB-23).</summary>
    public sealed class OwnerLabelerEntryConfiguration : IEntityTypeConfiguration<OwnerLabelerEntry>
    {
        public void Configure(EntityTypeBuilder<OwnerLabelerEntry> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("owner_labeler_lists");
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.LabelerId).HasColumnName("labeler_id");
            builder.HasKey(x => new { x.OwnerId, x.LabelerId });
            builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(OwnerLabelerEntry.DoDaiGhiChuToiDa);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        }
    }

    /// <summary>Danh gia labeler theo du an (FB-23).</summary>
    public sealed class LabelerRatingConfiguration : IEntityTypeConfiguration<LabelerRating>
    {
        public void Configure(EntityTypeBuilder<LabelerRating> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("labeler_ratings");
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.Property(x => x.LabelerId).HasColumnName("labeler_id");
            builder.HasKey(x => new { x.ProjectId, x.LabelerId });
            builder.Property(x => x.OwnerId).HasColumnName("owner_id").IsRequired();
            builder.Property(x => x.Stars).HasColumnName("stars").IsRequired();
            builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(LabelerRating.DoDaiNhanXetToiDa);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.ToTable(t => t.HasCheckConstraint("ck_labeler_ratings_sao", "stars BETWEEN 1 AND 5"));
            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.LabelerId).HasDatabaseName("ix_labeler_ratings_labeler");
        }
    }
}
