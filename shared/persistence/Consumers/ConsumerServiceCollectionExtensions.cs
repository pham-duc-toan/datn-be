using System;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Persistence.Consumers
{
    public static class ConsumerServiceCollectionExtensions
    {
        /// <summary>
        /// Nghe mot loai event. Goi SAU AddOutbox (can IIdempotencyGuard va
        /// cau hinh RabbitMq ma AddOutbox da dang ky).
        ///
        ///     services.AddEventConsumer&lt;ProjectDbContext, EscrowReserved, EscrowReservedHandler&gt;(
        ///         configuration, "project-svc.escrow-reserved");
        /// </summary>
        /// <param name="tenQueue">
        /// Ten queue, dang &lt;service&gt;.&lt;viec&gt;. Cung la ten handler trong
        /// processed_events — DOI TEN la mat dau vet chong trung, nen dat mot lan
        /// roi giu nguyen.
        /// </param>
        public static IServiceCollection AddEventConsumer<TDbContext, TPayload, THandler>(
            this IServiceCollection services,
            IConfiguration configuration,
            string tenQueue)
            where TDbContext : DbContext
            where TPayload : class, IEventPayload
            where THandler : class, IEventProcessor<TPayload>
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            services.Configure<EventConsumerOptions>(
                configuration.GetSection(EventConsumerOptions.SectionName));

            services.AddScoped<THandler>();

            // Dung lambda vi constructor can ten queue — mot chuoi, DI khong tu
            // biet lay o dau.
            services.AddHostedService(sp => new EventConsumer<TDbContext, TPayload, THandler>(
                tenQueue,
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
                sp.GetRequiredService<IOptions<EventConsumerOptions>>(),
                sp.GetRequiredService<ILogger<EventConsumer<TDbContext, TPayload, THandler>>>()));

            return services;
        }
    }
}
