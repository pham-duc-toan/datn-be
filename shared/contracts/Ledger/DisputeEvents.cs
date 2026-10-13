using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Ledger
{
    // Tranh chap (FM-06): doanh nghiep phan doi mot nhan DA DUYET trong luc tien con treo.
    // ledger-svc la chu — dong bang khoan treo va chi tien deu trong transaction cua ledger.

    /// <summary>Doanh nghiep mo tranh chap; khoan treo bi dong bang. Nguoi nghe: notification.</summary>
    public sealed record DisputeOpened : IEventPayload
    {
        public static string EventType
        {
            get { return "dispute.opened"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid DisputeId { get; init; }

        public required Guid AnnotationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required Guid BusinessId { get; init; }

        /// <summary>So tien dang treo cua nhan nay.</summary>
        public required long AmountVnd { get; init; }

        public required string Reason { get; init; }
    }

    /// <summary>
    /// Admin phan xu xong. RefundBusiness: khoan treo tra ve doanh nghiep, nhan bi loai (annotation
    /// danh dau tu choi, identity tru so nhan duoc duyet). PayLabeler: khoan treo chay tiep nhu thuong.
    /// </summary>
    public sealed record DisputeResolved : IEventPayload
    {
        public static string EventType
        {
            get { return "dispute.resolved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid DisputeId { get; init; }

        public required Guid AnnotationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        public required DisputeOutcome Outcome { get; init; }

        public required string Note { get; init; }
    }

    public enum DisputeOutcome
    {
        RefundBusiness,
        PayLabeler,
    }
}
