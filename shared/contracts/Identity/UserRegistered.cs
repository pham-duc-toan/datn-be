using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Identity
{
    /// <summary>
    /// Mot tai khoan vua duoc tao. Nguoi nghe: notification-svc (gui email
    /// xac thuc, FC-01).
    /// Nguon: contracts/events/CATALOG.md, muc identity-svc.
    ///
    /// KHONG chua mat khau, ke ca ban da bam. Event di qua bus, nam trong queue,
    /// co the vao DLQ va log — khong noi nao trong so do duoc thay hash mat khau.
    /// </summary>
    public sealed record UserRegistered : IEventPayload
    {
        public static string EventType
        {
            get { return "user.registered"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        /// <summary>Da chuan hoa: cat khoang trang, chu thuong.</summary>
        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        /// <summary>Vai tro nguoi dung tu chon luc dang ky: business, labeler, sharer.</summary>
        public required IReadOnlyList<string> Roles { get; init; }
    }
}
