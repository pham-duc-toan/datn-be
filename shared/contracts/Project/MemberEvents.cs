using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Project
{
    // Ba event nhan ban bang project_members sang task_db / annotation_db /
    // media_db (docs muc 3.8). Ban sao do la nen cua phan quyen chieu ngang
    // o moi service thuc thi (VD-S-14).

    public sealed record MemberAdded : IEventPayload
    {
        public static string EventType
        {
            get { return "member.added"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid UserId { get; init; }

        public required ProjectMemberRole Role { get; init; }
    }

    public sealed record MemberBlocked : IEventPayload
    {
        public static string EventType
        {
            get { return "member.blocked"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid UserId { get; init; }
    }

    /// <summary>Bo chan: thanh vien hoat dong tro lai.</summary>
    public sealed record MemberUnblocked : IEventPayload
    {
        public static string EventType
        {
            get { return "member.unblocked"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid UserId { get; init; }
    }

    public sealed record MemberRemoved : IEventPayload
    {
        public static string EventType
        {
            get { return "member.removed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid UserId { get; init; }
    }
}
