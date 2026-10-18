using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Admin.Api.Dtos
{
    public sealed class AuditEventResponse
    {
        public required Guid EventId { get; init; }

        public required string EventType { get; init; }

        public required string Producer { get; init; }

        public required DateTimeOffset OccurredAt { get; init; }

        public required Guid CorrelationId { get; init; }

        public Guid? CausationId { get; init; }

        /// <summary>null = he thong tu lam (consumer, worker).</summary>
        public Guid? ActorId { get; init; }

        public string? ActorRole { get; init; }

        public required JsonNode? Payload { get; init; }
    }

    public sealed class AuditPageResponse
    {
        public required IReadOnlyList<AuditEventResponse> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    /// <summary>Thong ke tong quan trong khoang ngay (FM-09) — tinh tu nhat ky audit.</summary>
    public sealed class StatsOverviewResponse
    {
        public required DateOnly From { get; init; }

        public required DateOnly To { get; init; }

        /// <summary>Nhat ky audit bat dau ghi tu luc nay — so lieu truoc moc nay khong co.</summary>
        public DateTimeOffset? AuditSince { get; init; }

        public required int NewUsers { get; init; }

        public required int BusinessesVerified { get; init; }

        public required int ProjectsPublished { get; init; }

        /// <summary>Bai lam da nop (moi bai = ca du an).</summary>
        public required int AttemptsSubmitted { get; init; }

        public required int ProjectsGraded { get; init; }

        public required int ProjectsCompleted { get; init; }

        public required long DepositsVnd { get; init; }

        /// <summary>Tien da tra cho nguoi lam: thuong bai dat + doanh thu chia se link + hoa hong.</summary>
        public required long LabelerPayoutsVnd { get; init; }

        public required long WithdrawalsVnd { get; init; }

        /// <summary>Khieu nai ca bai da mo.</summary>
        public required int AppealsOpened { get; init; }

        public required int AccountsBlocked { get; init; }

        public required int FraudCasesConfirmed { get; init; }

        public required IReadOnlyList<StatsDay> Daily { get; init; }
    }

    public sealed class StatsDay
    {
        public required DateOnly Date { get; init; }

        public required int NewUsers { get; init; }

        public required int AttemptsSubmitted { get; init; }

        public required int ProjectsGraded { get; init; }

        public required long DepositsVnd { get; init; }
    }

    public sealed class CreateAnnouncementRequest
    {
        public string? Title { get; init; }

        public string? Body { get; init; }

        /// <summary>all | business | labeler | sharer</summary>
        public string? Audience { get; init; }

        public bool SendEmail { get; init; }
    }

    public sealed class AnnouncementResponse
    {
        public required Guid Id { get; init; }

        public required string Title { get; init; }

        public required string Body { get; init; }

        public required string Audience { get; init; }

        public required bool SendEmail { get; init; }

        public required Guid CreatedBy { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }
}
