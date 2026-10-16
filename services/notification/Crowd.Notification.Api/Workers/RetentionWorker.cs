using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Notification.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Notification.Api.Workers
{
    /// <summary>
    /// Han giu thong bao: xoa thong bao / email giao dich cu hon notification.retention_days — tru dong con
    /// email CHO GUI (Pending) — va ve realtime het han. Theo lo de khong khoa bang lau. Thong bao chi la ban
    /// tin; su that (tien, trang thai) nam o service chu, khong mat gi khi xoa.
    /// </summary>
    public sealed class RetentionWorker : BackgroundService
    {
        private const int Lo = 5000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<RetentionWorker> _logger;

        public RetentionWorker(IServiceScopeFactory scopeFactory, ISettings settings, TimeProvider clock, ILogger<RetentionWorker> logger)
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
                    int so = await DonAsync(stoppingToken);
                    if (so > 0)
                    {
                        _logger.LogInformation("Thong bao: xoa {So} dong qua han giu", so);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Don thong bao cu loi — thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.NotificationCleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<int> DonAsync(CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset moc = bayGio - TimeSpan.FromDays(_settings.SoNguyen(SettingKeys.NotificationRetentionDays));
            int tong = 0;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                int lan;
                do
                {
                    lan = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM notifications WHERE id IN (
                            SELECT id FROM notifications WHERE created_at < {moc} AND email_state <> 'Pending'
                            LIMIT {Lo} FOR UPDATE SKIP LOCKED)", ct);
                    tong += lan;
                }
                while (lan == Lo);

                do
                {
                    lan = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM transactional_emails WHERE id IN (
                            SELECT id FROM transactional_emails WHERE created_at < {moc} AND state <> 'Pending'
                            LIMIT {Lo} FOR UPDATE SKIP LOCKED)", ct);
                    tong += lan;
                }
                while (lan == Lo);

                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM realtime_tickets WHERE expires_at < {bayGio}", ct);
            }

            return tong;
        }
    }
}
