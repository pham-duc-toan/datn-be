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
        private readonly LeaseService _lease;
        private readonly TimeProvider _clock;
        private readonly ILogger<DonDuAnDaDongService> _logger;

        public DonDuAnDaDongService(TaskDbContext db, LeaseService lease, TimeProvider clock, ILogger<DonDuAnDaDongService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
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
            _lease = lease;
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
                    throw new NotFoundException("Khong tim thay du an.");
                }

                if (duAn.Status != SnapshotStatus.Closed)
                {
                    throw new RuleViolationException("du_an_chua_dong", "Chi don du lieu van hanh cua du an da dong.");
                }

                if (duAn.DataPurgedAt.HasValue)
                {
                    return new PurgeResponse
                    {
                        ProjectId = projectId,
                        PurgedAt = duAn.DataPurgedAt.Value,
                        PurgedNow = false,
                        DeletedTasks = 0,
                        DeletedAssignments = 0,
                        DeletedReports = 0,
                        DeletedGoldSamples = 0,
                    };
                }

                if (duAn.FinalProgressJson == null)
                {
                    ProgressResponse tienDo = await _lease.TinhTienDoAsync(projectId, ct);
                    ProgressResponse banChup = new ProgressResponse
                    {
                        ProjectId = tienDo.ProjectId,
                        TotalTasks = tienDo.TotalTasks,
                        OpenTasks = tienDo.OpenTasks,
                        CompletedTasks = tienDo.CompletedTasks,
                        ExcludedTasks = tienDo.ExcludedTasks,
                        ActiveLeases = tienDo.ActiveLeases,
                        Submissions = tienDo.Submissions,
                        RequiredSubmissions = tienDo.RequiredSubmissions,
                        CancelledTasks = tienDo.CancelledTasks,

                        // Du an da dong: khong con toc do, khong con uoc tinh.
                        SubmissionsLast24h = 0,
                        EstimatedCompletionAt = null,
                        FrozenAt = _clock.GetUtcNow(),
                    };
                    duAn.ChupTienDoCuoi(JsonSerializer.Serialize(banChup, JsonSerializerOptions.Web));
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
            }

            // BUOC 2: xoa theo lo. Bao loi truoc (tro toi luot giu), roi luot giu + task cung lo, cuoi la cau vang.
            int baoLoi = await XoaBaoLoiAsync(projectId, ct);
            int luotGiu = 0;
            int task = 0;
            while (true)
            {
                int[] so = await XoaMotLoTaskAsync(projectId, ct);
                luotGiu = luotGiu + so[0];
                task = task + so[1];
                if (so[1] < KichThuocLo)
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
                "Don du an da dong {ProjectId}: {Task} task, {LuotGiu} luot giu, {BaoLoi} bao loi, {CauVang} cau vang",
                projectId, task, luotGiu, baoLoi, cauVang);

            return new PurgeResponse
            {
                ProjectId = projectId,
                PurgedAt = luc,
                PurgedNow = true,
                DeletedTasks = task,
                DeletedAssignments = luotGiu,
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

        /// <summary>
        /// Mot lo: chon toi da KichThuocLo task cua du an, xoa luot giu cua chung roi xoa chung — trong MOT
        /// transaction (hai cau cung nhin mot tap task nho ORDER BY id). Tra ve [so luot giu, so task].
        /// </summary>
        private async Task<int[]> XoaMotLoTaskAsync(Guid projectId, CancellationToken ct)
        {
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                int luotGiu = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM assignments WHERE task_id IN (
                           SELECT id FROM tasks WHERE project_id = {projectId} ORDER BY id LIMIT {KichThuocLo})", ct);
                int task = await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM tasks WHERE id IN (
                           SELECT id FROM tasks WHERE project_id = {projectId} ORDER BY id LIMIT {KichThuocLo})", ct);
                await tx.CommitAsync(ct);
                return new int[] { luotGiu, task };
            }
        }
    }
}
