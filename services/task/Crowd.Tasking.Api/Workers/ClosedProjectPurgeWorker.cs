using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Tasking.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Workers
{
    /// <summary>
    /// Don du lieu van hanh cua du an da dong qua project.closed_data_retention (0 = khong bao gio don).
    ///
    /// Moi vong lay vai du an cu nhat chua don, don tung du an trong scope rieng (DonDuAnDaDongService).
    /// Chay nhieu ban sao cung an toan: buoc chup tien do khoa dong ban sao, buoc xoa theo lo chay doi
    /// chi la ben sau khong con gi de xoa.
    /// </summary>
    public sealed class ClosedProjectPurgeWorker : BackgroundService
    {
        /// <summary>So du an toi da moi vong — du an lon xoa lau, khong don dong mot luc.</summary>
        public const int DuAnMoiVong = 20;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<ClosedProjectPurgeWorker> _logger;

        public ClosedProjectPurgeWorker(
            IServiceScopeFactory scopeFactory,
            ISettings settings,
            TimeProvider clock,
            ILogger<ClosedProjectPurgeWorker> logger)
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
                    await DonMotVongAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Don du an da dong loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.OutboxCleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<int> DonMotVongAsync(CancellationToken ct)
        {
            TimeSpan giu = _settings.ThoiGian(SettingKeys.ProjectClosedDataRetention);
            if (giu <= TimeSpan.Zero)
            {
                return 0;
            }

            DateTimeOffset han = _clock.GetUtcNow() - giu;
            List<Guid> canDon;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                DonDuAnDaDongService don = scope.ServiceProvider.GetRequiredService<DonDuAnDaDongService>();
                canDon = await don.DuAnCanDonAsync(han, DuAnMoiVong, ct);
            }

            foreach (Guid projectId in canDon)
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    DonDuAnDaDongService don = scope.ServiceProvider.GetRequiredService<DonDuAnDaDongService>();
                    await don.DonAsync(projectId, ct);
                }
            }

            return canDon.Count;
        }
    }
}
