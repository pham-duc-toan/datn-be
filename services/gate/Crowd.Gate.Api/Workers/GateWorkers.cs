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
using Crowd.Gate.Infrastructure.Source;
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

    /// <summary>
    /// WARM-UP (kien truc 3.32): nap GateCatalog tu view cua chu du lieu TRUOC khi gate nhan request. Hosted service
    /// khoi dong truoc Kestrel, nen StartAsync chua xong thi cong HTTP chua mo — 1000 request ap vao luc vua khoi dong
    /// khong gap bo nho rong. DB chu chua tra loi thi thu lai mai (log moi lan); tren K8s readinessProbe
    /// (/health/ready) giu pod ngoai load balancer suot luc do, pod cu van phuc vu.
    /// </summary>
    public sealed class CatalogWarmup : IHostedService
    {
        private static readonly TimeSpan ChoThuLai = TimeSpan.FromSeconds(2);

        private readonly GateCatalog _catalog;
        private readonly NguonCongLink _nguon;
        private readonly ILogger<CatalogWarmup> _logger;

        public CatalogWarmup(GateCatalog catalog, NguonCongLink nguon, ILogger<CatalogWarmup> logger)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _catalog = catalog;
            _nguon = nguon;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            int lan = 0;
            while (true)
            {
                lan++;
                try
                {
                    await _catalog.NapLaiAsync(_nguon, cancellationToken);
                    TomTatCatalog t = _catalog.TomTat();
                    _logger.LogInformation("Cong link san sang: {SoDuAn} du an, {SoLink} link (lan thu {Lan})", t.Projects.Count, t.Links, lan);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Chua nap duoc du lieu cong link (lan {Lan}) — chua nhan request, thu lai sau {Giay} giay", lan, ChoThuLai.TotalSeconds);
                }

                await Task.Delay(ChoThuLai, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Nap lai GateCatalog tu view cua chu du lieu moi gate.cache_refresh_interval (sau warm-up).</summary>
    public sealed class CatalogRefreshWorker : BackgroundService
    {
        private readonly GateCatalog _catalog;
        private readonly NguonCongLink _nguon;
        private readonly ISettings _settings;
        private readonly ILogger<CatalogRefreshWorker> _logger;

        public CatalogRefreshWorker(GateCatalog catalog, NguonCongLink nguon, ISettings settings, ILogger<CatalogRefreshWorker> logger)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _catalog = catalog;
            _nguon = nguon;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.GateCacheRefreshInterval, stoppingToken);
                    await _catalog.NapLaiAsync(_nguon, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nap lai du lieu cong link loi — giu ban cu trong bo nho, thu lai vong sau");
                }
            }
        }
    }
}
