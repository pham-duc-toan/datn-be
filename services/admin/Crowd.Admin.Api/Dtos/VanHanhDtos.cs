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

        /// <summary>kyb | identity | project | withdrawal | appeal | link | referral | manual</summary>
        public required string Kind { get; init; }

        /// <summary>Id o service chu — dung goi API duyet cua service do (xem tich-hop-frontend). manual: id viec.</summary>
        public required Guid SubjectId { get; init; }

        public required string Status { get; init; }

        public string? Title { get; init; }

        public Guid? RequesterId { get; init; }

        public long? AmountVnd { get; init; }

        public string? Note { get; init; }

        /// <summary>manual: du an lien quan (co the null).</summary>
        public Guid? ProjectId { get; init; }

        /// <summary>manual: mo ta viec can xu ly (loi thuong, cho admin).</summary>
        public string? Detail { get; init; }

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

    /// <summary>POST /admin/approvals/manual/{actionId}/resolve</summary>
    public sealed class ResolveManualRequest
    {
        /// <summary>Da lam gi (vd "Da chuyen tay 50.000 d cho nguoi lam"). Bat buoc.</summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// GET /admin/system-health — the "Tinh trang he thong" tren trang tong quan admin (kien truc 3.29). Chi dem
    /// loi KY THUAT (message trong DLQ); ca nghiep vu can nguoi xu ly nam o hang doi duyet (kind manual).
    /// </summary>
    public sealed class SystemHealthResponse
    {
        /// <summary>ok | co_loi | khong_ro (chua doc duoc / RabbitMQ khong tra loi).</summary>
        public required string Status { get; init; }

        /// <summary>Tong so viec he thong khong tu xu ly duoc (tong message trong moi DLQ).</summary>
        public required long FailedCount { get; init; }

        /// <summary>So luong xu ly dang co loi (so DLQ co message).</summary>
        public required int AffectedAreas { get; init; }

        public DateTimeOffset? CheckedAt { get; init; }

        /// <summary>Doi ky thuat da duoc bao qua email chua (setting ops.alert_email co dat).</summary>
        public required bool TechTeamNotified { get; init; }
    }

    // ---- Mau bai toan (FM-03) ----

    public sealed class TemplateRequest
    {
        /// <summary>Chi khi tao: ma ngan a-z 0-9 '-' (2–60 ky tu), duy nhat.</summary>
        public string? Code { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        /// <summary>Tap nhan (cung dinh dang nhom labelSchema cua PATCH /projects/{id}), co modality.</summary>
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
}
