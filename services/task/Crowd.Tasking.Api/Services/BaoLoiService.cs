using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Reports;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Bao task loi (FL-07).
    ///   - Labeler bao: luot lease tra lai pool (nhu bo qua), labeler khong nhan lai task nay, chu du
    ///     an thay bao loi. Du task.report_auto_exclude NGUOI KHAC NHAU bao cung mau → tu loai mau.
    ///   - Chu du an xem bao loi cua du an, bo qua (mau khong sao) hoac loai mau.
    /// Loai mau = huy task, CHI khi khong ai dang giu (nguoi dang lam van nop duoc, khong mat cong).
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

        public async Task<TaskReportResponse> BaoLoiAsync(Guid assignmentId, ReportTaskRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Guid labelerId = caller.LayUserId();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                Assignment? a = await TaskQueries.KhoaAssignmentAsync(_db, assignmentId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Khong tim thay luot nhan task.");
                }

                DateTimeOffset bayGio = _clock.GetUtcNow();
                if (!a.DangGiu(bayGio))
                {
                    throw new RuleViolationException("lease_khong_con", "Khong con giu task nay — khong bao loi duoc.");
                }

                TaskReport r = TaskReport.Tao(a.Id, a.TaskId, a.ProjectId, a.SampleId, labelerId, body.Reason, body.Note, bayGio);
                LabelingTask task = (await TaskQueries.KhoaTaskAsync(_db, new[] { a.TaskId }, ct))[0];
                a.BoQua(task, bayGio);
                _db.TaskReports.Add(r);

                // Du nguoi bao (tinh ca bao nay) → tu loai mau neu khong ai dang giu.
                int soNguoiBao = 1 + await _db.TaskReports.Where(x => x.TaskId == a.TaskId && x.LabelerId != labelerId)
                    .Select(x => x.LabelerId).Distinct().CountAsync(ct);
                if (soNguoiBao >= _settings.SoNguyen(SettingKeys.TaskReportAutoExclude) && task.HuyNeuKhongAiGiu(bayGio))
                {
                    await DanhDauDaLoaiAsync(a.TaskId, null, bayGio, ct);
                    r.XuLy(ReportState.Excluded, null, bayGio);
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
        /// Chu du an xu ly: dismiss = mau khong sao (chi bao nay); exclude = loai mau (moi bao cua task
        /// thanh excluded). Dang co nguoi giu task → 409 con_nguoi_dang_lam, thu lai sau.
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
                    if (task.State == TaskState.Open && !task.HuyNeuKhongAiGiu(bayGio))
                    {
                        throw new RuleViolationException("con_nguoi_dang_lam", "Dang co labeler giu mau nay — thu lai sau khi ho nop hoac het han.");
                    }

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
