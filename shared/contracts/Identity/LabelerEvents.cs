using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    // identity-svc SE phat ba event nay (diem uy tin, cap do, khoa tai khoan);
    // task-svc nghe de dung ban sao labeler_cache phuc vu loc eligibility.
    // Khai truoc de task-svc dung duoc ngay — giong escrow.* cua ledger.

    /// <summary>Tai khoan bi khoa: task/gate/link/ledger deu phai chan (CATALOG).</summary>
    public sealed record UserBlocked : IEventPayload
    {
        public static string EventType
        {
            get { return "user.blocked"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public string? Reason { get; init; }
    }

    /// <summary>
    /// Diem uy tin moi, thang 0-100. Mang GIA TRI MOI chu khong phai do chenh:
    /// consumer ghi de, va dung occurredAt de bo qua ban cu den tre (VD-D-05).
    /// </summary>
    public sealed record ReputationChanged : IEventPayload
    {
        public static string EventType
        {
            get { return "reputation.changed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required int Reputation { get; init; }
    }

    public sealed record LevelChanged : IEventPayload
    {
        public static string EventType
        {
            get { return "level.changed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required int Level { get; init; }
    }
}
