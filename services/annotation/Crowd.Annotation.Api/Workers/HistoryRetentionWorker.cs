using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Annotation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Annotation.Api.Workers
{
    /// <summary>
    /// Han giu nhat ky vong doi nhan (annotation_history, muc 3.14): dong cu hon annotation.history_retention
    /// bi xoa, TRU dong MOI NHAT cua moi nhan (van biet trang thai cuoi do ai chot, luc nao). Trang thai, nguoi
    /// duyet, ly do tu choi van nam tren chinh dong annotations; tien nam o so cai — khong mat gi de doi soat.
    /// 0 = khong bao gio xoa. Xoa theo lo de khong khoa bang lau.
    /// </summary>
    public sealed class HistoryRetentionWorker : BackgroundService
    {
        private const int Lo = 5000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<HistoryRetentionWorker> _logger;

        public HistoryRetentionWorker(IServiceScopeFactory scopeFactory, ISettings settings, TimeProvider clock, ILogger<HistoryRetentionWorker> logger)
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
                    TimeSpan han = _settings.ThoiGian(SettingKeys.AnnotationHistoryRetention);
                    if (han > TimeSpan.Zero)
                    {
                        DateTimeOffset moc = _clock.GetUtcNow() - han;
                        using (IServiceScope scope = _scopeFactory.CreateScope())
                        {
                            AnnotationDbContext db = scope.ServiceProvider.GetRequiredService<AnnotationDbContext>();
                            int tong = 0;
                            int xoa;
                            do
                            {
                                xoa = await db.Database.ExecuteSqlInterpolatedAsync($@"
                                    DELETE FROM annotation_history WHERE id IN (
                                        SELECT h.id FROM annotation_history h
                                        WHERE h.at < {moc}
                                          AND h.id <> (SELECT max(x.id) FROM annotation_history x WHERE x.annotation_id = h.annotation_id)
                                        LIMIT {Lo})", stoppingToken);
                                tong += xoa;
                            }
                            while (xoa == Lo && !stoppingToken.IsCancellationRequested);

                            if (tong > 0)
                            {
                                _logger.LogInformation("Xoa {SoDong} dong annotation_history cu hon {Moc}", tong, moc);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HistoryRetentionWorker loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.AnnotationHistoryCleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
