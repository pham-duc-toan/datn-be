using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    // identity-svc SE phat ba event nay (diem uy tin, cap do, khoa tai khoan);
    // project-svc nghe de dung ban sao labeler_cache phuc vu loc eligibility luc mo bai.

    /// <summary>Tai khoan bi khoa: project/gate/link deu phai chan (ledger hoi identity luc rut) (CATALOG).</summary>
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

    /// <summary>Admin mo khoa tai khoan. Nguoi nghe: task (cho nhan task lai), ledger, notification.</summary>
    public sealed record UserUnblocked : IEventPayload
    {
        public static string EventType
        {
            get { return "user.unblocked"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }
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
