using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Link
{
    // link-svc (P2): link rut gon cua nguoi chia se (sharer). gate-svc giu BAN SAO
    // link dang hoat dong de phuc vu trang vuot link ma khong goi HTTP sang link-svc.

    /// <summary>
    /// Link da qua kiem duyet (blacklist + quet URL) va bat dau phuc vu. Phat lai moi
    /// lan link duoc bat lai hoac doi tuy chon — ban sao ghi de theo LinkId.
    /// </summary>
    public sealed record LinkActivated : IEventPayload
    {
        public static string EventType
        {
            get { return "link.activated"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid LinkId { get; init; }

        /// <summary>Ma ngan (hoac alias) — duong dan /g/{code}.</summary>
        public required string Code { get; init; }

        public required Guid OwnerId { get; init; }

        public required string DestinationUrl { get; init; }

        /// <summary>Bam PBKDF2 cua mat khau link (FS-06). null = khong dat mat khau.</summary>
        public string? PasswordHash { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public Guid? CampaignId { get; init; }

        /// <summary>sha256(ip + muoi) cua nguoi tao link — gate chan tu vuot link cua chinh minh (VD-L-04).</summary>
        public string? CreatorIpHash { get; init; }
    }

    /// <summary>Link ngung phuc vu: chu link xoa, het han, hoac admin vo hieu hoa vi vi pham.</summary>
    public sealed record LinkDisabled : IEventPayload
    {
        public static string EventType
        {
            get { return "link.disabled"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid LinkId { get; init; }

        public required Guid OwnerId { get; init; }

        public required string Reason { get; init; }

        /// <summary>true = vi pham da xac nhan: ledger GIU LAI doanh thu dang treo cua link (VD-L-01).</summary>
        public required bool WithholdRevenue { get; init; }
    }

    /// <summary>
    /// Link vua VUOT nguong bao cao (setting link.report_review_threshold) — vao hang doi kiem duyet cua admin.
    /// Ket thuc bang link.disabled (vi pham) hoac link.review_dismissed (bo qua bao cao).
    /// </summary>
    public sealed record LinkReviewRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "link.review_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid LinkId { get; init; }

        public required Guid OwnerId { get; init; }

        public required string Code { get; init; }

        public required int ReportCount { get; init; }
    }

    /// <summary>Admin xem xong va BO QUA cac bao cao cua link (link tiep tuc phuc vu).</summary>
    public sealed record LinkReviewDismissed : IEventPayload
    {
        public static string EventType
        {
            get { return "link.review_dismissed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid LinkId { get; init; }

        public required Guid OwnerId { get; init; }
    }

    /// <summary>
    /// Gioi thieu co dau hieu lam dung (cung IP voi nguoi gioi thieu, nhieu tai khoan mot IP, dot bien) — CHUA
    /// tinh hoa hong; admin xet (hang doi duyet chung). Chap nhan → referral.registered; bac → referral.rejected.
    /// </summary>
    public sealed record ReferralFlagged : IEventPayload
    {
        public static string EventType
        {
            get { return "referral.flagged"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ReferrerId { get; init; }

        public required Guid ReferredId { get; init; }

        public required string Reasons { get; init; }
    }

    /// <summary>Admin bac gioi thieu nghi ngo: khong bao gio tinh hoa hong.</summary>
    public sealed record ReferralRejected : IEventPayload
    {
        public static string EventType
        {
            get { return "referral.rejected"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ReferrerId { get; init; }

        public required Guid ReferredId { get; init; }

        public required string Reason { get; init; }
    }

    /// <summary>Nguoi dung xac nhan duoc gioi thieu boi mot sharer (FS-08).</summary>
    public sealed record ReferralRegistered : IEventPayload
    {
        public static string EventType
        {
            get { return "referral.registered"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ReferrerId { get; init; }

        public required Guid ReferredId { get; init; }

        public required DateTimeOffset RegisteredAt { get; init; }
    }
}
