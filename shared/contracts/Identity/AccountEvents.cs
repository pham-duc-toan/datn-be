using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    // Tai khoan: xac thuc email (FC-01), dat lai mat khau (FC-03), thu hoi phien (VD-S-11),
    // huy hieu (FL-12). identity-svc phat.

    /// <summary>
    /// Can gui email xac thuc. Nguoi nghe: notification (gui email giao dich, KHONG tao thong bao
    /// trong ung dung). VerifyUrl chua ma mot lan — chi di trong mang noi bo, notification xoa noi
    /// dung email sau khi gui.
    /// </summary>
    public sealed record EmailVerificationRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "user.email_verification_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        public required string VerifyUrl { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>Can gui email dat lai mat khau. Nguoi nghe: notification (email giao dich).</summary>
    public sealed record PasswordResetRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "user.password_reset_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        public required string ResetUrl { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    /// <summary>
    /// Nguoi dung da xac thuc email. project va ledger giu ban sao "chua xac thuc" de chan publish /
    /// rut tien (FC-01).
    /// </summary>
    public sealed record UserEmailVerified : IEventPayload
    {
        public static string EventType
        {
            get { return "user.email_verified"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }
    }

    /// <summary>
    /// Moi phien dang nhap cua user phat truoc RevokedAt het hieu luc (khoa tai khoan, doi / dat lai
    /// mat khau, dang xuat moi thiet bi). Gateway tu choi access token co iat truoc moc nay (VD-S-11).
    /// </summary>
    public sealed record SessionsRevoked : IEventPayload
    {
        public static string EventType
        {
            get { return "user.sessions_revoked"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required DateTimeOffset RevokedAt { get; init; }

        /// <summary>blocked | password_changed | password_reset | logout_all</summary>
        public required string Reason { get; init; }
    }

    /// <summary>Labeler vua dat huy hieu (FL-12). Nguoi nghe: notification.</summary>
    public sealed record BadgeAwarded : IEventPayload
    {
        public static string EventType
        {
            get { return "badge.awarded"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        /// <summary>Ma huy hieu, vd "moc_100".</summary>
        public required string Badge { get; init; }

        /// <summary>Ten hien thi (tieng Viet co dau).</summary>
        public required string Name { get; init; }
    }
}
