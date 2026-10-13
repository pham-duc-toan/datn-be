using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Admin
{
    /// <summary>
    /// Admin gui thong bao toan he thong (FM-09). notification-svc tao thong bao cho moi nguoi dung
    /// thuoc Audience, gui kem email neu SendEmail.
    /// </summary>
    public sealed record SystemAnnouncement : IEventPayload
    {
        public static string EventType
        {
            get { return "system.announcement"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AnnouncementId { get; init; }

        public required string Title { get; init; }

        public required string Body { get; init; }

        /// <summary>all | business | labeler | sharer</summary>
        public required string Audience { get; init; }

        public required bool SendEmail { get; init; }
    }
}
