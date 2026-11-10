using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Vong doi THU BAI cua du an (docs 3.18):
    ///
    ///   nhan bai ──(du nguoi | het han chot | ket thuc som)──▶ dong thu bai: du an → Dang cham,
    ///                                                          phat project.collection_finished (quality-svc)
    ///        quality-svc cham ──▶ project.graded ──▶ ghi ket qua bai + nhan, mo han khieu nai
    ///        het han khieu nai + khong con khieu nai mo ──▶ chot: du an → Hoan thanh, phat project.completed
    ///
    /// Truoc khi gop, task-svc phat project.collection_finished / project.settled roi project-svc NGHE de doi
    /// trang thai — hai event di hai queue, settled toi truoc thi project-svc phai nem loi cho giao lai. Nay
    /// doi trang thai du an NGAY trong cung transaction.
    ///
    /// KHONG SaveChanges va KHONG mo transaction: noi goi lo. Dot thu bai phai DUOC KHOA FOR UPDATE truoc
    /// (LamBaiQueries.KhoaDotAsync) — xep hang nguoi nop bai cuoi, worker het han, lenh ket thuc som: chi mot
    /// ben dong thu bai.
    /// </summary>
    public sealed class ThuBaiService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectEventPublisher _events;
        private readonly ISettings _settings;
        private readonly ILogger<ThuBaiService> _logger;

        public ThuBaiService(ProjectDbContext db, ProjectEventPublisher events, ISettings settings, ILogger<ThuBaiService> logger)
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
        /// Dong thu bai: du an Dang chay / Tam dung → Dang cham, bai dang lam do bi het han (khong tinh), phat
        /// project.collection_finished kem danh sach bai DA NOP va mau bi loai giua chung. Da dong roi → false.
        /// </summary>
        /// <param name="dot">Dot thu bai DA KHOA FOR UPDATE.</param>
        /// <param name="lyDo">du_suat | het_han | ket_thuc_som</param>
        public async Task<bool> DongThuBaiAsync(CollectionRound dot, string lyDo, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (dot == null)
            {
                throw new ArgumentNullException(nameof(dot));
            }

            if (!dot.DongThuBai(lyDo))
            {
                return false;
            }

            LabelingProject duAn = await _db.Projects.FirstAsync(p => p.Id == dot.ProjectId, ct);
            duAn.BatDauCham(luc);

            List<Attempt> dangLam = await LamBaiQueries.KhoaBaiDangLamAsync(_db, dot.ProjectId, null, ct);
            foreach (Attempt a in dangLam)
            {
                a.HetHan(luc);
            }

            List<Attempt> daNop = await _db.Attempts
                .Where(a => a.ProjectId == dot.ProjectId && a.State == AttemptState.Submitted)
                .OrderBy(a => a.SubmittedAt)
                .ToListAsync(ct);
            List<Guid> biLoai = await _db.Samples
                .Where(s => s.ProjectId == dot.ProjectId && s.ExcludedAt != null)
                .Select(s => s.Id)
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
                ProjectId = dot.ProjectId,
                Reason = lyDo,
                Attempts = ds,
                ExcludedSampleIds = biLoai,
                FinishedAt = luc,
            });

            _logger.LogInformation(
                "Du an {ProjectId} dong thu bai ({LyDo}) → Dang cham: {Nop} bai nop, {Huy} bai dang lam bi huy, {Loai} mau bi loai",
                dot.ProjectId, lyDo, daNop.Count, dangLam.Count, biLoai.Count);
            return true;
        }

        /// <summary>
        /// project.graded: ghi ket qua vao tung bai (nhan cua bai dat → Accepted vao ket qua, truot → Excluded — suy ra tu bai),
        /// mo han khieu nai (task.appeal_window). Han = 0 thi chot luon. Giao lai → khong lam gi.
        /// </summary>
        /// <param name="dot">Dot thu bai DA KHOA FOR UPDATE.</param>
        public async Task GhiKetQuaChamAsync(CollectionRound dot, ProjectGraded p, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (dot == null)
            {
                throw new ArgumentNullException(nameof(dot));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (!dot.GhiDaCham(p.GradedAt, _settings.ThoiGian(SettingKeys.TaskAppealWindow)))
            {
                return;
            }

            List<Guid> ids = p.Attempts.Select(x => x.AttemptId).ToList();
            Dictionary<Guid, Attempt> bai = await _db.Attempts
                .Where(a => ids.Contains(a.Id) && a.ProjectId == p.ProjectId)
                .ToDictionaryAsync(a => a.Id, ct);

            // Chi ghi ket qua vao BAI: trang thai cua tung nhan (Accepted / Excluded) suy ra tu bai
            // (LabelAnnotation.TrangThai), nen khong UPDATE nhan nao.
            foreach (GradedAttempt g in p.Attempts)
            {
                Attempt? a;
                if (bai.TryGetValue(g.AttemptId, out a))
                {
                    a.GhiKetQua(g.ComparableCount, g.MatchedCount, g.AgreementPercent, g.Passed, g.Rewarded, p.GradedAt);
                    _db.AttemptHistory.Add(AttemptHistoryEntry.Tao(
                        a.Id, a.ProjectId, g.Passed ? AttemptHistoryEntry.Dat : AttemptHistoryEntry.Truot, null,
                        g.AgreementPercent + "% giống đa số, " + g.GoldCorrect + "/" + g.GoldTotal + " câu vàng", p.GradedAt));
                }
                else
                {
                    _logger.LogWarning("project.graded co bai {AttemptId} khong thuoc du an {ProjectId} — bo qua", g.AttemptId, p.ProjectId);
                }
            }

            // identity tinh lai cap tu so cau duoc chap nhan (gRPC LabelerStatsData, 3.24) — chi dung khi ket qua da
            // nam tren attempts, nen phat o day chu khong de identity nghe thang project.graded.
            _events.Phat(caller, new ProjectGradingApplied
            {
                ProjectId = p.ProjectId,
                ApprovedLabelerIds = bai.Values.Where(a => a.Passed == true).Select(a => a.LabelerId).Distinct().ToList(),
            });

            await ThuChotAsync(dot, luc, caller, ct);
        }

        /// <summary>
        /// Chot so bai duoc thuong neu da HET HAN khieu nai va KHONG con khieu nai mo: du an Dang cham → Hoan
        /// thanh, phat project.completed {so bai duoc thuong} — ledger hoan ky quy con lai SAU KHI da tra du so
        /// do (NC-B-01). Tra ve true neu vua chot.
        /// </summary>
        /// <param name="dot">Dot thu bai DA KHOA FOR UPDATE.</param>
        public async Task<bool> ThuChotAsync(CollectionRound dot, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            if (dot == null)
            {
                throw new ArgumentNullException(nameof(dot));
            }

            if (!dot.GradedAt.HasValue || dot.SettledAt.HasValue || !dot.AppealDeadline.HasValue || luc < dot.AppealDeadline.Value)
            {
                return false;
            }

            bool conKhieuNai = await _db.Attempts.AnyAsync(
                a => a.ProjectId == dot.ProjectId && a.AppealState == AppealState.Open, ct);
            if (conKhieuNai)
            {
                return false;
            }

            // Doc tu entity da theo doi (ket qua vua ghi trong cung transaction chua luu).
            List<Attempt> daNop = await _db.Attempts
                .Where(a => a.ProjectId == dot.ProjectId && a.State == AttemptState.Submitted)
                .ToListAsync(ct);

            // So bai DUOC THUONG (toi da so suat) — ledger tra du so nay roi hoan phan ky quy con lai.
            int soThuong = daNop.Count(a => a.DuocThuong);
            if (!dot.Chot(soThuong, luc))
            {
                return false;
            }

            LabelingProject duAn = await _db.Projects.FirstAsync(p => p.Id == dot.ProjectId, ct);
            if (duAn.HoanThanh(luc))
            {
                _events.Phat(caller, new ProjectCompleted
                {
                    ProjectId = duAn.Id,
                    OwnerId = duAn.OwnerId,
                    PassedAttemptCount = soThuong,
                });
            }

            _logger.LogInformation(
                "Du an {ProjectId} chot ket qua → Hoan thanh: {SoThuong} bai duoc thuong, {SoDat} bai dat / {SoNop} bai nop",
                dot.ProjectId, soThuong, daNop.Count(a => a.DatCuoiCung), daNop.Count);
            return true;
        }
    }
}
