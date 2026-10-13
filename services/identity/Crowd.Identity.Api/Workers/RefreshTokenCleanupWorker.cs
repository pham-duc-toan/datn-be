using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Workers
{
    /// <summary>
    /// Chay nen: dinh ky xoa refresh token het han (RefreshTokenCleanupService).
    /// Chu ky: setting identity.refresh_token_cleanup_interval (mac dinh 2 phut).
    /// Mot vong loi chi ghi log, vong sau thu lai — khong lam sap service.
    /// </summary>
    public sealed class RefreshTokenCleanupWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<RefreshTokenCleanupWorker> _logger;

        public RefreshTokenCleanupWorker(
            IServiceScopeFactory scopeFactory,
            ISettings settings,
            TimeProvider clock,
            ILogger<RefreshTokenCleanupWorker> logger)
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
                    _logger.LogError(ex, "Don refresh token het han loi — thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.IdentityRefreshTokenCleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task MotVongAsync(CancellationToken ct)
        {
            // Scope moi cho moi vong: DbContext la scoped, worker thi song suot doi service.
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                RefreshTokenCleanupService don = scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupService>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                int soToken = await don.XoaHetHanAsync(bayGio, ct);
                int soMa = await don.XoaMaTaiKhoanHetHanAsync(bayGio, ct);

                if (soToken > 0 || soMa > 0)
                {
                    _logger.LogInformation("Da xoa {SoToken} refresh token het han, {SoMa} ma xac thuc / dat lai mat khau het han", soToken, soMa);
                }
            }
        }
    }
}
