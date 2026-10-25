using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Don du lieu VAN HANH cua du an da dong (hoan thanh / huy): task, luot giu, bao loi, cau vang.
    ///
    /// Vi sao xoa duoc: du an dong la diem cuoi (khong mo lai), khong cap task nua. Thu con can ve sau
    /// nam o noi khac — nhan da nop / da duyet va ket qua o annotation-svc, tien o ledger-svc, mau o
    /// project-svc. Con bang tasks + assignments la bang LON NHAT cua he thong (moi mau x redundancy luot,
    /// kem ban nhap va nhan) va chi phuc vu viec cap task.
    ///
    /// BA BUOC, dung giua chung thi lan sau chay tiep (khong buoc nao lam hai lan):
    ///   1. Chup tien do cuoi vao ban sao du an (mot transaction, khoa dong ban sao) — tu day API tien do
    ///      tra ban chup, khong dem bang tasks nua.
    ///   2. Xoa theo LO (moi lo mot transaction ngan — khong giu khoa lau, khong phinh WAL).
    ///   3. Danh dau da don.
    /// </summary>
    public sealed class DonDuAnDaDongService
    {
        public const int KichThuocLo = 2000;

        private readonly TaskDbContext _db;
        private readonly LamBaiService _lamBai;
        private readonly TimeProvider _clock;
        private readonly ILogger<DonDuAnDaDongService> _logger;

        public DonDuAnDaDongService(TaskDbContext db, LamBaiService lamBai, TimeProvider clock, ILogger<DonDuAnDaDongService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (lamBai == null)
            {
                throw new ArgumentNullException(nameof(lamBai));
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
            _lamBai = lamBai;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>Cac du an da dong truoc <paramref name="han"/> ma chua don xong — cu nhat truoc.</summary>
        public Task<List<Guid>> DuAnCanDonAsync(DateTimeOffset han, int toiDa, CancellationToken ct)
        {
            return _db.ProjectSnapshots.AsNoTracking()
                .Where(x => x.Status == SnapshotStatus.Closed && x.DataPurgedAt == null && x.StatusChangedAt < han)
                .OrderBy(x => x.StatusChangedAt)
                .Select(x => x.ProjectId)
                .Take(toiDa)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Don mot du an. Khong tim thay → 404; chua dong → 409 (du_an_chua_dong); da don roi → tra ket qua cu
        /// (PurgedNow = false).
        /// </summary>
        public async Task<PurgeResponse> DonAsync(Guid projectId, CancellationToken ct)
        {
            // BUOC 1: khoa ban sao, chup tien do cuoi. FOR UPDATE: hai ban sao worker (hoac worker + admin)
            // khong cung chup; ben sau thay ban chup da co va di tiep buoc 2 (xoa theo lo von an toan khi chay doi).
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                ProjectSnapshot? duAn = await _db.ProjectSnapshots
                    .FromSqlRaw("SELECT * FROM project_snapshots WHERE project_id = {0} FOR UPDATE", projectId)
                    .FirstOrDefaultAsync(ct);

                if (duAn == null)
                {
                    throw new NotFoundException("Không tìm thấy dự án.");
                }

                if (duAn.Status != SnapshotStatus.Closed)
                {
                    throw new RuleViolationException("du_an_chua_dong", "Chỉ dọn dữ liệu vận hành của dự án đã đóng.");
                }

                if (duAn.DataPurgedAt.HasValue)
                {
                    return new PurgeResponse
                    {
                        ProjectId = projectId,
                        PurgedAt = duAn.DataPurgedAt.Value,
                        PurgedNow = false,
                        DeletedTasks = 0,
                        DeletedAnswers = 0,
                        DeletedReports = 0,
                        DeletedGoldSamples = 0,
                    };
                }

                if (duAn.FinalProgressJson == null)
                {
                    ProgressResponse tienDo = await _lamBai.TinhTienDoAsync(duAn, ct);
                    ProgressResponse banChup = new ProgressResponse
                    {
                        ProjectId = tienDo.ProjectId,
                        Slots = tienDo.Slots,
                        MaxParticipants = tienDo.MaxParticipants,
                        QuestionCount = tienDo.QuestionCount,
                        InProgress = tienDo.InProgress,
                        Submitted = tienDo.Submitted,
                        Expired = tienDo.Expired,
                        Cancelled = tienDo.Cancelled,
                        Phase = tienDo.Phase,
                        FinishReason = tienDo.FinishReason,
                        Deadline = tienDo.Deadline,
                        CollectionFinishedAt = tienDo.CollectionFinishedAt,
                        GradedAt = tienDo.GradedAt,
                        AppealDeadline = tienDo.AppealDeadline,
                        SettledAt = tienDo.SettledAt,
                        PassedCount = tienDo.PassedCount,
                        OpenAppeals = tienDo.OpenAppeals,

                        // Du an da dong: khong con uoc tinh.
                        EstimatedCompletionAt = null,
                        FrozenAt = _clock.GetUtcNow(),
                    };
                    duAn.ChupTienDoCuoi(JsonSerializer.Serialize(banChup, JsonSerializerOptions.Web));
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
            }

            // BUOC 2: xoa theo lo. Bao loi, cau tra loi cua cac bai (nhap / da nop — nhan da nop nam o
            // annotation-svc), roi cau hoi, cuoi la cau vang. Bang attempts GIU NGUYEN: ket qua cham, khieu nai.
            int baoLoi = await XoaBaoLoiAsync(projectId, ct);
            int traLoi = await XoaCauTraLoiAsync(projectId, ct);
            int task = 0;
            while (true)
            {
                int so = await XoaMotLoTaskAsync(projectId, ct);
                task = task + so;
                if (so < KichThuocLo)
                {
                    break;
                }
            }

            int cauVang = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM gold_samples WHERE project_id = {projectId}", ct);

            // BUOC 3: danh dau.
            DateTimeOffset luc = _clock.GetUtcNow();
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                ProjectSnapshot duAn = await _db.ProjectSnapshots
                    .FromSqlRaw("SELECT * FROM project_snapshots WHERE project_id = {0} FOR UPDATE", projectId)
                    .FirstAsync(ct);
                await _db.Entry(duAn).ReloadAsync(ct);
                duAn.DanhDauDaDon(luc);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                luc = duAn.DataPurgedAt.HasValue ? duAn.DataPurgedAt.Value : luc;
            }

            _logger.LogInformation(
                "Don du an da dong {ProjectId}: {Task} cau hoi, {TraLoi} cau tra loi, {BaoLoi} bao loi, {CauVang} cau vang",
                projectId, task, traLoi, baoLoi, cauVang);

            return new PurgeResponse
            {
                ProjectId = projectId,
                PurgedAt = luc,
                PurgedNow = true,
                DeletedTasks = task,
                DeletedAnswers = traLoi,
                DeletedReports = baoLoi,
                DeletedGoldSamples = cauVang,
            };
        }

        private async Task<int> XoaBaoLoiAsync(Guid projectId, CancellationToken ct)
        {
            int tong = 0;
            while (true)
            {
                int so = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM task_reports WHERE id IN (
                           SELECT id FROM task_reports WHERE project_id = {projectId} LIMIT {KichThuocLo})", ct);
                tong = tong + so;
                if (so < KichThuocLo)
                {
                    return tong;
                }
            }
        }

        private async Task<int> XoaCauTraLoiAsync(Guid projectId, CancellationToken ct)
        {
            int tong = 0;
            while (true)
            {
                int so = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM attempt_answers WHERE ctid IN (
                           SELECT x.ctid FROM attempt_answers x JOIN attempts a ON a.id = x.attempt_id
                            WHERE a.project_id = {projectId} LIMIT {KichThuocLo})", ct);
                tong = tong + so;
                if (so < KichThuocLo)
                {
                    return tong;
                }
            }
        }

        /// <summary>Mot lo cau hoi (toi da KichThuocLo) cua du an. Tra ve so cau da xoa.</summary>
        private async Task<int> XoaMotLoTaskAsync(Guid projectId, CancellationToken ct)
        {
            return await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM tasks WHERE id IN (
                       SELECT id FROM tasks WHERE project_id = {projectId} ORDER BY id LIMIT {KichThuocLo})", ct);
        }
    }
}
