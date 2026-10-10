using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Settings
{
    /// <summary>
    /// Dong bo ban sao setting cho mot service co DB:
    ///   - khoi dong: nap settings_replica vao bo nho TRUOC khi cac worker khac chay,
    ///     roi xin admin-svc phat lai toan bo (settings.snapshot_requested qua outbox);
    ///   - dinh ky (settings.reload_interval): nap lai tu DB — event chi toi MOT instance
    ///     cua service, cac instance khac thay doi qua lan nap nay.
    /// </summary>
    public sealed class SettingsReplicaSync<TDbContext> : BackgroundService
        where TDbContext : DbContext
    {
        private static readonly TimeSpan KhoangXinLai = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopes;
        private readonly SettingsStore _store;
        private readonly string _tenService;
        private readonly ILogger<SettingsReplicaSync<TDbContext>> _logger;

        public SettingsReplicaSync(
            IServiceScopeFactory scopes, SettingsStore store, string tenService, ILogger<SettingsReplicaSync<TDbContext>> logger)
        {
            if (scopes == null)
            {
                throw new ArgumentNullException(nameof(scopes));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopes = scopes;
            _store = store;
            _tenService = tenService;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // Nap XONG roi moi cho cac hosted service dang ky sau chay (outbox, consumer,
            // worker) — chung doc setting ngay vong dau.
            try
            {
                int so = await NapAsync(cancellationToken);
                await XinSnapshotAsync(cancellationToken);
                _logger.LogInformation("Setting: nap {So} gia tri tu ban sao, da xin admin-svc phat lai", so);
            }
            catch (Exception ex) when ((ex as OperationCanceledException) == null)
            {
                _logger.LogError(ex, "Khong nap duoc ban sao setting — dung gia tri khoi tao cho toi lan nap sau");
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTimeOffset lanXinCuoi = DateTimeOffset.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ChoTheoSetting.ChoAsync(_store, SettingKeys.SettingsReloadInterval, stoppingToken);
                    int so = await NapAsync(stoppingToken);

                    // Lan chay DAU TIEN cua service: queue settings-snapshot chua ton tai luc xin o
                    // StartAsync (consumer khai bao queue SAU), nen snapshot admin phat ra bi lo.
                    // Ban sao con rong thi xin lai — toi da moi 30 giay mot lan.
                    if (so == 0 && DateTimeOffset.UtcNow - lanXinCuoi >= KhoangXinLai)
                    {
                        await XinSnapshotAsync(stoppingToken);
                        lanXinCuoi = DateTimeOffset.UtcNow;
                        _logger.LogInformation("Setting: ban sao van rong — xin admin-svc phat lai lan nua");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nap lai ban sao setting that bai, thu lai vong sau");
                }
            }
        }

        private async Task<int> NapAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopes.CreateScope())
            {
                TDbContext db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                List<SettingReplica> ds = await db.Set<SettingReplica>().AsNoTracking().ToListAsync(ct);
                foreach (SettingReplica r in ds)
                {
                    _store.ApDung(r.Key, r.ValueJson, r.Version);
                }

                return ds.Count;
            }
        }

        private async Task XinSnapshotAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopes.CreateScope())
            {
                TDbContext db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
                outbox.Enqueue(EventEnvelope.Create(
                    _tenService,
                    Guid.CreateVersion7(),
                    new SettingsSnapshotRequested { Service = _tenService }));
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
