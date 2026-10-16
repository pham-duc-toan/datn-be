using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Admin.Api.Dtos
{
    // ---- Hang doi duyet chung ----

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    public sealed class ApprovalItemResponse
    {
        public required Guid Id { get; init; }

        /// <summary>kyb | identity | project | withdrawal | appeal | dispute | link</summary>
        public required string Kind { get; init; }

        /// <summary>Id o service chu — dung goi API duyet cua service do (xem tich-hop-frontend).</summary>
        public required Guid SubjectId { get; init; }

        public required string Status { get; init; }

        public string? Title { get; init; }

        public Guid? RequesterId { get; init; }

        public long? AmountVnd { get; init; }

        public string? Note { get; init; }

        public required DateTimeOffset OpenedAt { get; init; }

        public DateTimeOffset? DecidedAt { get; init; }

        public Guid? DecidedBy { get; init; }
    }

    public sealed class ApprovalKindSummary
    {
        public required string Kind { get; init; }

        public required int Pending { get; init; }

        public DateTimeOffset? OldestOpenedAt { get; init; }

        /// <summary>Don cu nhat da cho bao nhieu gio.</summary>
        public double? OldestWaitingHours { get; init; }
    }

    public sealed class ApprovalSummaryResponse
    {
        public required int TotalPending { get; init; }

        public required IReadOnlyList<ApprovalKindSummary> Kinds { get; init; }
    }

    // ---- Mau bai toan (FM-03) ----

    public sealed class TemplateRequest
    {
        /// <summary>Chi khi tao: ma ngan a-z 0-9 '-' (2–60 ky tu), duy nhat.</summary>
        public string? Code { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        /// <summary>Tap nhan (cung dinh dang PUT /projects/{id}/label-schema), co modality.</summary>
        public JsonElement LabelSchema { get; init; }

        public string? GuidelineMarkdown { get; init; }

        public long? SuggestedUnitPriceVnd { get; init; }

        public int? SuggestedRedundancy { get; init; }
    }

    public sealed class TemplateResponse
    {
        public required Guid Id { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string Modality { get; init; }

        public required JsonNode? LabelSchema { get; init; }

        public required string GuidelineMarkdown { get; init; }

        public long? SuggestedUnitPriceVnd { get; init; }

        public int? SuggestedRedundancy { get; init; }

        public required bool Active { get; init; }

        public required int Version { get; init; }

        public required DateTimeOffset UpdatedAt { get; init; }
    }

    // ---- Van hanh: DLQ ----

    public sealed class QueueDepthResponse
    {
        public required string Queue { get; init; }

        public required long Messages { get; init; }

        public required bool IsDeadLetter { get; init; }

        public required bool OverThreshold { get; init; }
    }

    public sealed class OpsQueuesResponse
    {
        /// <summary>false = khong doc duoc management API (RabbitMQ sap / sai cau hinh) — chinh no la mot canh bao.</summary>
        public required bool Reachable { get; init; }

        public DateTimeOffset? CheckedAt { get; init; }

        public required int Threshold { get; init; }

        public required IReadOnlyList<QueueDepthResponse> DeadLetterQueues { get; init; }
    }
}
