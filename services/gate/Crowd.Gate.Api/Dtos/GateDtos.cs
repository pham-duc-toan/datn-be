using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Crowd.Gate.Api.Dtos
{
    /// <summary>GET /g/{code} — thong tin de ve trang vuot link (khong lo link dich).</summary>
    public sealed class GatePageResponse
    {
        public required string Code { get; init; }

        public required bool RequiresPassword { get; init; }

        /// <summary>Dem nguoc toi thieu (giay) truoc khi nop bai.</summary>
        public required int CountdownSeconds { get; init; }

        public required bool TurnstileEnabled { get; init; }

        public required string TurnstileSiteKey { get; init; }
    }

    public sealed class CreateSessionRequest
    {
        /// <summary>Token widget Cloudflare Turnstile tra ve tren trinh duyet.</summary>
        public string? TurnstileToken { get; init; }

        public string? Password { get; init; }
    }

    public sealed class GateQuestion
    {
        public required Guid SampleId { get; init; }

        public required string Modality { get; init; }

        /// <summary>Anh: link xem co han (MinIO). Van ban / cap: null.</summary>
        public string? FileUrl { get; init; }

        /// <summary>Van ban {"text"} / cap {"a","b","prompt"?}. Anh: null.</summary>
        public JsonElement? Content { get; init; }

        public required JsonElement Metadata { get; init; }
    }

    /// <summary>
    /// Bo cau hoi. Cau vang va cau that TRON LAN, khong co co nao cho biet cau nao la vang.
    /// Questions rong = khong co du an phuc vu: chi dem nguoc roi nop rong.
    /// </summary>
    public sealed class GateSessionResponse
    {
        public required Guid SessionId { get; init; }

        public required DateTimeOffset AnswerableAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>Tap nhan cua du an (de ve cong cu tra loi). null khi khong co cau hoi.</summary>
        public JsonElement? LabelSchema { get; init; }

        public required IReadOnlyList<GateQuestion> Questions { get; init; }
    }

    public sealed class SubmitRequest
    {
        /// <summary>sampleId → nhan (cung dinh dang payload labeler nop: {"&lt;tenCongCu&gt;": ...}).</summary>
        public Dictionary<Guid, JsonElement>? Answers { get; init; }
    }

    public sealed class SubmitResponse
    {
        public required bool Passed { get; init; }

        /// <summary>Dat: "/go/{code}?t=..." — mo trong trinh duyet de toi link dich.</summary>
        public string? RedirectUrl { get; init; }

        public DateTimeOffset? RedirectExpiresAt { get; init; }

        /// <summary>Truot: bo cau hoi MOI de lam lai.</summary>
        public GateSessionResponse? NewSession { get; init; }
    }

    public sealed class DailyStatResponse
    {
        public required DateOnly Date { get; init; }

        public required long Views { get; init; }

        public required long Submits { get; init; }

        public required long PaidClicks { get; init; }

        /// <summary>Doanh thu UOC TINH (vi ledger la so chinh thuc).</summary>
        public required long EstimatedRevenueVnd { get; init; }
    }

    public sealed class GroupStatResponse
    {
        public required string Key { get; init; }

        public required long Views { get; init; }

        public required long PaidClicks { get; init; }

        public required long EstimatedRevenueVnd { get; init; }
    }
}
