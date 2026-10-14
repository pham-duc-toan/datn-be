using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Admin.Api.Workers
{
    /// <summary>
    /// Moi gio xoa nhat ky audit cu hon admin.audit_retention_days, theo lo (moi cau DELETE ngan, khong
    /// khoa bang lau; SKIP LOCKED de hai instance khong tranh nhau). Day la duong DUY NHAT xoa audit.
    /// </summary>
    public sealed class AuditRetentionWorker : BackgroundService
    {
        public const int KichThuocLo = 5000;
        private static readonly TimeSpan ChuKy = TimeSpan.FromHours(1);

        /// <summary>Bus giao lai message trong vai giay / phut; 1 gio la du rong.</summary>
        private static readonly TimeSpan GiuDauVetDaDem = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<AuditRetentionWorker> _logger;

        public AuditRetentionWorker(IServiceScopeFactory scopeFactory, ISettings settings, TimeProvider clock, ILogger<AuditRetentionWorker> logger)
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
                        _logger.LogInformation("Audit: xoa {So} dong qua thoi han luu", so);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Audit: don nhat ky cu loi — thu lai vong sau");
                }

                try
                {
                    await Task.Delay(ChuKy, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> DonAsync(CancellationToken ct)
        {
            DateTimeOffset moc = _clock.GetUtcNow() - TimeSpan.FromDays(_settings.SoNguyen(SettingKeys.AdminAuditRetentionDays));
            int tong = 0;
            int lanNay;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                AdminDbContext db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
                do
                {
                    lanNay = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM audit_events WHERE event_id IN (
                            SELECT event_id FROM audit_events WHERE occurred_at < {moc}
                            ORDER BY occurred_at LIMIT {KichThuocLo} FOR UPDATE SKIP LOCKED)", ct);
                    tong = tong + lanNay;
                }
                while (lanNay == KichThuocLo);

                // Dau vet "da dem" cua event khong luu chi tiet: chi can song qua khoang bus co the giao lai.
                DateTimeOffset mocDaDem = _clock.GetUtcNow() - GiuDauVetDaDem;
                do
                {
                    lanNay = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM audit_counted WHERE event_id IN (
                            SELECT event_id FROM audit_counted WHERE seen_at < {mocDaDem}
                            LIMIT {KichThuocLo} FOR UPDATE SKIP LOCKED)", ct);
                }
                while (lanNay == KichThuocLo);
            }

            return tong;
        }
    }
}
