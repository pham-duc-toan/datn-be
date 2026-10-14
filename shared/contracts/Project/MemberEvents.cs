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

        /// <summary>
        /// Duong vao (them sau, khong bat buoc — ban cu khong co): owner_invite (chu du an moi
        /// truc tiep), invite_code, join, entrance_test, join_request. notification-svc chi bao
        /// cho nguoi duoc moi khi la owner_invite; cac service nhan ban bo qua truong nay.
        /// </summary>
        public string? Via { get; init; }

        /// <summary>Ten du an luc vao — de thong bao co ten (khong bat buoc).</summary>
        public string? ProjectName { get; init; }
    }

    /// <summary>Gia tri cua MemberAdded.Via.</summary>
    public static class MemberJoinVia
    {
        public const string OwnerInvite = "owner_invite";
        public const string InviteCode = "invite_code";
        public const string Join = "join";
        public const string EntranceTest = "entrance_test";
        public const string JoinRequest = "join_request";
    }

    /// <summary>Labeler gui don xin tham gia du an che do duyet → bao chu du an.</summary>
    public sealed record JoinRequestSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "join_request.submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid RequestId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        public required Guid OwnerId { get; init; }

        public required Guid UserId { get; init; }

        /// <summary>Don sinh tu bai test dau vao: diem dat.</summary>
        public int? ScorePercent { get; init; }
    }

    /// <summary>Chu du an duyet / tu choi don → bao labeler. Duyet thi member.added di kem.</summary>
    public sealed record JoinRequestDecided : IEventPayload
    {
        public static string EventType
        {
            get { return "join_request.decided"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid RequestId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        public required Guid UserId { get; init; }

        public required bool Approved { get; init; }

        public string? Reason { get; init; }
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
