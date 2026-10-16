using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    // Bao mat tai khoan: xac thuc hai lop (FC-04), xac minh danh tinh ca nhan (muc 2.9, FP-05,
    // VD-T-04). identity-svc phat.

    /// <summary>
    /// 2FA vua duoc bat / tat. Nguoi nghe: notification (email canh bao bao mat bat buoc — nguoi dung
    /// khong tu tat thi biet ngay tai khoan dang bi chiem).
    /// </summary>
    public sealed record UserTwoFactorChanged : IEventPayload
    {
        public static string EventType
        {
            get { return "user.two_factor_changed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required bool Enabled { get; init; }

        /// <summary>enabled | disabled | admin_reset</summary>
        public required string Reason { get; init; }
    }

    /// <summary>Nguoi dung nop ho so xac minh danh tinh — vao hang doi duyet cua admin.</summary>
    public sealed record IdentityVerificationSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "user.identity_verification_submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required string FullName { get; init; }
    }

    /// <summary>
    /// Admin xac minh danh tinh. Quyen di theo claim identity_verified / adult cua access token (khong
    /// service nao giu ban sao); event de bao nguoi dung va dung hang doi duyet chung (admin).
    /// </summary>
    public sealed record UserIdentityVerified : IEventPayload
    {
        public static string EventType
        {
            get { return "user.identity_verified"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required DateTimeOffset VerifiedAt { get; init; }
    }

    /// <summary>Admin tu choi ho so xac minh danh tinh (kem ly do). Nguoi dung sua va nop lai duoc.</summary>
    public sealed record UserIdentityRejected : IEventPayload
    {
        public static string EventType
        {
            get { return "user.identity_rejected"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required string Reason { get; init; }
    }

    /// <summary>
    /// S-07: admin vua lay link xem giay to dinh danh (giay phep KYB, anh CCCD). Khong ai consume de xu ly —
    /// admin-svc ghi vao nhat ky audit (chi ghi them): ai xem (actor), xem giay to cua ai, luc nao.
    /// </summary>
    public sealed record SensitiveDocumentViewed : IEventPayload
    {
        public static string EventType
        {
            get { return "sensitive_document.viewed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Chu giay to (doanh nghiep / nguoi xac minh).</summary>
        public required Guid SubjectUserId { get; init; }

        /// <summary>kyb_license | identity_document</summary>
        public required string DocumentKind { get; init; }

        /// <summary>So link xem vua cap (CCCD: mat truoc + mat sau).</summary>
        public required int LinkCount { get; init; }
    }
}
