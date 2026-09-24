using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Mot dong duy nhat de bat outbox cho mot service.
    ///
    /// Muc dich: 13 service khong ai phai nho dang ky du 5 thu va dat dung
    /// vong doi cho tung cai. Dat sai vong doi la loi im lang — vd dang ky
    /// OutboxWriter thanh Singleton se lam no ghi vao mot DbContext khac voi
    /// cai ma code nghiep vu dang dung, va event roi ra ngoai transaction.
    /// </summary>
    public static class OutboxServiceCollectionExtensions
    {
        /// <summary>
        /// Dang ky day du phan outbox: cho GHI, cho GUI, va worker nen.
        /// </summary>
        /// <typeparam name="TDbContext">
        /// DbContext cua chinh service. Phai goi ApplyConfiguration cho
        /// OutboxMessageConfiguration trong OnModelCreating cua no.
        /// </typeparam>
        public static IServiceCollection AddOutbox<TDbContext>(
            this IServiceCollection services,
            IConfiguration configuration)
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

            services.Configure<OutboxOptions>(
                configuration.GetSection(OutboxOptions.SectionName));
            services.Configure<RabbitMqOptions>(
                configuration.GetSection(RabbitMqOptions.SectionName));

            // TimeProvider: de sau nay test tua duoc dong ho ma khong phai cho
            // thuc te troi qua. TryAdd vi ung dung co the da dang ky roi.
            if (!services.Contains(ServiceDescriptor.Singleton(TimeProvider.System)))
            {
                services.AddSingleton(TimeProvider.System);
            }

            // SCOPED, cung vong doi voi DbContext. Phai dung lambda vi
            // OutboxWriter nhan kieu co so DbContext, con ung dung dang ky kieu
            // cu the TDbContext — DI khong tu suy ra duoc moi lien he do.
            services.AddScoped<IOutboxWriter>(sp =>
                new OutboxWriter(sp.GetRequiredService<TDbContext>()));

            // SINGLETON: giu mot ket noi TCP toi RabbitMQ dung lai mai. Dang ky
            // Scoped se mo ket noi moi cho tung request — bat tay TCP cong AMQP
            // ton hang chuc mili giay moi lan.
            services.AddSingleton<IOutboxPublisher, RabbitMqOutboxPublisher>();

            services.AddHostedService<OutboxDispatcher<TDbContext>>();

            // Nua NHAN: chan xu ly trung khi bus giao lai message (VD-D-02).
            // Cung SCOPED va cung DbContext voi IOutboxWriter — ca hai phai ghi
            // vao cung mot change tracker thi moi commit chung transaction duoc.
            services.AddScoped<IIdempotencyGuard>(sp => new IdempotencyGuard(
                sp.GetRequiredService<TDbContext>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<IdempotencyGuard>>()));

            return services;
        }
    }
}
