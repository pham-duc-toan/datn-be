using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Workers
{
    /// <summary>
    /// Worker cua vong doi bai lam (docs 3.18), moi task.reaper_interval:
    ///   1. bai qua han giu (task.attempt_duration / han chot) ma chua nop → het han, mat suat;
    ///   2. du an QUA HAN CHOT chua dong thu bai → dong thu bai ("het_han"), phat project.collection_finished;
    ///   3. du an da cham, HET HAN KHIEU NAI, khong con khieu nai mo → chot, phat project.settled.
    ///
    /// Chay nhieu ban sao cung an toan: (1) SKIP LOCKED chia viec; (2)(3) khoa ban sao du an FOR UPDATE va
    /// domain chi dong / chot MOT lan.
    /// </summary>
    public sealed class BaiLamWorker : BackgroundService
    {
        private const int DuAnMoiVong = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<BaiLamWorker> _logger;

        public BaiLamWorker(IServiceScopeFactory scopeFactory, ISettings settings, TimeProvider clock, ILogger<BaiLamWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MotVongAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker bai lam loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.TaskReaperInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>Mot vong day du — public de test goi thang.</summary>
        public async Task MotVongAsync(CancellationToken ct)
        {
            int coLo = _settings.SoNguyen(SettingKeys.TaskReaperBatchSize);
            int n;
            do
            {
                n = await HetHanMotLoAsync(coLo, ct);
                if (n > 0)
                {
                    _logger.LogInformation("Het han {So} bai qua thoi gian giu", n);
                }
            }
            while (n == coLo);

            foreach (Guid id in await DuAnQuaHanChotAsync(ct))
            {
                await XuLyDuAnAsync(id, true, ct);
            }

            foreach (Guid id in await DuAnChoChotAsync(ct))
            {
                await XuLyDuAnAsync(id, false, ct);
            }
        }

        private async Task<int> HetHanMotLoAsync(int coLo, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    DateTimeOffset bayGio = _clock.GetUtcNow();
                    List<Attempt> quaHan = await TaskQueries.KhoaBaiQuaHanAsync(db, bayGio, coLo, ct);
                    foreach (Attempt a in quaHan)
                    {
                        a.HetHan(bayGio);
                    }

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return quaHan.Count;
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }
        }

        private async Task<List<Guid>> DuAnQuaHanChotAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                return await db.ProjectSnapshots.AsNoTracking()
                    .Where(p => p.IsConfigured && p.CollectionFinishedAt == null && p.Deadline != null && p.Deadline <= bayGio
                                && (p.Status == SnapshotStatus.Running || p.Status == SnapshotStatus.Paused))
                    .OrderBy(p => p.Deadline)
                    .Select(p => p.ProjectId)
                    .Take(DuAnMoiVong)
                    .ToListAsync(ct);
            }
        }

        private async Task<List<Guid>> DuAnChoChotAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                return await db.ProjectSnapshots.AsNoTracking()
                    .Where(p => p.GradedAt != null && p.SettledAt == null && p.AppealDeadline != null && p.AppealDeadline <= bayGio)
                    .OrderBy(p => p.AppealDeadline)
                    .Select(p => p.ProjectId)
                    .Take(DuAnMoiVong)
                    .ToListAsync(ct);
            }
        }

        /// <summary>Moi du an mot scope + transaction rieng — mot du an loi khong keo cac du an khac.</summary>
        private async Task XuLyDuAnAsync(Guid projectId, bool dongThuBai, CancellationToken ct)
        {
            try
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
                    ThuBaiService thuBai = scope.ServiceProvider.GetRequiredService<ThuBaiService>();
                    var tx = await db.Database.BeginTransactionAsync(ct);
                    try
                    {
                        ProjectSnapshot? duAn = await TaskQueries.KhoaDuAnAsync(db, projectId, ct);
                        if (duAn == null)
                        {
                            return;
                        }

                        DateTimeOffset bayGio = _clock.GetUtcNow();
                        Caller heThong = Caller.HeThong(Guid.CreateVersion7(), null);
                        bool doi = dongThuBai
                            ? await thuBai.DongThuBaiAsync(duAn, "het_han", bayGio, heThong, ct)
                            : await thuBai.ThuChotAsync(duAn, bayGio, heThong, ct);

                        if (doi)
                        {
                            await db.SaveChangesAsync(ct);
                        }

                        await tx.CommitAsync(ct);
                    }
                    finally
                    {
                        await tx.DisposeAsync();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Khong {Viec} duoc du an {ProjectId}, thu lai vong sau", dongThuBai ? "dong thu bai" : "chot", projectId);
            }
        }
    }
}
