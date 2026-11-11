using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Admin
{
    /// <summary>
    /// Mot event KHONG phai loi he thong nhung de lai hau qua nghiep vu can nguoi xu ly tay (kien truc 3.29 —
    /// loai 2): vd thuong bai dat khong chi duoc vi ky quy da hoan. Service nao cung phat duoc (EventConsumer
    /// dung chung phat thay handler khi handler nem CanXuLyTayException). admin-svc dua vao hang doi duyet
    /// (kind "manual"), admin xu ly xong thi bam "Da xu ly" → manual_action.resolved.
    ///
    /// Title / Description viet bang loi cho admin (khong ten service, khong ma event).
    /// </summary>
    public sealed record ManualActionRequired : IEventPayload
    {
        public static string EventType
        {
            get { return "manual_action.required"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ActionId { get; init; }

        /// <summary>Ma loai viec on dinh (vd "thuong_khong_chi_duoc") — de loc / thong ke.</summary>
        public required string Kind { get; init; }

        public required string Title { get; init; }

        public required string Description { get; init; }

        public Guid? ProjectId { get; init; }

        /// <summary>Nguoi lien quan (nguoi lam, doanh nghiep...).</summary>
        public Guid? UserId { get; init; }

        public long? AmountVnd { get; init; }

        /// <summary>Event goc khong xu ly duoc — de dev tra lai trong nhat ky / log.</summary>
        public required string SourceEventType { get; init; }

        public required Guid SourceEventId { get; init; }
    }

    /// <summary>Admin danh dau da xu ly xong mot viec can xu ly tay (actor = admin).</summary>
    public sealed record ManualActionResolved : IEventPayload
    {
        public static string EventType
        {
            get { return "manual_action.resolved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ActionId { get; init; }

        public string? Note { get; init; }
    }
}
