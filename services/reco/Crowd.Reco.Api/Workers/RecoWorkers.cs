using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Reco.Api.Services;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.ClickHouse;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Crowd.Reco.Api.Workers
{
    /// <summary>
    /// WARM-UP (kien truc 3.32): nap bo nho du an TRUOC khi reco-svc nhan request (hosted service chay truoc khi Kestrel
    /// mo cong). project_db chua san sang thi thu lai moi 2 giay; chot thu hai la middleware 503 trong Program.
    /// </summary>
    public sealed class CatalogWarmup : IHostedService
    {
        private readonly CatalogGoiY _catalog;
        private readonly ILogger<CatalogWarmup> _logger;

        public CatalogWarmup(CatalogGoiY catalog, ILogger<CatalogWarmup> logger)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _catalog = catalog;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            for (int lan = 1; !cancellationToken.IsCancellationRequested; lan++)
            {
                try
                {
                    int so = await _catalog.NapAsync(true, cancellationToken);
                    _logger.LogInformation("Bo goi y da nap {So} du an dang nhan bai", so);
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogWarning(ex, "Warm-up bo goi y that bai (lan {Lan}) — thu lai sau 2 giay", lan);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Nap lai bo nho moi reco.cache_refresh_interval, hoac NGAY khi consumer bao du an mo / dong.</summary>
    public sealed class CatalogRefreshWorker : BackgroundService
    {
        private readonly CatalogGoiY _catalog;
        private readonly ISettings _settings;
        private readonly ILogger<CatalogRefreshWorker> _logger;

        public CatalogRefreshWorker(CatalogGoiY catalog, ISettings settings, ILogger<CatalogRefreshWorker> logger)
        {
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
                    await _catalog.ChoYeuCauAsync(_settings.ThoiGian(SettingKeys.RecoCacheRefreshInterval), stoppingToken);
                    await _catalog.NapAsync(false, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Giu ban cu — goi y van chay (so lieu cu toi da mot chu ky).
                    _logger.LogError(ex, "Nap lai bo nho goi y loi — giu ban cu");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// RELAY: Redis Stream reco:events → ClickHouse (reco_events, feed_served) + cong bo dem cho hanh vi FRONTEND (hien
    /// the / khong quan tam) theo lo — mot transaction Postgres cho ca lo, khong ghi DB tren duong request.
    /// Thu tu: Postgres COMMIT → ClickHouse → ACK. Chet giua chung thi lan sau doc lai muc chua ACK: ClickHouse gop ban
    /// trung theo event_id; bo dem co the cong lai mot lo nho (chap nhan — chi la tin hieu so thich, khong phai tien).
    /// </summary>
    public sealed class EventRelayWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RecoRedis _redis;
        private readonly RecoClickStore _click;
        private readonly CatalogGoiY _catalog;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<EventRelayWorker> _logger;
        private readonly string _tenConsumer = Environment.MachineName + "-" + Environment.ProcessId;

        public EventRelayWorker(
            IServiceScopeFactory scopeFactory,
            RecoRedis redis,
            RecoClickStore click,
            CatalogGoiY catalog,
            ISettings settings,
            TimeProvider clock,
            ILogger<EventRelayWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (click == null)
            {
                throw new ArgumentNullException(nameof(click));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
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
            _redis = redis;
            _click = click;
            _catalog = catalog;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool khoiTao = false;
            bool docLaiCu = true;

            while (!stoppingToken.IsCancellationRequested)
            {
                int so = 0;
                try
                {
                    if (!khoiTao)
                    {
                        await _redis.TaoNhomRelayAsync();
                        await _click.TaoBangAsync(stoppingToken);
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
                    _logger.LogError(ex, "Relay hanh vi goi y loi — giu nguyen trong stream, thu lai vong sau");
                    docLaiCu = true;
                }

                if (so >= _settings.SoNguyen(SettingKeys.RecoEventBatchSize))
                {
                    continue;
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.RecoEventFlushInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> ChuyenMotLoAsync(bool docLaiCu, CancellationToken ct)
        {
            StreamEntry[] lo = await _redis.DocLoAsync(_tenConsumer, _settings.SoNguyen(SettingKeys.RecoEventBatchSize), docLaiCu);
            if (lo.Length == 0)
            {
                return 0;
            }

            List<DongHanhVi> hanhVi = new List<DongHanhVi>();
            List<DongDaTra> daTra = new List<DongDaTra>();
            List<CongBoDem> cong = new List<CongBoDem>();
            RedisValue[] ids = new RedisValue[lo.Length];

            for (int i = 0; i < lo.Length; i++)
            {
                ids[i] = lo[i].Id;
                RedisValue v = lo[i]["e"];
                MucHangDoi? m = v.IsNullOrEmpty ? null : HanhViService.GiaiMa(v.ToString());
                if (m == null)
                {
                    continue;
                }

                if (m.Sv != null)
                {
                    daTra.Add(m.Sv);
                }

                if (m.Ev != null)
                {
                    hanhVi.Add(m.Ev);
                    if (m.TuFrontend)
                    {
                        DuAnGoiY? p = _catalog.Tim(m.Ev.ProjectId);
                        if (p != null)
                        {
                            foreach (ThayDoiBoDem x in HanhVi.ThayDoi(m.Ev.Kind, p))
                            {
                                cong.Add(new CongBoDem { UserId = m.Ev.UserId, Loai = x.Loai, Ma = x.Ma, WA = x.WA, WB = x.WB });
                            }
                        }
                    }
                }
            }

            if (cong.Count > 0)
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    RecoDbContext db = scope.ServiceProvider.GetRequiredService<RecoDbContext>();
                    var tx = await db.Database.BeginTransactionAsync(ct);
                    try
                    {
                        IReadOnlyCollection<Guid> doi = await BoDemStore.CongAsync(
                            db, cong, _clock.GetUtcNow(), _settings.SoNguyen(SettingKeys.RecoAffinityHalfLifeDays), ct);
                        await tx.CommitAsync(ct);
                        await _redis.XoaSoThichAsync(doi);
                    }
                    finally
                    {
                        await tx.DisposeAsync();
                    }
                }
            }

            await _click.GhiHanhViAsync(hanhVi, ct);
            await _click.GhiDaTraAsync(daTra, ct);
            await _redis.XacNhanAsync(ids);
            return lo.Length;
        }
    }

    /// <summary>Kiem manifest mo hinh moi reco.model_check_interval (ml-svc hoc moi dem) — loi thi giu mo hinh dang co.</summary>
    public sealed class ModelReloadWorker : BackgroundService
    {
        private readonly MoHinhDangChay _moHinh;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<ModelReloadWorker> _logger;

        public ModelReloadWorker(MoHinhDangChay moHinh, IServiceScopeFactory scopeFactory, ISettings settings, TimeProvider clock, ILogger<ModelReloadWorker> logger)
        {
            if (moHinh == null)
            {
                throw new ArgumentNullException(nameof(moHinh));
            }

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

            _moHinh = moHinh;
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
                    if (await _moHinh.KiemTraAsync(stoppingToken))
                    {
                        await GhiLichSuAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Kiem tra mo hinh goi y moi loi — giu mo hinh dang chay");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.RecoModelCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task GhiLichSuAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                RecoDbContext db = scope.ServiceProvider.GetRequiredService<RecoDbContext>();
                DateTimeOffset bayGio = _clock.GetUtcNow();
                List<ModelVersion> ds = new List<ModelVersion>();
                ds.Add(new ModelVersion
                {
                    Version = _moHinh.Production.PhienBan,
                    Kind = _moHinh.Production.Loai,
                    Slot = "production",
                    FileKey = _moHinh.KhoaProduction ?? string.Empty,
                    LoadedAt = bayGio,
                });
                IMoHinhGoiY? thu = _moHinh.Candidate;
                if (thu != null)
                {
                    ds.Add(new ModelVersion
                    {
                        Version = thu.PhienBan,
                        Kind = thu.Loai,
                        Slot = "candidate",
                        FileKey = _moHinh.KhoaCandidate ?? string.Empty,
                        LoadedAt = bayGio,
                    });
                }

                foreach (ModelVersion m in ds)
                {
                    bool co = await db.ModelVersions.AnyAsync(x => x.Version == m.Version && x.Slot == m.Slot, ct);
                    if (!co)
                    {
                        db.ModelVersions.Add(m);
                    }
                }

                await db.SaveChangesAsync(ct);
            }
        }
    }
}
