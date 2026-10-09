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

        /// <summary>null voi nhan tu cong link (khong thuoc task nao).</summary>
        public Guid? TaskId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>professional (labeler) | linkGateway (khach vang lai qua trang vuot link).</summary>
        public required LabelSource Source { get; init; }

        /// <summary>Link xem / nghe file cua mau, co han vai phut (S-07). null voi text / pair.</summary>
        public string? FileUrl { get; init; }

        /// <summary>Noi dung text / pair cua mau. null voi du lieu file.</summary>
        public RawJson? SampleContent { get; init; }

        /// <summary>Metadata mau: kich thuoc, thoi luong, doan [segmentStart, segmentEnd].</summary>
        public required RawJson SampleMetadata { get; init; }

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

        /// <summary>
        /// Goi y tu quality-svc: nhan co khop ket qua dong thuan khong. null = chua co
        /// ket qua (task chua du nguoi) hoac tap nhan khong co cong cu gop duoc.
        /// </summary>
        public bool? ConsensusAgrees { get; init; }
    }

    /// <summary>Ket qua duyet hang loat cac nhan khop dong thuan.</summary>
    public sealed class BulkApproveResponse
    {
        public required int ApprovedCount { get; init; }

        /// <summary>Nhan cua chinh nguoi duyet — khong tu duyet duoc, bo qua.</summary>
        public required int SkippedOwnCount { get; init; }
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

        public string? StorageKey { get; init; }

        public RawJson? SampleContent { get; init; }

        public required RawJson SampleMetadata { get; init; }

        public required int ApprovedCount { get; init; }

        /// <summary>Co cong cu gop theo da so ma khong lua chon nao qua ban (FB-22).</summary>
        public required bool Disputed { get; init; }

        /// <summary>
        /// Ket qua dong thuan cua quality-svc tren MOI nhan da nop (ca nhan chua duyet):
        /// "agreed" | "disputed" | "notApplicable". null = chua co.
        /// </summary>
        public string? ConsensusStatus { get; init; }

        /// <summary>Ket qua chot cua quality-svc theo tung cong cu gop duoc.</summary>
        public RawJson? ConsensusFinal { get; init; }

        /// <summary>
        /// Ket qua gop theo TUNG CONG CU: {"ten":{"kind","method","final","votes","disputed"}}.
        /// method "none" = chua gop tu dong, xem Labels.
        /// </summary>
        public required RawJson Tools { get; init; }

        /// <summary>Phan du lieu cua cac nhan DA DUYET (moi nguoi mot phan tu), cu nhat truoc.</summary>
        public required IReadOnlyList<RawJson> Labels { get; init; }
    }

    public sealed class ProjectResultsResponse
    {
        public required Guid ProjectId { get; init; }

        public required string Modality { get; init; }

        public required RawJson LabelSchema { get; init; }

        public required int SampleCount { get; init; }

        public required int DisputedCount { get; init; }

        /// <summary>Phan bo nhan theo tung cong cu: {"ten cong cu":{"lop":so}} — nen cua canh bao lech lop (FB-24).</summary>
        public required IReadOnlyDictionary<string, SortedDictionary<string, int>> LabelDistribution { get; init; }

        public required IReadOnlyList<SampleResultResponse> Samples { get; init; }
    }
}
