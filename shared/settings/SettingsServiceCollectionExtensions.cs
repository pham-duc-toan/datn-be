using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Settings
{
    public static class SettingsServiceCollectionExtensions
    {
        /// <summary>
        /// Setting dong (admin-svc la chu — kien truc 3.32): doc THANG view v_settings cua admin_db
        /// (ConnectionStrings:SettingsRead, role settings_reader) vao bo nho, khong ban sao, khong event.
        ///
        /// GOI TRUOC AddOutbox / AddEventConsumer / worker khac: hosted service dang ky truoc khoi dong truoc, nen
        /// WARM-UP (SettingsWarmup) nap xong setting truoc khi worker dau tien chay va truoc khi Kestrel mo cong —
        /// admin_db chua tra loi thi service chua nhan viec nao (khong chay bang gia tri khoi tao sai).
        /// </summary>
        public static IServiceCollection AddCrowdSettings(this IServiceCollection services, IConfiguration configuration, string tenService)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(tenService))
            {
                throw new ArgumentException("Thieu ten service.", nameof(tenService));
            }

            string chuoi = configuration.GetConnectionString("SettingsRead")
                ?? throw new InvalidOperationException(
                    "Thieu ConnectionStrings:SettingsRead (role settings_reader doc view v_settings cua admin-svc).");

            services.AddSingleton<SettingsStore>();
            services.AddSingleton<ISettings>(sp => sp.GetRequiredService<SettingsStore>());
            services.AddSingleton(new SettingsSource(chuoi));
            services.AddHostedService(sp => new SettingsWarmup(
                sp.GetRequiredService<SettingsSource>(),
                sp.GetRequiredService<SettingsStore>(),
                tenService,
                sp.GetRequiredService<ILogger<SettingsWarmup>>()));
            services.AddHostedService(sp => new SettingsReloadWorker(
                sp.GetRequiredService<SettingsSource>(),
                sp.GetRequiredService<SettingsStore>(),
                sp.GetRequiredService<ILogger<SettingsReloadWorker>>()));
            return services;
        }
    }

    /// <summary>
    /// WARM-UP: nap setting tu admin_db truoc moi hosted service dang ky sau (outbox, consumer, worker) va truoc
    /// Kestrel. Chua nap duoc thi thu lai mai — service dung ngoai cua (tren K8s readinessProbe giu pod ngoai load
    /// balancer) thay vi chay bang gia tri khoi tao co the sai (vd phi, nguong dat).
    /// </summary>
    public sealed class SettingsWarmup : IHostedService
    {
        private static readonly TimeSpan ChoThuLai = TimeSpan.FromSeconds(2);

        private readonly SettingsSource _nguon;
        private readonly SettingsStore _store;
        private readonly string _tenService;
        private readonly ILogger<SettingsWarmup> _logger;

        public SettingsWarmup(SettingsSource nguon, SettingsStore store, string tenService, ILogger<SettingsWarmup> logger)
        {
            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _nguon = nguon;
            _store = store;
            _tenService = tenService;
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
                    int so = await _nguon.NapAsync(_store, cancellationToken);
                    _logger.LogInformation("{Service}: nap {So} setting tu admin-svc (lan thu {Lan})", _tenService, so, lan);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Service}: chua doc duoc setting tu admin-svc (lan {Lan}) — chua nhan viec, thu lai sau {Giay} giay",
                        _tenService, lan, ChoThuLai.TotalSeconds);
                }

                await Task.Delay(ChoThuLai, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Nap lai setting moi settings.reload_interval: moi instance tu doc (khong can event toi tung instance). Loi thi
    /// giu gia tri dang co trong bo nho, vong sau thu lai.
    /// </summary>
    public sealed class SettingsReloadWorker : BackgroundService
    {
        private readonly SettingsSource _nguon;
        private readonly SettingsStore _store;
        private readonly ILogger<SettingsReloadWorker> _logger;

        public SettingsReloadWorker(SettingsSource nguon, SettingsStore store, ILogger<SettingsReloadWorker> logger)
        {
            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _nguon = nguon;
            _store = store;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ChoTheoSetting.ChoAsync(_store, SettingKeys.SettingsReloadInterval, stoppingToken);
                    await _nguon.NapAsync(_store, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Doc lai setting tu admin-svc that bai — giu gia tri dang co, thu lai vong sau");
                }
            }
        }
    }
}
