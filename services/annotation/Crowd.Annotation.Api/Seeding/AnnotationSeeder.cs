using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Consumers;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Annotation.Api.Seeding
{
    /// <summary>
    /// Seed annotation-svc: dieu khoan du an + ban sao thanh vien (phat lai
    /// project.published / member.added vao CHINH processor), roi cac nhan cua
    /// kich ban o dung trang thai: cho duyet, da duyet, bi tu choi, dang khieu nai.
    ///
    /// Nhan tao bang LabelAnnotation.TaoTuLuotNop roi goi Duyet / TuChoi /
    /// KhieuNai — dung luat domain (vd khong tu duyet nhan cua minh, khieu nai
    /// trong han 7 ngay). AnnotationId tat dinh vi ledger-svc tham chieu toi no.
    /// </summary>
    public sealed class AnnotationSeeder
    {
        private readonly AnnotationDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<AnnotationSeeder> _logger;

        public AnnotationSeeder(AnnotationDbContext db, TimeProvider clock, ILogger<AnnotationSeeder> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _clock = clock;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Idempotent THEO TUNG DU AN (moc: thanh vien chu du an — du an Nhap
            // chua co project_terms). DB cu da co P1-P4 thi van them du an moi.
            List<Guid> ids = KichBanSeed.Projects.Select(p => p.Id).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(
                await _db.Members.Where(m => ids.Contains(m.ProjectId)).Select(m => m.ProjectId).Distinct().ToListAsync(ct));

            List<SeedProject> canTao = KichBanSeed.Projects.Where(p => !daCo.Contains(p.Id)).ToList();
            if (canTao.Count == 0)
            {
                _logger.LogInformation("Seed annotation: da du du an seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                await PhatLaiEventAsync(canTao, bayGio, ct);
                int soNhan = TaoNhan(canTao, bayGio);

                SeedOutbox.BoEventChuaGui(_db);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation("Seed annotation: tao {SoNhan} nhan", soNhan);
            }
        }

        private async Task PhatLaiEventAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            MemberAddedProcessor thanhVien = new MemberAddedProcessor(_db);
            ProjectPublishedProcessor daPublish = new ProjectPublishedProcessor(_db);

            foreach (SeedProject sp in duAn)
            {
                DateTimeOffset lucTao = bayGio - sp.CreatedAgo;
                List<MemberAdded> dsThanhVien = SeedEvents.ThanhVien(sp);

                await thanhVien.XuLyAsync(SeedEvents.Boc(dsThanhVien[0], lucTao), ct);

                if (sp.Stage == SeedStage.Running)
                {
                    DateTimeOffset lucDuyet = sp.LucDuyet(bayGio);
                    await daPublish.XuLyAsync(SeedEvents.Boc(SeedEvents.DaPublish(sp, bayGio), lucDuyet), ct);

                    for (int i = 1; i < dsThanhVien.Count; i++)
                    {
                        await thanhVien.XuLyAsync(SeedEvents.Boc(dsThanhVien[i], lucDuyet + TimeSpan.FromHours(1)), ct);
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
        }

        private int TaoNhan(List<SeedProject> duAn, DateTimeOffset bayGio)
        {
            int dem = 0;

            foreach (SeedProject sp in duAn)
            {
                foreach (SeedSubmission s in sp.Submissions)
                {
                    TaoMotNhan(sp, s, bayGio);
                    dem++;
                }
            }

            return dem;
        }

        private void TaoMotNhan(SeedProject sp, SeedSubmission s, DateTimeOffset bayGio)
        {
            SeedSample mau = sp.Mau(s.SampleId);

            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                s.AssignmentId,
                s.TaskId,
                s.ProjectId,
                s.SampleId,
                s.StorageKey,
                mau.ContentJson == null ? null : RawJson.Tu(mau.ContentJson),
                mau.Metadata.ToRawJson(),
                s.LabelerId,
                SeedEvents.NhanCua(sp, s),
                bayGio - s.SubmittedAgo);
            SeedIds.GanId(a, s.AnnotationId);

            ApKetQuaDuyet(a, s, bayGio);

            _db.Annotations.Add(a);
        }

        private static void ApKetQuaDuyet(LabelAnnotation a, SeedSubmission s, DateTimeOffset bayGio)
        {
            if (s.Review == SeedReview.PendingReview)
            {
                return;
            }

            if (!s.ReviewerId.HasValue || !s.ReviewedAgo.HasValue)
            {
                throw new InvalidOperationException("Luot nop " + s.AnnotationId + " thieu nguoi duyet / luc duyet.");
            }

            Guid nguoiDuyet = s.ReviewerId.Value;
            DateTimeOffset lucDuyet = bayGio - s.ReviewedAgo.Value;

            if (s.Review == SeedReview.Approved)
            {
                a.Duyet(nguoiDuyet, lucDuyet);
                return;
            }

            a.TuChoi(nguoiDuyet, s.RejectReason ?? "Sai nhan.", lucDuyet);

            if (s.Review == SeedReview.Appealed)
            {
                if (!s.AppealedAgo.HasValue || s.AppealMessage == null)
                {
                    throw new InvalidOperationException("Luot nop " + s.AnnotationId + " thieu noi dung / luc khieu nai.");
                }

                a.KhieuNai(s.LabelerId, s.AppealMessage, bayGio - s.AppealedAgo.Value);
            }
        }
    }
}
