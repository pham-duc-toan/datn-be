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

        /// <summary>Nguoi duoc moi chap nhan loi moi cho (sau khi dang ky / dong y dieu khoan).</summary>
        public const string Invitation = "invitation";
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

    /// <summary>
    /// Chu du an moi nguoi nhung CHUA them thang duoc (email chua co tai khoan, hoac du an can nguoi do tu
    /// dong y canh bao / ky NDA). Nguoi nghe: notification — gui email moi (kem link dang ky neu chua co tai
    /// khoan) va thong bao trong ung dung neu da co.
    /// </summary>
    public sealed record ProjectInvitationCreated : IEventPayload
    {
        public static string EventType
        {
            get { return "project.invitation_created"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid InvitationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        /// <summary>Dia chi nhan thu moi (da chuan hoa).</summary>
        public required string Email { get; init; }

        /// <summary>null = email chua co tai khoan.</summary>
        public Guid? UserId { get; init; }

        /// <summary>Phai tu dong y canh bao noi dung / ky NDA truoc khi chap nhan.</summary>
        public required bool RequiresConsent { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
