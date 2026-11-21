using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Reco
{
    /// <summary>
    /// reco-svc → notification-svc (kien truc 3.40): du an labeler da THICH sap het han chot (truoc
    /// reco.liked_reminder_hours) ma labeler chua nop bai. Moi (labeler, du an) chi phat MOT lan.
    /// </summary>
    public sealed record LikedProjectDeadlineSoon : IEventPayload
    {
        public static string EventType
        {
            get { return "liked_project.deadline_soon"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid UserId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        public required DateTimeOffset Deadline { get; init; }
    }
}
