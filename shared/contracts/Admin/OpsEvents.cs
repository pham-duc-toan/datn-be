using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Admin
{
    /// <summary>
    /// Canh bao van hanh: mot hang doi DLQ co message (VD-D-06, VD-D-12 — hop dong payload nghiem ngat thi loi
    /// hop dong rot vao DLQ, moi health check van xanh). admin-svc phat khi DLQ vuot nguong (va nhac lai theo
    /// ops.alert_repeat); notification-svc gui email toi ops.alert_email.
    /// </summary>
    public sealed record OpsDlqAlert : IEventPayload
    {
        public static string EventType
        {
            get { return "ops.dlq_alert"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Ten hang doi DLQ (vd ledger-svc.annotation-approved.dlq).</summary>
        public required string Queue { get; init; }

        public required long Depth { get; init; }

        public required int Threshold { get; init; }
    }
}
