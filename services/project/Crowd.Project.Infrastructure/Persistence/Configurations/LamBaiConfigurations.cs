using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Domain.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Project.Infrastructure.Persistence.Configurations
{
    // Phan LAM BAI va NHAN (truoc day la task-svc va annotation-svc, nay gop vao project-svc — cung vong doi
    // voi du an nen cung database, khong con ban sao du an / mau / cau vang / thanh vien).
    //
    // Hai bang PHAN VUNG HASH(project_id) 64 phan (annotations, samples) duoc TAO bang SQL tay trong
    // migration GopLamBaiVaNhan — EF khong sinh duoc partition. Khoa chinh cua bang phan vung phai chua cot
    // phan vung → (project_id, id).

    public sealed class CollectionRoundConfiguration : IEntityTypeConfiguration<CollectionRound>
    {
        public void Configure(EntityTypeBuilder<CollectionRound> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("project_collections");
            builder.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
            builder.HasKey(x => x.ProjectId);
            builder.Property(x => x.MaxParticipants).HasColumnName("max_participants").IsRequired();
            builder.Property(x => x.PausedAt).HasColumnName("paused_at");
            builder.Property(x => x.FinishReason).HasColumnName("finish_reason").HasMaxLength(20);
            builder.Property(x => x.GradedAt).HasColumnName("graded_at");
            builder.Property(x => x.AppealDeadline).HasColumnName("appeal_deadline");
            builder.Property(x => x.SettledAt).HasColumnName("settled_at");
            builder.Property(x => x.PassedCount).HasColumnName("passed_count");
            builder.Property(x => x.FinalProgressJson).HasColumnName("final_progress").HasColumnType("jsonb");
            builder.Property(x => x.DataPurgedAt).HasColumnName("data_purged_at");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Ignore(x => x.DaDongThuBai);

            builder.HasOne<LabelingProject>().WithOne().HasForeignKey<CollectionRound>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // Worker: du an da cham cho chot (het han khieu nai).
            builder.HasIndex(x => x.AppealDeadline)
                .HasFilter("graded_at IS NOT NULL AND settled_at IS NULL")
                .HasDatabaseName("ix_project_collections_cho_chot");
        }
    }

    public sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
    {
        public void Configure(EntityTypeBuilder<Attempt> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("attempts");
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
            builder.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            builder.Property(x => x.EndedAt).HasColumnName("ended_at");

            // Thu tu cau hoi rieng cua bai (uuid[]).
            builder.Ignore(x => x.QuestionOrder);
            builder.Property<List<Guid>>("_questionOrder").HasColumnName("question_order").IsRequired();

            builder.Property(x => x.GoldTotal).HasColumnName("gold_total").IsRequired();
            builder.Property(x => x.GoldCorrect).HasColumnName("gold_correct").IsRequired();
            builder.Property(x => x.AnsweredCount).HasColumnName("answered_count");
            builder.Property(x => x.GoldAnswersJson).HasColumnName("gold_answers").HasColumnType("jsonb");
            builder.Property(x => x.ClientIp).HasColumnName("client_ip").HasMaxLength(64);
            builder.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(100);

            builder.Property(x => x.ComparableCount).HasColumnName("comparable_count");
            builder.Property(x => x.MatchedCount).HasColumnName("matched_count");
            builder.Property(x => x.AgreementPercent).HasColumnName("agreement_percent");
            builder.Property(x => x.Passed).HasColumnName("passed");
            builder.Property(x => x.Rewarded).HasColumnName("rewarded");
            builder.Property(x => x.GradedAt).HasColumnName("graded_at");

            builder.Property(x => x.AppealState).HasColumnName("appeal_state").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.AppealMessage).HasColumnName("appeal_message").HasMaxLength(Attempt.DoDaiKhieuNaiToiDa);
            builder.Property(x => x.AppealedAt).HasColumnName("appealed_at");
            builder.Property(x => x.AppealNote).HasColumnName("appeal_note").HasMaxLength(Attempt.DoDaiKhieuNaiToiDa);
            builder.Property(x => x.AppealResolvedBy).HasColumnName("appeal_resolved_by");
            builder.Property(x => x.AppealResolvedAt).HasColumnName("appeal_resolved_at");

            builder.Ignore(x => x.DangLam);
            builder.Ignore(x => x.DatCuoiCung);
            builder.Ignore(x => x.DuocThuong);

            // Khoa lac quan: worker het han va nguoi nop cham cung mot dong — ben den sau nhan 0 dong thay vi
            // ghi de (VD-D-11). Moi duong ghi deu khoa FOR UPDATE truoc nen hiem khi gap.
            builder.Property<uint>("RowVersion").IsRowVersion();

            builder.HasOne<LabelingProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // MOT NGUOI MOT BAI moi du an (dang lam hoac da nop). Bai het han / bo thi lam lai duoc.
            builder.HasIndex(x => new { x.ProjectId, x.LabelerId })
                .IsUnique()
                .HasFilter("state IN ('InProgress','Submitted')")
                .HasDatabaseName("ux_attempts_project_labeler_active");

            // Dem suat + liet ke bai cua du an.
            builder.HasIndex(x => new { x.ProjectId, x.State }).HasDatabaseName("ix_attempts_project_state");

            // "Bai cua toi" va "nhan cua toi" (doc qua attempts, khong quet 64 phan vung annotations).
            builder.HasIndex(x => new { x.LabelerId, x.StartedAt }).HasDatabaseName("ix_attempts_labeler");

            // So cau duoc chap nhan / bang xep hang cua identity (gRPC LabelerStatsData, 3.24): chi bai dat hoac khieu
            // nai duoc chap nhan — truoc la bo dem labeler_stats.approved_count chep lai o identity.
            builder.HasIndex(x => x.LabelerId)
                .HasFilter("passed = true OR appeal_state = 'Accepted'")
                .HasDatabaseName("ix_attempts_chap_nhan");

            // Worker tim bai qua han giu.
            builder.HasIndex(x => x.ExpiresAt).HasFilter("state = 'InProgress'").HasDatabaseName("ix_attempts_het_han");

            // Hang doi khieu nai cua admin.
            builder.HasIndex(x => x.AppealedAt).HasFilter("appeal_state = 'Open'").HasDatabaseName("ix_attempts_khieu_nai_mo");

            // Chong gian lan (quality-svc hoi qua gRPC FindSharedSubmitters, 3.22): ai nop bai tu cung IP / thiet
            // bi trong mot khoang thoi gian — truoc la bang fraud_submissions chep lai o quality.
            builder.HasIndex(x => new { x.ClientIp, x.SubmittedAt }).HasFilter("client_ip IS NOT NULL").HasDatabaseName("ix_attempts_client_ip");
            builder.HasIndex(x => new { x.DeviceId, x.SubmittedAt }).HasFilter("device_id IS NOT NULL").HasDatabaseName("ix_attempts_device");
        }
    }

    public sealed class AttemptHistoryConfiguration : IEntityTypeConfiguration<AttemptHistoryEntry>
    {
        public void Configure(EntityTypeBuilder<AttemptHistoryEntry> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("attempt_history");
            builder.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
            builder.Property(x => x.ActorId).HasColumnName("actor_id");
            builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(AttemptHistoryEntry.DoDaiGhiChuToiDa);
            builder.Property(x => x.At).HasColumnName("at").IsRequired();
            builder.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.AttemptId).HasDatabaseName("ix_attempt_history_attempt");
        }
    }

    public sealed class SampleReportConfiguration : IEntityTypeConfiguration<SampleReport>
    {
        public void Configure(EntityTypeBuilder<SampleReport> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("sample_reports");
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            builder.HasKey(x => x.Id);
            builder.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
            builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(x => x.SampleId).HasColumnName("sample_id").IsRequired();
            builder.Property(x => x.LabelerId).HasColumnName("labeler_id").IsRequired();
            builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(SampleReport.DoDaiGhiChuToiDa);
            builder.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
            builder.Property(x => x.ResolvedBy).HasColumnName("resolved_by");

            builder.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Sample>().WithMany().HasForeignKey(x => new { x.ProjectId, x.SampleId })
                .HasPrincipalKey(s => new { s.ProjectId, s.Id }).OnDelete(DeleteBehavior.Cascade);

            // Moi bai bao mot cau mot lan.
            builder.HasIndex(x => new { x.AttemptId, x.SampleId }).IsUnique().HasDatabaseName("ux_sample_reports_attempt_sample");
            // Chu du an xem bao loi cua du an; dem so nguoi bao moi mau.
            builder.HasIndex(x => new { x.ProjectId, x.State, x.CreatedAt }).HasDatabaseName("ix_sample_reports_du_an");
            builder.HasIndex(x => new { x.ProjectId, x.SampleId }).HasDatabaseName("ix_sample_reports_mau");
        }
    }

    public sealed class LabelAnnotationConfiguration : IEntityTypeConfiguration<LabelAnnotation>
    {
        public void Configure(EntityTypeBuilder<LabelAnnotation> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Bang LON NHAT ve lau dai (moi nhan da nop) — PHAN VUNG HASH(project_id) 64 phan, payload nen lz4
            // (migration). Khong chi muc GIN tren payload: khong truy van nao loc ben trong JSON (phan bo nhan
            // tinh tu ket qua gop), GIN chi ton them 30-50% dung luong va lam cham moi lan chen.
            // Chi 6 cot (xem LabelAnnotation): trang thai / moc thoi gian suy ra tu attempts, dinh dang tu projects.
            builder.ToTable("annotations");
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.Property(x => x.AttemptId).HasColumnName("attempt_id");
            builder.Property(x => x.SampleId).HasColumnName("sample_id");

            // Moi (bai lam, mau) DUNG MOT nhan — bai nop / gate.solved giao lai khong sinh nhan thu hai. Khoa chinh
            // cua bang phan vung phai chua cot phan vung (project_id).
            builder.HasKey(x => new { x.ProjectId, x.AttemptId, x.SampleId });
            builder.Property(x => x.LabelerId).HasColumnName("labeler_id");
            builder.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(x => x.DataJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

            // KHONG dung xmin lam khoa lac quan o day: bang PHAN VUNG khong cho "INSERT ... RETURNING xmin" (Postgres
            // bao "cannot retrieve a system column in this context"). Cung khong can: nhan chi INSERT luc nop / gate.solved,
            // khong bao gio UPDATE (cham va khieu nai chi sua attempts).
        }
    }

    public sealed class SampleResultCacheConfiguration : IEntityTypeConfiguration<KetQuaMauDem>
    {
        public void Configure(EntityTypeBuilder<KetQuaMauDem> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Bo nho dem ket qua gop theo mau (dau van tay dau vao) — xem KetQuaMauDem.
            builder.ToTable("sample_results");
            builder.Property(x => x.ProjectId).HasColumnName("project_id");
            builder.Property(x => x.SampleId).HasColumnName("sample_id");
            builder.HasKey(x => new { x.ProjectId, x.SampleId });
            builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
            builder.Property(x => x.ToolsJson).HasColumnName("tools").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.ComputedAt).HasColumnName("computed_at").IsRequired();
        }
    }
}
