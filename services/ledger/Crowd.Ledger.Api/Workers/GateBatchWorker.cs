using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Ledger.Api.Workers
{
    /// <summary>
    /// Che do batched (VD-M-08): moi ledger.gate_batch_interval gop cac luot cong link dang cho
    /// thanh MOT but toan moi du an. Moi lo mot transaction duoi khoa so cai. Lo day (= gate_batch_size)
    /// thi lam lo tiep ngay. Chay ca khi da chuyen ve perClick de xu ly not hang doi con lai.
    /// </summary>
    public sealed class GateBatchWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<GateBatchWorker> _logger;

        public GateBatchWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<GateBatchWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int toiDa = _settings.SoNguyen(SettingKeys.LedgerGateBatchSize);
                    int so;
                    do
                    {
                        so = await MotLoAsync(toiDa, stoppingToken);
                        if (so > 0)
                        {
                            _logger.LogInformation("Chi theo lo {So} luot vuot link", so);
                        }
                    }
                    while (so >= toiDa);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chi theo lo loi — luot van cho trong hang doi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.LedgerGateBatchInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> MotLoAsync(int toiDa, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                LedgerDbContext db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
                MoneyFlowService flow = scope.ServiceProvider.GetRequiredService<MoneyFlowService>();

                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    int so = await flow.ChiTheoLoAsync(toiDa, Caller.HeThong(Guid.CreateVersion7(), null), ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return so;
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }
        }
    }
}
