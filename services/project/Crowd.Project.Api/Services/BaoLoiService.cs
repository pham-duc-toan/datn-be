using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Reports;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Bao loi CAU HOI (FL-07) trong bai lam:
    ///   - Labeler bao mot cau trong bai DANG LAM bi loi (file hong, khong phu hop, mo ta khong ro) — van lam
    ///     tiep bai, chu du an thay bao loi. Du task.report_auto_exclude NGUOI KHAC NHAU bao cung mau → tu loai.
    ///   - Chu du an xem bao loi cua du an, bo qua (mau khong sao) hoac loai mau.
    /// Loai mau = samples.excluded_at: khong vao bai MOI, va bi bo khoi phan cham (project.collection_finished
    /// mang ExcludedSampleIds) — ai da tra loi cau do cung khong bi tinh.
    /// </summary>
    public sealed class BaoLoiService
    {
        public const string LoaiDoChuDuAn = "report_accepted";
        public const string LoaiDoNhieuNguoiBao = "auto_reports";

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public BaoLoiService(ProjectDbContext db, ProjectAccessService access, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _settings = settings;
            _clock = clock;
        }

        public async Task<SampleReportResponse> BaoLoiAsync(Guid attemptId, ReportSampleRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!body.SampleId.HasValue)
            {
                throw new InvalidValueException("thieu_mau", "Cần trường sampleId của câu bị lỗi.");
            }

            Guid labelerId = caller.LayUserId();
            Guid sampleId = body.SampleId.Value;

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                Attempt? a = await LamBaiQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Không tìm thấy bài làm.");
                }

                DateTimeOffset bayGio = _clock.GetUtcNow();
                a.KiemConLamDuoc(bayGio);

                if (!a.CoCauHoi(sampleId))
                {
                    throw new NotFoundException("Câu hỏi không thuộc bài làm này.");
                }

                if (await _db.SampleReports.AnyAsync(r => r.AttemptId == a.Id && r.SampleId == sampleId, ct))
                {
                    throw new RuleViolationException("da_bao_loi", "Bạn đã báo lỗi câu này.");
                }

                SampleReport r = SampleReport.Tao(a.Id, a.ProjectId, sampleId, labelerId, body.Reason, body.Note, bayGio);
                _db.SampleReports.Add(r);

                // Du nguoi bao (tinh ca bao nay) → tu loai mau.
                int soNguoiBao = 1 + await _db.SampleReports
                    .Where(x => x.ProjectId == a.ProjectId && x.SampleId == sampleId && x.LabelerId != labelerId)
                    .Select(x => x.LabelerId).Distinct().CountAsync(ct);
                if (soNguoiBao >= _settings.SoNguyen(SettingKeys.TaskReportAutoExclude))
                {
                    Sample? mau = await LamBaiQueries.KhoaMauAsync(_db, a.ProjectId, sampleId, ct);
                    if (mau != null && mau.LoaiBo(LoaiDoNhieuNguoiBao, bayGio))
                    {
                        await DanhDauDaLoaiAsync(a.ProjectId, sampleId, null, bayGio, ct);
                        r.XuLy(ReportState.Excluded, null, bayGio);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return TaoResponse(r, soNguoiBao);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Bao loi cua du an — chu du an hoac admin.</summary>
        public async Task<IReadOnlyList<SampleReportResponse>> DanhSachAsync(Guid projectId, string? status, Caller caller, CancellationToken ct)
        {
            await _access.LayChoChuAsync(projectId, caller, ct);

            IQueryable<SampleReport> q = _db.SampleReports.AsNoTracking().Where(r => r.ProjectId == projectId);
            string loc = string.IsNullOrWhiteSpace(status) ? "open" : status;
            if (loc != "all")
            {
                ReportState trangThai;
                if (!Enum.TryParse(loc, true, out trangThai) || !Enum.IsDefined(trangThai))
                {
                    throw new InvalidValueException("trang_thai_khong_hop_le", "status: open | dismissed | excluded | all.");
                }

                q = q.Where(r => r.State == trangThai);
            }

            List<SampleReport> ds = await q.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
            List<Guid> mauIds = ds.Select(r => r.SampleId).Distinct().ToList();
            Dictionary<Guid, int> soNguoi = await _db.SampleReports.AsNoTracking()
                .Where(r => r.ProjectId == projectId && mauIds.Contains(r.SampleId))
                .GroupBy(r => r.SampleId)
                .Select(g => new { SampleId = g.Key, So = g.Select(x => x.LabelerId).Distinct().Count() })
                .ToDictionaryAsync(x => x.SampleId, x => x.So, ct);

            return ds.Select(r => TaoResponse(r, soNguoi[r.SampleId])).ToList();
        }

        /// <summary>
        /// Chu du an xu ly: dismiss = mau khong sao (chi bao nay); exclude = loai mau (moi bao cua cau thanh
        /// excluded) — cau bi bo khoi bai moi va khoi phan cham.
        /// </summary>
        public async Task<SampleReportResponse> XuLyAsync(Guid reportId, ResolveReportRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                SampleReport? r = await _db.SampleReports.FirstOrDefaultAsync(x => x.Id == reportId, ct);
                if (r == null)
                {
                    throw new NotFoundException("Không tìm thấy báo lỗi.");
                }

                await _access.LayChoChuAsync(r.ProjectId, caller, ct);
                DateTimeOffset bayGio = _clock.GetUtcNow();
                Guid nguoiXuLy = caller.LayUserId();

                if (string.Equals(body.Action, "dismiss", StringComparison.OrdinalIgnoreCase))
                {
                    r.XuLy(ReportState.Dismissed, nguoiXuLy, bayGio);
                }
                else if (string.Equals(body.Action, "exclude", StringComparison.OrdinalIgnoreCase))
                {
                    Sample? mau = await LamBaiQueries.KhoaMauAsync(_db, r.ProjectId, r.SampleId, ct);
                    if (mau != null)
                    {
                        mau.LoaiBo(LoaiDoChuDuAn, bayGio);
                    }

                    await DanhDauDaLoaiAsync(r.ProjectId, r.SampleId, nguoiXuLy, bayGio, ct);
                }
                else
                {
                    throw new InvalidValueException("hanh_dong_khong_hop_le", "action: dismiss | exclude.");
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                int soNguoi = await _db.SampleReports
                    .Where(x => x.ProjectId == r.ProjectId && x.SampleId == r.SampleId)
                    .Select(x => x.LabelerId).Distinct().CountAsync(ct);
                return TaoResponse(r, soNguoi);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        private async Task DanhDauDaLoaiAsync(Guid projectId, Guid sampleId, Guid? nguoiXuLy, DateTimeOffset luc, CancellationToken ct)
        {
            List<SampleReport> mo = await _db.SampleReports
                .Where(x => x.ProjectId == projectId && x.SampleId == sampleId && x.State == ReportState.Open)
                .ToListAsync(ct);
            foreach (SampleReport x in mo)
            {
                x.XuLy(ReportState.Excluded, nguoiXuLy, luc);
            }
        }

        private static SampleReportResponse TaoResponse(SampleReport r, int soNguoiBao)
        {
            string trangThai = r.State.ToString();
            return new SampleReportResponse
            {
                Id = r.Id,
                SampleId = r.SampleId,
                LabelerId = r.LabelerId,
                Reason = r.Reason,
                Note = r.Note,
                State = char.ToLowerInvariant(trangThai[0]) + trangThai.Substring(1),
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt,
                ReportsOnSample = soNguoiBao,
            };
        }
    }
}
