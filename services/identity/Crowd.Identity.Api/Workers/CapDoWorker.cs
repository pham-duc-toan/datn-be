using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Workers
{
    /// <summary>
    /// Chay nen (kien truc 3.24): tinh lai cap / huy hieu cho labeler trong hang cho level_dirty — so cau duoc
    /// chap nhan hoi project-svc qua gRPC. Moi vong lam het hang cho theo lo (CapDoService.LoTinhCap), roi cho
    /// setting identity.level_recalc_interval. project-svc khong tra loi → giu hang cho, vong sau thu lai.
    /// </summary>
    public sealed class CapDoWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<CapDoWorker> _logger;

        public CapDoWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<CapDoWorker> logger)
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
                    await MotVongAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (DichVuKhongSanSangException ex)
                {
                    _logger.LogWarning("Chua tinh lai cap duoc: {Loi} — vong sau thu lai", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tinh lai cap loi — thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.IdentityLevelRecalcInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task MotVongAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Scope moi cho moi lo: DbContext la scoped, worker thi song suot doi service.
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    int n = await scope.ServiceProvider.GetRequiredService<CapDoService>().TinhLaiLoAsync(ct);
                    if (n == 0)
                    {
                        return;
                    }

                    _logger.LogInformation("Da tinh lai cap cho {SoLabeler} labeler", n);
                    if (n < CapDoService.LoTinhCap)
                    {
                        return;
                    }
                }
            }
        }
    }
}
