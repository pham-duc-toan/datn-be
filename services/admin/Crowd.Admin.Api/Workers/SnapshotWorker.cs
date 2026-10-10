using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Services;
using Crowd.BuildingBlocks.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Admin.Api.Workers
{
    /// <summary>
    /// Phat lai toan bo setting dinh ky (settings.snapshot_interval): service nao lo mat
    /// event (dang tat, queue bi xoa) van dong bo lai ma khong can ai lam gi.
    /// </summary>
    public sealed class SnapshotWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ISettings _settings;
        private readonly ILogger<SnapshotWorker> _logger;

        public SnapshotWorker(IServiceScopeFactory scopes, ISettings settings, ILogger<SnapshotWorker> logger)
        {
            if (scopes == null)
            {
                throw new ArgumentNullException(nameof(scopes));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.SettingsSnapshotInterval, stoppingToken);

                    using (IServiceScope scope = _scopes.CreateScope())
                    {
                        SettingService service = scope.ServiceProvider.GetRequiredService<SettingService>();
                        await service.PhatSnapshotAsync(Guid.CreateVersion7(), null, true, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Phat snapshot setting dinh ky that bai, thu lai vong sau");
                }
            }
        }
    }
}
