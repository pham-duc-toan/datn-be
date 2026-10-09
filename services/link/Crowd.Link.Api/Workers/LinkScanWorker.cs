using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Link.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Link.Api.Workers
{
    /// <summary>
    /// Quet link moi (Safe Browsing + ten mien bi chan) roi kich hoat / chan. Tach khoi
    /// request tao link: goi API ngoai cham hay loi khong lam cham / hong viec tao link,
    /// va loi tam thoi thi vong sau tu thu lai (link van o PendingScan).
    /// </summary>
    public sealed class LinkScanWorker : BackgroundService
    {
        private const int CoLo = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<LinkScanWorker> _logger;

        public LinkScanWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<LinkScanWorker> logger)
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
                        ModerationService m = scope.ServiceProvider.GetRequiredService<ModerationService>();
                        int so = await m.QuetMotLoAsync(CoLo, stoppingToken);
                        if (so > 0)
                        {
                            _logger.LogInformation("Quet xong {So} link", so);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Quet link loi, thu lai vong sau");
                }

                try
                {
                    await Task.Delay(_settings.ThoiGian(SettingKeys.LinkScanInterval), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
