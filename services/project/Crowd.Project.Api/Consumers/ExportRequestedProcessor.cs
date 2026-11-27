using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Project;
using Crowd.Project.Api.Services;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// Doanh nghiep bam tao file ket qua (export.requested, chinh project-svc phat qua outbox — kien truc 3.41) → gop ket
    /// qua, dung file, luu MinIO, chot job. Viec nam o ExportService.TaoFileAsync.
    ///
    /// KHONG SaveChanges: IdempotencyGuard luu ket qua job + processed_events trong mot transaction — giao lai sau khi da
    /// xong thi bo qua, giao lai giua chung thi lam lai tu dau (file ghi de cung khoa).
    /// </summary>
    public sealed class ExportRequestedProcessor : IEventProcessor<ExportRequested>
    {
        private readonly ExportService _xuat;

        public ExportRequestedProcessor(ExportService xuat)
        {
            if (xuat == null)
            {
                throw new ArgumentNullException(nameof(xuat));
            }

            _xuat = xuat;
        }

        public Task XuLyAsync(EventEnvelope<ExportRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _xuat.TaoFileAsync(envelope.Payload, ct);
        }
    }
}
