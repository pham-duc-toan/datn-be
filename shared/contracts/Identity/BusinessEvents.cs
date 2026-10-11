using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    // Xac minh doanh nghiep (KYB, FB-01 / FB-02 / FM-01). identity-svc la chu ho so doanh nghiep.

    /// <summary>Doanh nghiep nop ho so KYB — vao hang doi duyet cua admin. Nguoi nghe: notification.</summary>
    public sealed record BusinessKybSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "business.kyb_submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid BusinessId { get; init; }

        public required string CompanyName { get; init; }
    }

    /// <summary>
    /// Admin xac minh doanh nghiep. project-svc giu ban sao de cho phep publish du an;
    /// notification bao cho doanh nghiep.
    /// </summary>
    public sealed record BusinessVerified : IEventPayload
    {
        public static string EventType
        {
            get { return "business.verified"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid BusinessId { get; init; }

        public required DateTimeOffset VerifiedAt { get; init; }
    }

    /// <summary>Admin tu choi ho so KYB (kem ly do). Doanh nghiep sua va nop lai duoc.</summary>
    public sealed record BusinessKybRejected : IEventPayload
    {
        public static string EventType
        {
            get { return "business.kyb_rejected"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid BusinessId { get; init; }

        public required string Reason { get; init; }
    }
}
