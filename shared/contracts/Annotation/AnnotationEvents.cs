using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Annotation
{
    /// <summary>
    /// Mot nhan vua duoc LUU vao annotation-svc. quality-svc tinh dong thuan,
    /// fraud-svc soi gian lan (CATALOG).
    /// </summary>
    public sealed record AnnotationSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "annotation.submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AnnotationId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>null khi nhan den tu cong link (khach vang lai).</summary>
        public Guid? LabelerId { get; init; }

        public required IReadOnlyList<string> Labels { get; init; }

        public required AnnotationSource Source { get; init; }
    }

    /// <summary>
    /// Nhan bi tu choi (FB-21). notification bao labeler; identity tru uy tin.
    /// KHONG chi tien — ledger khong nghe event nay.
    /// </summary>
    public sealed record AnnotationRejected : IEventPayload
    {
        public static string EventType
        {
            get { return "annotation.rejected"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AnnotationId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public Guid? LabelerId { get; init; }

        public required string Reason { get; init; }

        /// <summary>true = tu choi CUOI CUNG (khieu nai da bi bac), khong con khieu nai duoc.</summary>
        public required bool IsFinal { get; init; }
    }

    /// <summary>Labeler khieu nai nhan bi tu choi (FL-09). admin-svc dua vao hang doi.</summary>
    public sealed record AppealOpened : IEventPayload
    {
        public static string EventType
        {
            get { return "appeal.opened"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AnnotationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required string Message { get; init; }
    }
}
