using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Admin;
using Crowd.Project.Api.Services;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>tag.merged (admin-svc) → doi ma the trong nhan du an (goi y du an van khop ho so ky nang).</summary>
    public sealed class TagMergedProcessor : IEventProcessor<TagMerged>
    {
        private readonly ProjectService _duAn;
        private readonly ILogger<TagMergedProcessor> _logger;

        public TagMergedProcessor(ProjectService duAn, ILogger<TagMergedProcessor> logger)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _duAn = duAn;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<TagMerged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            TagMerged p = envelope.Payload;
            int doi = await _duAn.GopTheAsync(p.Kind, p.FromCode, p.ToCode, ct);
            _logger.LogInformation("Gop the {Kind} {From} → {To}: doi {So} du an", p.Kind, p.FromCode, p.ToCode, doi);
        }
    }
}
