using System;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Settings
{
    public static class SettingsServiceCollectionExtensions
    {
        /// <summary>
        /// Service CO database: ban sao settings_replica + nghe setting.changed /
        /// settings.snapshot + ISettings doc tu bo nho.
        ///
        /// GOI TRUOC AddOutbox / AddEventConsumer khac: hosted service dang ky truoc khoi
        /// dong truoc, nen setting duoc nap xong truoc khi worker dau tien chay.
        /// DbContext phai ApplyConfiguration(new SettingReplicaConfiguration()).
        /// </summary>
        public static IServiceCollection AddCrowdSettings<TDbContext>(
            this IServiceCollection services, IConfiguration configuration, string tenService)
            where TDbContext : DbContext
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

            services.AddSingleton<SettingsStore>();
            services.AddSingleton<ISettings>(sp => sp.GetRequiredService<SettingsStore>());

            services.AddHostedService(sp => new SettingsReplicaSync<TDbContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<SettingsStore>(),
                tenService,
                sp.GetRequiredService<ILogger<SettingsReplicaSync<TDbContext>>>()));

            services.AddEventConsumer<TDbContext, SettingChanged, SettingChangedProcessor<TDbContext>>(
                configuration, tenService + ".setting-changed");
            services.AddEventConsumer<TDbContext, SettingsSnapshot, SettingsSnapshotProcessor<TDbContext>>(
                configuration, tenService + ".settings-snapshot");

            return services;
        }

        /// <summary>Tien trinh KHONG co database (gateway): giu setting trong bo nho.</summary>
        public static IServiceCollection AddCrowdSettingsInMemory(
            this IServiceCollection services, IConfiguration configuration, string tenService)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
            services.AddSingleton<SettingsStore>();
            services.AddSingleton<ISettings>(sp => sp.GetRequiredService<SettingsStore>());
            services.AddHostedService(sp => new SettingsInMemoryListener(
                sp.GetRequiredService<SettingsStore>(),
                sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
                tenService,
                sp.GetRequiredService<ILogger<SettingsInMemoryListener>>()));
            return services;
        }
    }
}
