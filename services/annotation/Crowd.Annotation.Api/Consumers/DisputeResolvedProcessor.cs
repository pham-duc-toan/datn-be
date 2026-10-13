using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Consumers
{
    /// <summary>
    /// dispute.resolved (ledger, FM-06): doanh nghiep thang → nhan da duyet bi loai (khong vao ket
    /// qua, khong khieu nai lai). Labeler thang → khong doi gi. Giao lai → LoaiDoTranhChap tu bo qua.
    /// </summary>
    public sealed class DisputeResolvedProcessor : IEventProcessor<DisputeResolved>
    {
        private readonly AnnotationDbContext _db;

        public DisputeResolvedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<DisputeResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            DisputeResolved p = envelope.Payload;
            if (p.Outcome != DisputeOutcome.RefundBusiness)
            {
                return;
            }

            LabelAnnotation? a = await _db.Annotations.FirstOrDefaultAsync(x => x.Id == p.AnnotationId, ct);
            if (a == null)
            {
                // ledger chi tranh chap nhan da duyet o day — khong co la loi du lieu, de vao DLQ cho nguoi xem.
                throw new InvalidOperationException("Khong tim thay nhan " + p.AnnotationId + " cua tranh chap " + p.DisputeId + ".");
            }

            a.LoaiDoTranhChap(p.Note, envelope.OccurredAt);
        }
    }
}
