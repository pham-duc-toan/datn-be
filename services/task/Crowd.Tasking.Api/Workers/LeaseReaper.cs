using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Workers
{
    /// <summary>
    /// REAPER (docs 3.5): lease qua 15 phut ma chua nop thi tra task ve pool.
    ///
    /// Khong co reaper, labeler bo di giua chung se giu task mai mai va du an
    /// khong bao gio xong. Chay nhieu ban sao cung an toan: SKIP LOCKED chia
    /// viec, moi lease chi mot reaper xu ly.
    /// </summary>
    public sealed class LeaseReaper : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<LeaseReaper> _logger;

        public LeaseReaper(
            IServiceScopeFactory scopeFactory,
            ISettings settings,
            TimeProvider clock,
            ILogger<LeaseReaper> logger)
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
                    int soDaThu;
                    int coLo = _settings.SoNguyen(SettingKeys.TaskReaperBatchSize);
                    do
                    {
                        soDaThu = await QuetMotLoAsync(coLo, stoppingToken);
                        if (soDaThu > 0)
                        {
                            _logger.LogInformation("Reaper tra {So} lease qua han ve pool", soDaThu);
                        }
                    }
                    while (soDaThu == coLo);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reaper loi, thu lai vong sau");
                }

                try
                {
                    await Task.Delay(_settings.ThoiGian(SettingKeys.TaskReaperInterval), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> QuetMotLoAsync(int coLo, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
                LeaseRevoker revoker = scope.ServiceProvider.GetRequiredService<LeaseRevoker>();

                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    DateTimeOffset bayGio = _clock.GetUtcNow();
                    List<Assignment> quaHan = await TaskQueries.KhoaLeaseQuaHanAsync(db, bayGio, coLo, ct);

                    await revoker.HetHanAsync(quaHan, bayGio, ct);

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
    }
}
