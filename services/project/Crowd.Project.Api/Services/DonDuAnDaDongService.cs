using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Don du lieu VAN HANH cua du an da dong (hoan thanh / huy): bao loi mau, ban nhap con sot.
    ///
    /// Du an dong la diem cuoi (khong mo lai). Thu con can ve sau GIU NGUYEN: bai lam (ket qua cham, khieu nai,
    /// lich su), nhan da nop (ket qua, file xuat), mau va cau vang (ket qua tro toi). Truoc khi gop, task-svc
    /// con phai xoa ca ban sao cau hoi (tasks) va cau vang (gold_samples) — nay khong con ban sao nao.
    ///
    /// BA BUOC, dung giua chung thi lan sau chay tiep (khong buoc nao lam hai lan):
    ///   1. Chup tien do cuoi vao dot thu bai (mot transaction, khoa dong) — tu day API tien do tra ban chup.
    ///   2. Xoa theo LO (moi lo mot transaction ngan — khong giu khoa lau, khong phinh WAL).
    ///   3. Danh dau da don.
    /// </summary>
    public sealed class DonDuAnDaDongService
    {
        public const int KichThuocLo = 2000;

        private readonly ProjectDbContext _db;
        private readonly LamBaiService _lamBai;
        private readonly TimeProvider _clock;
        private readonly ILogger<DonDuAnDaDongService> _logger;

        public DonDuAnDaDongService(ProjectDbContext db, LamBaiService lamBai, TimeProvider clock, ILogger<DonDuAnDaDongService> logger)
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

        /// <summary>Cac du an da dong truoc <paramref name="han"/> (da tung chay) ma chua don xong — cu nhat truoc.</summary>
        public Task<List<Guid>> DuAnCanDonAsync(DateTimeOffset han, int toiDa, CancellationToken ct)
        {
            return _db.Projects.AsNoTracking()
                .Where(p => (p.Status == ProjectStatus.Completed || p.Status == ProjectStatus.Cancelled)
                            && p.ClosedAt != null && p.ClosedAt < han
                            && _db.CollectionRounds.Any(d => d.ProjectId == p.Id && d.DataPurgedAt == null))
                .OrderBy(p => p.ClosedAt)
                .Select(p => p.Id)
                .Take(toiDa)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Don mot du an. Khong tim thay / chua tung chay → 404; chua dong → 409 (du_an_chua_dong); da don roi →
        /// tra ket qua cu (PurgedNow = false).
        /// </summary>
        public async Task<PurgeResponse> DonAsync(Guid projectId, CancellationToken ct)
        {
            // BUOC 1: khoa dot thu bai, chup tien do cuoi. FOR UPDATE: hai ban sao worker (hoac worker + admin)
            // khong cung chup; ben sau thay ban chup da co va di tiep buoc 2 (xoa theo lo von an toan khi chay doi).
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                CollectionRound? dot = await LamBaiQueries.KhoaDotAsync(_db, projectId, ct);
                LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
                if (dot == null || duAn == null)
                {
                    throw new NotFoundException("Không tìm thấy dự án.");
                }

                if (dot.DataPurgedAt.HasValue)
                {
                    return new PurgeResponse
                    {
                        ProjectId = projectId,
                        PurgedAt = dot.DataPurgedAt.Value,
                        PurgedNow = false,
                        DeletedDrafts = 0,
                        DeletedReports = 0,
                    };
                }

                ProgressResponse tienDo = await _lamBai.TinhTienDoAsync(duAn, dot, ct);
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

                dot.ChupTienDoCuoi(duAn, JsonSerializer.Serialize(banChup, JsonSerializerOptions.Web));
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            // BUOC 2: xoa theo lo. Bai lam, nhan, mau, cau vang GIU NGUYEN.
            int baoLoi = await XoaTheoLoAsync(
                projectId,
                p => _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM sample_reports WHERE id IN (
                           SELECT id FROM sample_reports WHERE project_id = {p} LIMIT {KichThuocLo})", ct));
            int nhap = await XoaTheoLoAsync(
                projectId,
                p => _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM attempt_drafts WHERE ctid IN (
                           SELECT x.ctid FROM attempt_drafts x JOIN attempts a ON a.id = x.attempt_id
                            WHERE a.project_id = {p} LIMIT {KichThuocLo})", ct));

            // BUOC 3: danh dau.
            DateTimeOffset luc = _clock.GetUtcNow();
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                CollectionRound dot = (await LamBaiQueries.KhoaDotAsync(_db, projectId, ct))!;
                await _db.Entry(dot).ReloadAsync(ct);
                dot.DanhDauDaDon(luc);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                luc = dot.DataPurgedAt.HasValue ? dot.DataPurgedAt.Value : luc;
            }

            _logger.LogInformation("Don du an da dong {ProjectId}: {BaoLoi} bao loi, {Nhap} cau tra loi nhap", projectId, baoLoi, nhap);

            return new PurgeResponse
            {
                ProjectId = projectId,
                PurgedAt = luc,
                PurgedNow = true,
                DeletedDrafts = nhap,
                DeletedReports = baoLoi,
            };
        }

        private static async Task<int> XoaTheoLoAsync(Guid projectId, Func<Guid, Task<int>> motLo)
        {
            int tong = 0;
            while (true)
            {
                int so = await motLo(projectId);
                tong = tong + so;
                if (so < KichThuocLo)
                {
                    return tong;
                }
            }
        }
    }
}
