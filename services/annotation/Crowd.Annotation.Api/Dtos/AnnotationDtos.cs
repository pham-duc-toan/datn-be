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

        /// <summary>Bai lam chua nhan nay (cong link: id phien vuot link).</summary>
        public required Guid AttemptId { get; init; }

        /// <summary>Luc chot vao / loai khoi ket qua (khi du an cham xong).</summary>
        public DateTimeOffset? DecidedAt { get; init; }
    }

    /// <summary>Ket qua duyet hang loat cac nhan khop dong thuan.</summary>
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
    /// <summary>Nhan cua toi (FL-08) — chi de doi chieu; tien / ket qua di theo bai lam (task-svc).</summary>
    public sealed class MyAnnotationsResponse
    {
        public required PagedResponse<AnnotationResponse> Annotations { get; init; }

        /// <summary>Nhan trong bai da nop, cho cham.</summary>
        public required int SubmittedCount { get; init; }

        /// <summary>Nhan vao ket qua (bai dat).</summary>
        public required int AcceptedCount { get; init; }

        /// <summary>Nhan bi loai (bai truot).</summary>
        public required int ExcludedCount { get; init; }
    }

    public sealed class SampleResultResponse
    {
        public required Guid SampleId { get; init; }

        public string? StorageKey { get; init; }

        public RawJson? SampleContent { get; init; }

        public required RawJson SampleMetadata { get; init; }

        public required int ApprovedCount { get; init; }

        /// <summary>Co cong cu gop ma khong ra ket qua duoc qua nua so nguoi ung ho (FB-22).</summary>
        public required bool Disputed { get; init; }

        /// <summary>
        /// Ket qua dong thuan cua quality-svc tren MOI nhan da nop (ca nhan chua duyet):
        /// "agreed" | "disputed" | "notApplicable". null = chua co.
        /// </summary>
        public string? ConsensusStatus { get; init; }

        /// <summary>Ket qua chot cua quality-svc theo tung cong cu gop duoc.</summary>
        public RawJson? ConsensusFinal { get; init; }

        /// <summary>
        /// Ket qua gop theo TUNG CONG CU tu cac nhan DA DUYET: {"ten":{"kind","method","final","agrees","disputed",...}}.
        /// method: majority | cluster | medoid | median | borda | none. final = null khi tranh chap.
        /// "none" = cong cu khong gop tu dong (freeText) hoac chua ai lam, xem Labels.
        /// </summary>
        public required RawJson Tools { get; init; }

        /// <summary>Phan du lieu cua cac nhan DA DUYET (moi nguoi mot phan tu), cu nhat truoc.</summary>
        public required IReadOnlyList<RawJson> Labels { get; init; }
    }

    public sealed class ProjectResultsResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>So mau dung lai ket qua da tinh (bo nho dem) — chi de do, khong ghi vao file xuat.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int CacheHits { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public int CacheMisses { get; init; }

        public required string Modality { get; init; }

        public required RawJson LabelSchema { get; init; }

        public required int SampleCount { get; init; }

        public required int DisputedCount { get; init; }

        /// <summary>Phan bo nhan theo tung cong cu: {"ten cong cu":{"lop":so}} — nen cua canh bao lech lop (FB-24).</summary>
        public required IReadOnlyDictionary<string, SortedDictionary<string, int>> LabelDistribution { get; init; }

        /// <summary>Canh bao lech phan bo (FB-24); rong = binh thuong.</summary>
        public required IReadOnlyList<ImbalanceWarningDto> ImbalanceWarnings { get; init; }

        public required IReadOnlyList<SampleResultResponse> Samples { get; init; }
    }

    /// <summary>Mot canh bao lech phan bo nhan (FB-24).</summary>
    public sealed class ImbalanceWarningDto
    {
        /// <summary>lop_ap_dao (mot lop chiem qua nguong) | lop_chua_co_nhan (lop khai ma chua ai chon)</summary>
        public required string Code { get; init; }

        public required string Tool { get; init; }

        public required string Label { get; init; }

        public required int Count { get; init; }

        public required int Total { get; init; }

        public required int Percent { get; init; }
    }

    /// <summary>GET /annotations/projects/{id}/distribution — phan bo + canh bao, khong kem tung mau.</summary>
    public sealed class DistributionResponse
    {
        public required Guid ProjectId { get; init; }

        public required int SampleCount { get; init; }

        public required IReadOnlyDictionary<string, SortedDictionary<string, int>> LabelDistribution { get; init; }

        public required IReadOnlyList<ImbalanceWarningDto> ImbalanceWarnings { get; init; }

        /// <summary>Nguong dang ap dung (setting annotation.imbalance_threshold_percent).</summary>
        public required int ThresholdPercent { get; init; }
    }
}
