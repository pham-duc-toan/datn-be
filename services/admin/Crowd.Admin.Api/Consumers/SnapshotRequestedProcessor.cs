using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Services;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Admin;
using Microsoft.Extensions.Logging;

namespace Crowd.Admin.Api.Consumers
{
    /// <summary>settings.snapshot_requested: mot service vua khoi dong xin toan bo setting → phat settings.snapshot.</summary>
    public sealed class SnapshotRequestedProcessor : IEventProcessor<SettingsSnapshotRequested>
    {
        private readonly SettingService _service;
        private readonly ILogger<SnapshotRequestedProcessor> _logger;

        public SnapshotRequestedProcessor(SettingService service, ILogger<SnapshotRequestedProcessor> logger)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _service = service;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<SettingsSnapshotRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            await _service.PhatSnapshotAsync(envelope.CorrelationId, envelope.EventId, false, ct);
            _logger.LogInformation("{Service} xin toan bo setting — da phat settings.snapshot", envelope.Payload.Service);
        }
    }
}
