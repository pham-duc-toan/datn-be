using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.BuildingBlocks.Persistence.Consumers
{
    /// <summary>
    /// Nghiep vu xu ly MOT loai event. Service cai interface nay, con viec nhan
    /// message, doc envelope, chong trung, ack/nack do EventConsumer lo.
    ///
    /// GIAO KEO: KHONG goi SaveChanges ben trong. Handler chi sua entity va
    /// Enqueue event phat sinh; EventConsumer boc handler trong
    /// IIdempotencyGuard, guard goi SaveChanges MOT lan cho ca nghiep vu lan
    /// dau vet processed_events, roi commit chung (VD-D-02).
    ///
    /// Dang ky SCOPED — moi message mot ban, cung DbContext voi guard.
    /// </summary>
    public interface IEventProcessor<TPayload>
        where TPayload : class, IEventPayload
    {
        Task XuLyAsync(EventEnvelope<TPayload> envelope, CancellationToken ct);
    }
}
