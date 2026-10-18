using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Reports;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Bao loi CAU HOI (FL-07) trong bai lam:
    ///   - Labeler bao mot cau trong bai DANG LAM bi loi (file hong, khong phu hop, mo ta khong ro) — van lam
    ///     tiep bai, chu du an thay bao loi. Du task.report_auto_exclude NGUOI KHAC NHAU bao cung mau → tu loai.
    ///   - Chu du an xem bao loi cua du an, bo qua (mau khong sao) hoac loai mau.
    /// Loai mau = cau hoi Cancelled: khong vao bai MOI, va bi bo khoi phan cham (project.collection_finished
    /// mang ExcludedSampleIds) — ai da tra loi cau do cung khong bi tinh.
    /// </summary>
    public sealed class BaoLoiService
    {
        private readonly TaskDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public BaoLoiService(TaskDbContext db, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _settings = settings;
            _clock = clock;
        }

        public async Task<TaskReportResponse> BaoLoiAsync(Guid attemptId, ReportTaskRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!body.SampleId.HasValue)
            {
                throw new InvalidValueException("thieu_mau", "Can truong sampleId cua cau bi loi.");
            }

            Guid labelerId = caller.LayUserId();
            Guid sampleId = body.SampleId.Value;

            var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                Attempt? a = await TaskQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Khong tim thay bai lam.");
                }

                DateTimeOffset bayGio = _clock.GetUtcNow();
                a.KiemConLamDuoc(bayGio);

                if (!a.CoCauHoi(sampleId))
                {
                    throw new NotFoundException("Cau hoi khong thuoc bai lam nay.");
                }

                LabelingTask? task = await _db.Tasks.FirstOrDefaultAsync(t => t.ProjectId == a.ProjectId && t.SampleId == sampleId, ct);
                if (task == null)
                {
                    throw new NotFoundException("Khong tim thay cau hoi.");
                }

                if (await _db.TaskReports.AnyAsync(r => r.AttemptId == a.Id && r.SampleId == sampleId, ct))
                {
                    throw new RuleViolationException("da_bao_loi", "Ban da bao loi cau nay.");
                }

                TaskReport r = TaskReport.Tao(a.Id, task.Id, a.ProjectId, sampleId, labelerId, body.Reason, body.Note, bayGio);
                _db.TaskReports.Add(r);

                // Du nguoi bao (tinh ca bao nay) → tu loai mau.
                int soNguoiBao = 1 + await _db.TaskReports.Where(x => x.TaskId == task.Id && x.LabelerId != labelerId)
                    .Select(x => x.LabelerId).Distinct().CountAsync(ct);

                if (soNguoiBao >= _settings.SoNguyen(SettingKeys.TaskReportAutoExclude))
                {
                    LabelingTask khoa = (await TaskQueries.KhoaTaskAsync(_db, new[] { task.Id }, ct))[0];
                    if (khoa.LoaiBo(bayGio))
                    {
                        await DanhDauDaLoaiAsync(task.Id, null, bayGio, ct);
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

        /// <summary>Bao loi cua du an — chu du an (ban sao project.published) hoac admin; nguoi khac → 404.</summary>
        public async Task<IReadOnlyList<TaskReportResponse>> DanhSachAsync(Guid projectId, string? status, Caller caller, CancellationToken ct)
        {
            await KiemChuDuAnAsync(projectId, caller, ct);

            IQueryable<TaskReport> q = _db.TaskReports.AsNoTracking().Where(r => r.ProjectId == projectId);
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

            List<TaskReport> ds = await q.OrderBy(r => r.CreatedAt).Take(500).ToListAsync(ct);
            List<Guid> taskIds = ds.Select(r => r.TaskId).Distinct().ToList();
            Dictionary<Guid, int> soNguoi = await _db.TaskReports.AsNoTracking()
                .Where(r => taskIds.Contains(r.TaskId))
                .GroupBy(r => r.TaskId)
                .Select(g => new { TaskId = g.Key, So = g.Select(x => x.LabelerId).Distinct().Count() })
                .ToDictionaryAsync(x => x.TaskId, x => x.So, ct);

            return ds.Select(r => TaoResponse(r, soNguoi[r.TaskId])).ToList();
        }

        /// <summary>
        /// Chu du an xu ly: dismiss = mau khong sao (chi bao nay); exclude = loai mau (moi bao cua cau thanh
        /// excluded) — cau bi bo khoi bai moi va khoi phan cham.
        /// </summary>
        public async Task<TaskReportResponse> XuLyAsync(Guid reportId, ResolveReportRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                TaskReport? r = await _db.TaskReports.FirstOrDefaultAsync(x => x.Id == reportId, ct);
                if (r == null)
                {
                    throw new NotFoundException("Khong tim thay bao loi.");
                }

                await KiemChuDuAnAsync(r.ProjectId, caller, ct);
                DateTimeOffset bayGio = _clock.GetUtcNow();
                Guid nguoiXuLy = caller.LayUserId();

                if (string.Equals(body.Action, "dismiss", StringComparison.OrdinalIgnoreCase))
                {
                    r.XuLy(ReportState.Dismissed, nguoiXuLy, bayGio);
                }
                else if (string.Equals(body.Action, "exclude", StringComparison.OrdinalIgnoreCase))
                {
                    LabelingTask task = (await TaskQueries.KhoaTaskAsync(_db, new[] { r.TaskId }, ct))[0];
                    task.LoaiBo(bayGio);
                    await DanhDauDaLoaiAsync(r.TaskId, nguoiXuLy, bayGio, ct);
                }
                else
                {
                    throw new InvalidValueException("hanh_dong_khong_hop_le", "action: dismiss | exclude.");
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                int soNguoi = await _db.TaskReports.Where(x => x.TaskId == r.TaskId).Select(x => x.LabelerId).Distinct().CountAsync(ct);
                return TaoResponse(r, soNguoi);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        private async Task DanhDauDaLoaiAsync(Guid taskId, Guid? nguoiXuLy, DateTimeOffset luc, CancellationToken ct)
        {
            List<TaskReport> mo = await _db.TaskReports.Where(x => x.TaskId == taskId && x.State == ReportState.Open).ToListAsync(ct);
            foreach (TaskReport x in mo)
            {
                x.XuLy(ReportState.Excluded, nguoiXuLy, luc);
            }
        }

        private async Task KiemChuDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            if (caller.IsAdmin)
            {
                return;
            }

            Guid uid = caller.LayUserId();
            if (!await _db.ProjectSnapshots.AnyAsync(p => p.ProjectId == projectId && p.OwnerId == uid, ct))
            {
                throw new NotFoundException("Khong tim thay du an.");
            }
        }

        private static TaskReportResponse TaoResponse(TaskReport r, int soNguoiBao)
        {
            string trangThai = r.State.ToString();
            return new TaskReportResponse
            {
                Id = r.Id,
                TaskId = r.TaskId,
                SampleId = r.SampleId,
                LabelerId = r.LabelerId,
                Reason = r.Reason,
                Note = r.Note,
                State = char.ToLowerInvariant(trangThai[0]) + trangThai.Substring(1),
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt,
                ReportsOnTask = soNguoiBao,
            };
        }
    }
}
