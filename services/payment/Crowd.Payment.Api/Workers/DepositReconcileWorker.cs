using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Payment.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Workers
{
    /// <summary>
    /// Doi soat lenh nap qua cong (VD-M-09): IPN co the lac (cong goi luc payment dang khoi dong lai,
    /// mang loi, dev chay localhost cong khong goi vao duoc). Moi payment.deposit_reconcile_interval,
    /// lenh Pending qua payment.deposit_lookup_after duoc HOI NGUOC cong; qua payment.deposit_expire_after
    /// ma van chua tra thi danh dau het han. Logic nam o DepositService.DoiSoatAsync.
    ///
    /// Chay nhieu instance payment cung khong sao: chot thanh cong co RowVersion + UNIQUE(provider,
    /// provider_txn_id), hai instance cung chot thi mot ben thua va bo qua.
    /// </summary>
    public sealed class DepositReconcileWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<DepositReconcileWorker> _logger;

        public DepositReconcileWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<DepositReconcileWorker> logger)
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
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        DepositService service = scope.ServiceProvider.GetRequiredService<DepositService>();
                        int daChot = await service.DoiSoatAsync(stoppingToken);
                        if (daChot > 0)
                        {
                            _logger.LogInformation("Doi soat lenh nap: chot {SoLenh} lenh", daChot);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DepositReconcileWorker loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.PaymentDepositReconcileInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
