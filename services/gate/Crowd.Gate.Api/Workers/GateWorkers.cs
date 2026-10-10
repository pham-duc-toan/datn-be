using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Gate.Api.Services;
using Crowd.Gate.Infrastructure.ClickHouse;
using Crowd.Gate.Infrastructure.Persistence;
using Crowd.Gate.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Crowd.Gate.Api.Workers
{
    /// <summary>
    /// RELAY: Redis Stream gate:events → outbox (gate.solved, click.validated) + ClickHouse
    /// (click_events), THEO LO.
    ///
    /// Vi sao khong ghi outbox ngay trong request: duong nong khong cham Postgres (docs 3.6),
    /// va gop lo thi mot transaction / mot lan insert ClickHouse cho hang tram luot.
    ///
    /// Thu tu: outbox COMMIT → ClickHouse → ACK. Chet o giua thi lan sau doc lai muc chua ACK
    /// va lam lai: event trung bi chan o ben nhan (ledger theo ClickId, annotation theo
    /// assignment_id), dong ClickHouse trung bi ReplacingMergeTree gop theo click_id.
    /// </summary>
    public sealed class EventRelayWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RedisGateStore _redis;
        private readonly ClickStore _clicks;
        private readonly ISettings _settings;
        private readonly ILogger<EventRelayWorker> _logger;
        private readonly string _tenConsumer = Environment.MachineName + "-" + Environment.ProcessId;

        public EventRelayWorker(
            IServiceScopeFactory scopeFactory, RedisGateStore redis, ClickStore clicks, ISettings settings, ILogger<EventRelayWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (clicks == null)
            {
                throw new ArgumentNullException(nameof(clicks));
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
            _redis = redis;
            _clicks = clicks;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool khoiTao = false;

            // Lan dau doc lai muc da giao nhung chua ACK (relay chet lan truoc), roi moi doc muc moi.
            bool docLaiCu = true;

            while (!stoppingToken.IsCancellationRequested)
            {
                int so = 0;
                try
                {
                    if (!khoiTao)
                    {
                        await _redis.TaoNhomRelayAsync();
                        await _clicks.TaoBangAsync(stoppingToken);
                        khoiTao = true;
                    }

                    so = await ChuyenMotLoAsync(docLaiCu, stoppingToken);
                    if (docLaiCu && so == 0)
                    {
                        docLaiCu = false;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Relay gate loi — giu nguyen trong stream, thu lai vong sau");
                    docLaiCu = true;
                }

                // Lo day thi lam tiep ngay, khong nghi.
                if (so >= _settings.SoNguyen(SettingKeys.GateEventBatchSize))
                {
                    continue;
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.GateEventFlushInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> ChuyenMotLoAsync(bool docLaiCu, CancellationToken ct)
        {
            StreamEntry[] lo = await _redis.DocLoAsync(_tenConsumer, _settings.SoNguyen(SettingKeys.GateEventBatchSize), docLaiCu);
            if (lo.Length == 0)
            {
                return 0;
            }

            List<DongClick> dong = new List<DongClick>();
            RedisValue[] ids = new RedisValue[lo.Length];

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                GateDbContext db = scope.ServiceProvider.GetRequiredService<GateDbContext>();
                GateEventPublisher events = scope.ServiceProvider.GetRequiredService<GateEventPublisher>();

                for (int i = 0; i < lo.Length; i++)
                {
                    ids[i] = lo[i].Id;
                    RedisValue v = lo[i]["e"];
                    MucSuKien? m = v.IsNullOrEmpty ? null : JsonSerializer.Deserialize<MucSuKien>(v.ToString(), CrowdJson.Options);
                    if (m == null)
                    {
                        _logger.LogError("Muc stream {Id} hong — bo qua", lo[i].Id);
                        continue;
                    }

                    dong.Add(m.Row);
                    if (m.Solved != null && m.Solved.Labels.Count > 0)
                    {
                        events.PhatHeThong(m.Solved);
                    }

                    if (m.Click != null)
                    {
                        events.PhatHeThong(m.Click);
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            await _clicks.GhiAsync(dong, ct);
            await _redis.XacNhanAsync(ids);
            return lo.Length;
        }
    }

    /// <summary>Nap lai GateCatalog tu gate_db: dinh ky, va ngay khi consumer bao co thay doi.</summary>
    public sealed class CatalogRefreshWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GateCatalog _catalog;
        private readonly ISettings _settings;
        private readonly ILogger<CatalogRefreshWorker> _logger;

        public CatalogRefreshWorker(IServiceScopeFactory scopeFactory, GateCatalog catalog, ISettings settings, ILogger<CatalogRefreshWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
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
            _catalog = catalog;
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
                        await _catalog.NapLaiAsync(scope.ServiceProvider.GetRequiredService<GateDbContext>(), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nap bo nho dem gate loi — giu ban cu, thu lai vong sau");
                }

                try
                {
                    await _catalog.ChoAsync(_settings.ThoiGian(SettingKeys.GateCacheRefreshInterval), stoppingToken);

                    // Gom cac thay doi lien tiep (vd mot lo dataset.ingested nhieu event) va
                    // doi consumer commit xong.
                    await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
