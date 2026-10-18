using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Consumers;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Annotation.Api.Seeding
{
    /// <summary>
    /// Seed annotation-svc: dieu khoan du an + ban sao thanh vien (phat lai
    /// project.published / member.added vao CHINH processor), roi nhan cua moi
    /// BAI DA NOP (docs 3.18): cho cham (du an dang chay) hoac da chot theo ket
    /// qua cham (du an Completed: bai dat → Accepted, bai truot → Excluded).
    ///
    /// Nhan tao bang LabelAnnotation.TaoTuBaiNop + ChotTheoKetQua — dung luat
    /// domain. AnnotationId tat dinh de cac lan seed khop nhau.
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
                int soNhan = await TaoNhanAsync(canTao, bayGio, ct);

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

                if (sp.DaChay)
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

        private async Task<int> TaoNhanAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            int dem = 0;

            foreach (SeedProject sp in duAn)
            {
                foreach (SeedAttempt b in sp.Attempts)
                {
                    if (b.State != SeedAttemptState.Submitted)
                    {
                        continue;
                    }

                    dem += await TaoNhanCuaBaiAsync(sp, b, bayGio, ct);
                }

                await _db.SaveChangesAsync(ct);

                if (sp.Stage == SeedStage.Completed)
                {
                    await ChotDuAnAsync(sp, bayGio, ct);
                }
            }

            return dem;
        }

        /// <summary>Giong AttemptSubmittedProcessor nhung gan AnnotationId tat dinh.</summary>
        private async Task<int> TaoNhanCuaBaiAsync(SeedProject sp, SeedAttempt b, DateTimeOffset bayGio, CancellationToken ct)
        {
            AttemptSubmitted bai = SeedEvents.BaiNop(sp, b, bayGio);
            Dictionary<Guid, Guid> idNhan = b.Answers.ToDictionary(a => a.SampleId, a => a.AnnotationId);
            int n = 0;

            foreach (AttemptAnswer tl in bai.Answers)
            {
                await MauDaGanStore.DamBaoAsync(_db, tl.SampleId, sp.Id, tl.StorageKey, tl.SampleContent, tl.SampleMetadata, ct);
                if (tl.LabelPayload == null)
                {
                    continue;
                }

                LabelAnnotation a = LabelAnnotation.TaoTuBaiNop(b.AttemptId, sp.Id, tl.SampleId, b.LabelerId, tl.LabelPayload, bai.SubmittedAt);
                SeedIds.GanId(a, idNhan[tl.SampleId]);
                _db.Annotations.Add(a);
                n++;
            }

            return n;
        }

        /// <summary>Du an Completed: phat lai project.graded + project.completed vao CHINH processor.</summary>
        private async Task ChotDuAnAsync(SeedProject sp, DateTimeOffset bayGio, CancellationToken ct)
        {
            if (!sp.CompletedAgo.HasValue)
            {
                throw new InvalidOperationException("Du an seed " + sp.Key + " Completed nhung thieu CompletedAgo.");
            }

            ProjectGraded cham = SeedEvents.DaCham(sp, bayGio);
            ProjectGradedProcessor daCham = new ProjectGradedProcessor(_db, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectGradedProcessor>.Instance);
            await daCham.XuLyAsync(SeedEvents.Boc(cham, cham.GradedAt), ct);

            ProjectCompletedProcessor xong = new ProjectCompletedProcessor(_db);
            await xong.XuLyAsync(SeedEvents.Boc(SeedEvents.HoanThanh(sp), bayGio - sp.CompletedAgo.Value), ct);
            await _db.SaveChangesAsync(ct);
        }
    }
}
