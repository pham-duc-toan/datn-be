using System;
using System.Collections.Generic;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Labeling;

namespace Crowd.Annotation.Api.Dtos
{
    public sealed class AnnotationResponse
    {
        public required Guid Id { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Link xem anh co han vai phut (S-07).</summary>
        public required string ImageUrl { get; init; }

        public Guid? LabelerId { get; init; }

        /// <summary>Noi dung nhan: {"taskType":..., "schemaVersion":..., "data":{...}}.</summary>
        public required LabelPayload Payload { get; init; }

        public required AnnotationStatus Status { get; init; }

        public required DateTimeOffset SubmittedAt { get; init; }

        public DateTimeOffset? ReviewedAt { get; init; }

        public string? RejectReason { get; init; }

        public string? AppealMessage { get; init; }

        /// <summary>true = labeler con khieu nai duoc (bi tu choi, chua khieu nai, con han).</summary>
        public required bool CanAppeal { get; init; }
    }

    public sealed class HistoryEntryResponse
    {
        public required string Action { get; init; }

        public Guid? ActorId { get; init; }

        public string? Note { get; init; }

        public required DateTimeOffset At { get; init; }
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    /// <summary>Lich su cong viec cua labeler (FL-08).</summary>
    public sealed class MyAnnotationsResponse
    {
        public required PagedResponse<AnnotationResponse> Annotations { get; init; }

        public required int PendingCount { get; init; }

        public required int ApprovedCount { get; init; }

        public required int RejectedCount { get; init; }

        public required int AppealedCount { get; init; }

        /// <summary>Duyet / (duyet + tu choi), phan tram. null khi chua co nhan nao duoc xet.</summary>
        public int? ApprovalRatePercent { get; init; }
    }

    public sealed class ReasonRequest
    {
        public string? Reason { get; init; }
    }

    public sealed class AppealRequest
    {
        public string? Message { get; init; }
    }

    public sealed class ResolveAppealRequest
    {
        public bool Accept { get; init; }

        public string? Note { get; init; }
    }

    /// <summary>Ket qua chot cua mot mau (FB-22 / FB-25).</summary>
    public sealed class SampleResultResponse
    {
        public required Guid SampleId { get; init; }

        public required IReadOnlyList<string> FinalLabels { get; init; }

        public required IReadOnlyDictionary<string, int> Votes { get; init; }

        public required int ApprovedCount { get; init; }

        public required bool Disputed { get; init; }
    }

    public sealed class ProjectResultsResponse
    {
        public required Guid ProjectId { get; init; }

        public required int SampleCount { get; init; }

        public required int DisputedCount { get; init; }

        /// <summary>Phan bo nhan chot — nen cua canh bao lech lop (FB-24).</summary>
        public required IReadOnlyDictionary<string, int> LabelDistribution { get; init; }

        public required IReadOnlyList<SampleResultResponse> Samples { get; init; }
    }
}
