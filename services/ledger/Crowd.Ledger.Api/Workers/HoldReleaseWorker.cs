using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Api.Settings;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Ledger.Api.Workers
{
    /// <summary>
    /// Het thoi gian treo (3-7 ngay) → chuyen thu lao sang so du kha dung.
    ///
    /// Moi khoan treo MOT transaction rieng: mot khoan loi khong giu cac khoan
    /// khac lai. Khoa dong bang SKIP LOCKED — chay nhieu ban sao cung khong giai
    /// phong mot khoan hai lan (va but toan "hold:{id}" UNIQUE chan them lop nua).
    /// </summary>
    public sealed class HoldReleaseWorker : BackgroundService
    {
        private const int CoLo = 100;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly LedgerOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<HoldReleaseWorker> _logger;

        public HoldReleaseWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<LedgerOptions> options,
            TimeProvider clock,
            ILogger<HoldReleaseWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
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
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int so = await QuetAsync(stoppingToken);
                    if (so > 0)
                    {
                        _logger.LogInformation("Giai phong {So} khoan treo den han", so);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Giai phong treo loi, thu lai vong sau");
                }

                try
                {
                    await Task.Delay(_options.ChuKyGiaiPhong, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> QuetAsync(CancellationToken ct)
        {
            List<Guid> ids;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                LedgerDbContext db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                ids = await db.Holds
                    .Where(h => h.State == HoldState.Held && h.ReleaseAt <= bayGio)
                    .OrderBy(h => h.ReleaseAt)
                    .Select(h => h.Id)
                    .Take(CoLo)
                    .ToListAsync(ct);
            }

            int dem = 0;
            foreach (Guid id in ids)
            {
                if (await GiaiPhongMotAsync(id, ct))
                {
                    dem = dem + 1;
                }
            }

            return dem;
        }

        private async Task<bool> GiaiPhongMotAsync(Guid holdId, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                LedgerDbContext db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
                MoneyFlowService flow = scope.ServiceProvider.GetRequiredService<MoneyFlowService>();

                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    FundsHold? h = await db.Holds
                        .FromSqlRaw("SELECT * FROM holds WHERE id = {0} AND state = 'Held' FOR UPDATE SKIP LOCKED", holdId)
                        .FirstOrDefaultAsync(ct);

                    if (h == null || !h.DenHan(_clock.GetUtcNow()))
                    {
                        return false;
                    }

                    await flow.GiaiPhongTreoAsync(h, Caller.HeThong(Guid.CreateVersion7(), null), ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return true;
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }
        }
    }
}
