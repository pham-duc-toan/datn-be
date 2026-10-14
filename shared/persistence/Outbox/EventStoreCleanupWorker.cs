using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Don hai bang ha tang su kien — khong don thi chung lon hon ca du lieu nghiep vu:
    ///   - outbox: event DA GUI qua outbox.retention (mac dinh 7 ngay). Event chua gui KHONG bao gio xoa.
    ///   - processed_events: dau vet chong xu ly trung qua consumers.processed_retention (14 ngay).
    ///
    /// Xoa theo LO voi FOR UPDATE SKIP LOCKED: khong giu khoa lau, khong tranh voi dispatcher dang gui
    /// hay consumer dang ghi (hai ben dung vao dong MOI, job nay chi dung dong CU), chay nhieu ban sao
    /// service cung luc khong xoa trung. Mot vong xoa toi da LoMoiVong lo roi nghi — bang ton dong lon
    /// (lan dau bat tinh nang) duoc don dan, khong don mot phat lam nghen DB.
    /// </summary>
    public sealed class EventStoreCleanupWorker<TDbContext> : BackgroundService
        where TDbContext : DbContext
    {
        public const int KichThuocLo = 2000;
        public const int LoMoiVong = 25;

        /// <summary>Service khong dang ky ISettings (test, cong cu): dung mac dinh nay.</summary>
        public static readonly TimeSpan GiuOutboxMacDinh = TimeSpan.FromDays(7);
        public static readonly TimeSpan GiuDaXuLyMacDinh = TimeSpan.FromDays(14);
        public static readonly TimeSpan ChuKyMacDinh = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeProvider _clock;
        private readonly ILogger<EventStoreCleanupWorker<TDbContext>> _logger;

        public EventStoreCleanupWorker(
            IServiceScopeFactory scopeFactory,
            TimeProvider clock,
            ILogger<EventStoreCleanupWorker<TDbContext>> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
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
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // ISettings la singleton (SettingsStore). Service khong dang ky setting → null, dung mac dinh.
            ISettings? settings;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                settings = scope.ServiceProvider.GetService<ISettings>();
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DonMotVongAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Don rac hong khong duoc lam chet service — vong sau thu lai.
                    _logger.LogError(ex, "Don outbox / processed_events that bai, thu lai vong sau");
                }

                try
                {
                    // Cho theo NHIP, doc lai setting moi nhip: admin rut chu ky thi co hieu luc ngay, khong
                    // ngu not chu ky cu (giong moi worker khac — xem ChoTheoSetting).
                    if (settings == null)
                    {
                        await Task.Delay(ChuKyMacDinh, stoppingToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ChoTheoSetting.ChoAsync(settings, SettingKeys.OutboxCleanupInterval, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>Mot vong don (doc lai thoi han giu moi vong). Tra ve tong so dong da xoa.</summary>
        public async Task<long> DonMotVongAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                ISettings? settings = scope.ServiceProvider.GetService<ISettings>();
                TimeSpan giuOutbox = settings == null ? GiuOutboxMacDinh : settings.ThoiGian(SettingKeys.OutboxRetention);
                TimeSpan giuDaXuLy = settings == null ? GiuDaXuLyMacDinh : settings.ThoiGian(SettingKeys.ConsumersProcessedRetention);

                TDbContext db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                DateTimeOffset hanOutbox = bayGio - giuOutbox;
                DateTimeOffset hanDaXuLy = bayGio - giuDaXuLy;

                long outbox = 0;
                for (int lo = 0; lo < LoMoiVong; lo++)
                {
                    int so = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM outbox WHERE id IN (
                            SELECT id FROM outbox
                            WHERE published_at IS NOT NULL AND published_at < {hanOutbox}
                            LIMIT {KichThuocLo} FOR UPDATE SKIP LOCKED)", ct).ConfigureAwait(false);
                    outbox += so;
                    if (so < KichThuocLo)
                    {
                        break;
                    }
                }

                long daXuLy = 0;
                for (int lo = 0; lo < LoMoiVong; lo++)
                {
                    int so = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM processed_events WHERE (event_id, handler) IN (
                            SELECT event_id, handler FROM processed_events
                            WHERE processed_at < {hanDaXuLy}
                            LIMIT {KichThuocLo} FOR UPDATE SKIP LOCKED)", ct).ConfigureAwait(false);
                    daXuLy += so;
                    if (so < KichThuocLo)
                    {
                        break;
                    }
                }

                if (outbox > 0 || daXuLy > 0)
                {
                    _logger.LogInformation(
                        "Don ha tang su kien: xoa {Outbox} event da gui truoc {HanOutbox:u}, {DaXuLy} dau vet da xu ly truoc {HanDaXuLy:u}",
                        outbox, hanOutbox, daXuLy, hanDaXuLy);
                }

                return outbox + daXuLy;
            }
        }
    }
}
