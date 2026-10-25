using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Settings;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Vong doi THU BAI cua du an (docs 3.18):
    ///
    ///   nhan bai ──(du suat | het han chot | ket thuc som)──▶ dong thu bai ──▶ project.collection_finished
    ///        quality-svc cham ──▶ project.graded ──▶ ghi ket qua, mo han khieu nai
    ///        het han khieu nai + khong con khieu nai mo ──▶ chot ──▶ project.settled {so nguoi dat}
    ///
    /// KHONG SaveChanges va KHONG mo transaction: noi goi lo (consumer: IdempotencyGuard; worker / API:
    /// transaction rieng). Ban sao du an phai DUOC KHOA FOR UPDATE truoc (TaskQueries.KhoaDuAnAsync) —
    /// xep hang nguoi nop bai cuoi, worker het han, lenh ket thuc som: chi mot ben dong thu bai.
    /// </summary>
    public sealed class ThuBaiService
    {
        private readonly TaskDbContext _db;
        private readonly TaskEventPublisher _events;
        private readonly ISettings _settings;
        private readonly ILogger<ThuBaiService> _logger;

        public ThuBaiService(TaskDbContext db, TaskEventPublisher events, ISettings settings, ILogger<ThuBaiService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _events = events;
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// Dong thu bai: bai dang lam do bi het han (khong tinh), phat project.collection_finished kem
        /// danh sach bai DA NOP va mau bi loai giua chung. Da dong roi → false.
        /// </summary>
        /// <param name="duAn">Ban sao du an DA KHOA FOR UPDATE.</param>
        /// <param name="lyDo">du_suat | het_han | ket_thuc_som</param>
        public async Task<bool> DongThuBaiAsync(ProjectSnapshot duAn, string lyDo, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!duAn.IsConfigured || !duAn.DongThuBai(lyDo, luc))
            {
                return false;
            }

            List<Attempt> dangLam = await TaskQueries.KhoaBaiDangLamAsync(_db, duAn.ProjectId, null, ct);
            foreach (Attempt a in dangLam)
            {
                a.HetHan(luc);
            }

            List<Attempt> daNop = await _db.Attempts
                .Where(a => a.ProjectId == duAn.ProjectId && a.State == AttemptState.Submitted)
                .OrderBy(a => a.SubmittedAt)
                .ToListAsync(ct);

            List<Guid> biLoai = await _db.Tasks
                .Where(t => t.ProjectId == duAn.ProjectId && t.State == TaskState.Cancelled)
                .Select(t => t.SampleId)
                .ToListAsync(ct);

            List<SubmittedAttemptRef> ds = new List<SubmittedAttemptRef>();
            foreach (Attempt a in daNop)
            {
                ds.Add(new SubmittedAttemptRef
                {
                    AttemptId = a.Id,
                    LabelerId = a.LabelerId,
                    GoldTotal = a.GoldTotal,
                    GoldCorrect = a.GoldCorrect,
                });
            }

            _events.Phat(caller, new ProjectCollectionFinished
            {
                ProjectId = duAn.ProjectId,
                Reason = lyDo,
                Attempts = ds,
                ExcludedSampleIds = biLoai,
                FinishedAt = luc,
            });

            _logger.LogInformation(
                "Du an {ProjectId} dong thu bai ({LyDo}): {Nop} bai nop, {Huy} bai dang lam bi huy, {Loai} mau bi loai",
                duAn.ProjectId, lyDo, daNop.Count, dangLam.Count, biLoai.Count);
            return true;
        }

        /// <summary>
        /// project.graded: ghi ket qua vao tung bai, mo han khieu nai (task.appeal_window). Han = 0 thi chot
        /// luon. Giao lai → khong lam gi.
        /// </summary>
        public async Task GhiKetQuaChamAsync(ProjectSnapshot duAn, ProjectGraded p, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (!duAn.GhiDaCham(p.GradedAt, _settings.ThoiGian(SettingKeys.TaskAppealWindow)))
            {
                return;
            }

            List<Guid> ids = p.Attempts.Select(x => x.AttemptId).ToList();
            Dictionary<Guid, Attempt> bai = await _db.Attempts
                .Where(a => ids.Contains(a.Id) && a.ProjectId == p.ProjectId)
                .ToDictionaryAsync(a => a.Id, ct);

            foreach (GradedAttempt g in p.Attempts)
            {
                Attempt? a;
                if (bai.TryGetValue(g.AttemptId, out a))
                {
                    a.GhiKetQua(g.ComparableCount, g.MatchedCount, g.AgreementPercent, g.Passed, g.Rewarded, p.GradedAt);
                }
                else
                {
                    _logger.LogWarning("project.graded co bai {AttemptId} khong thuoc du an {ProjectId} — bo qua", g.AttemptId, p.ProjectId);
                }
            }

            await ThuChotAsync(duAn, luc, caller, ct);
        }

        /// <summary>
        /// Chot so nguoi dat neu da HET HAN khieu nai va KHONG con khieu nai mo: phat project.settled — project-
        /// svc chuyen du an Hoan thanh, ledger hoan ky quy con lai sau khi tra du so nguoi dat. Tra ve true neu
        /// vua chot.
        /// </summary>
        public async Task<bool> ThuChotAsync(ProjectSnapshot duAn, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!duAn.GradedAt.HasValue || duAn.SettledAt.HasValue || !duAn.AppealDeadline.HasValue || luc < duAn.AppealDeadline.Value)
            {
                return false;
            }

            bool conKhieuNai = await _db.Attempts.AnyAsync(
                a => a.ProjectId == duAn.ProjectId && a.AppealState == AppealState.Open, ct);
            if (conKhieuNai)
            {
                return false;
            }

            // Doc tu entity da theo doi (ket qua vua ghi trong cung transaction chua luu).
            List<Attempt> daNop = await _db.Attempts
                .Where(a => a.ProjectId == duAn.ProjectId && a.State == AttemptState.Submitted)
                .ToListAsync(ct);
            // So bai DUOC THUONG (toi da so suat) — ledger tra du so nay roi hoan phan ky quy con lai.
            int soThuong = daNop.Count(a => a.DuocThuong);

            if (!duAn.Chot(soThuong, luc))
            {
                return false;
            }

            _events.Phat(caller, new ProjectSettled { ProjectId = duAn.ProjectId, PassedAttemptCount = soThuong });
            _logger.LogInformation(
                "Du an {ProjectId} chot ket qua: {SoThuong} bai duoc thuong, {SoDat} bai dat / {SoNop} bai nop",
                duAn.ProjectId, soThuong, daNop.Count(a => a.DatCuoiCung), daNop.Count);
            return true;
        }
    }
}
