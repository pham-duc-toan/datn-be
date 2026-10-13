using System;
using System.Collections.Generic;

namespace Crowd.Ledger.Api.Dtos
{
    // ---------------- tranh chap (FM-06) ----------------

    public sealed class OpenDisputeRequest
    {
        public Guid AnnotationId { get; init; }

        public string? Reason { get; init; }
    }

    public sealed class ResolveDisputeRequest
    {
        /// <summary>refundBusiness | payLabeler</summary>
        public string? Outcome { get; init; }

        public string? Note { get; init; }
    }

    public sealed class DisputeResponse
    {
        public required Guid Id { get; init; }

        public required Guid AnnotationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        public required string Reason { get; init; }

        /// <summary>open | refundedBusiness | paidLabeler</summary>
        public required string State { get; init; }

        public required DateTimeOffset OpenedAt { get; init; }

        public DateTimeOffset? ResolvedAt { get; init; }

        public string? ResolutionNote { get; init; }
    }

    // ---------------- thu nhap (FL-11) ----------------

    public sealed class EarningsResponse
    {
        public required DateOnly From { get; init; }

        public required DateOnly To { get; init; }

        /// <summary>day | project</summary>
        public required string GroupBy { get; init; }

        public required long TotalVnd { get; init; }

        /// <summary>Theo nguon: annotation (nhan duoc duyet), gateClick (luot vuot link), referral (hoa hong).</summary>
        public required IReadOnlyDictionary<string, long> ByKindVnd { get; init; }

        public required IReadOnlyList<EarningsItem> Items { get; init; }
    }

    public sealed class EarningsItem
    {
        /// <summary>Ngay (yyyy-MM-dd, gio Viet Nam) hoac projectId — theo groupBy.</summary>
        public required string Key { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>So khoan thu (moi nhan / moi lo vuot link / moi hoa hong mot khoan).</summary>
        public required int Count { get; init; }
    }

    // ---------------- chi phi du an (FB-20) ----------------

    public sealed class ProjectCostResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>active | closing | closed</summary>
        public required string EscrowState { get; init; }

        /// <summary>Da ky quy luc publish.</summary>
        public required long ReservedVnd { get; init; }

        /// <summary>Con lai trong ky quy.</summary>
        public required long BalanceVnd { get; init; }

        /// <summary>Da chi cho nhan duoc duyet (thu lao labeler + phi nen tang).</summary>
        public required long SpentAnnotationsVnd { get; init; }

        /// <summary>Da chi cho cong vuot link (nguoi chia se + nen tang).</summary>
        public required long SpentGateVnd { get; init; }

        /// <summary>Tien tranh chap doanh nghiep thang, da ve lai ky quy / vi.</summary>
        public required long DisputeRefundsVnd { get; init; }

        /// <summary>Ky quy con du da tra ve vi doanh nghiep khi ket thuc.</summary>
        public required long ReturnedVnd { get; init; }

        public required int PaidAnnotations { get; init; }

        /// <summary>Thu lao labeler dang treo (chua rut duoc) — doanh nghiep con tranh chap duoc.</summary>
        public required long HeldVnd { get; init; }

        public required long DisputedVnd { get; init; }
    }
}
