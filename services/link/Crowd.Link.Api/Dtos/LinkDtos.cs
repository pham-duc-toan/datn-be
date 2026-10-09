using System;
using System.Collections.Generic;
using Crowd.Link.Api.Entities;

namespace Crowd.Link.Api.Dtos
{
    public sealed class CreateLinkRequest
    {
        public string? Url { get; init; }

        /// <summary>Ma tu chon (FS-06). Trong = sinh ngau nhien.</summary>
        public string? Alias { get; init; }

        /// <summary>Mat khau mo link (FS-06), 4-100 ky tu. Trong = khong dat.</summary>
        public string? Password { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public Guid? CampaignId { get; init; }
    }

    public sealed class BulkCreateLinksRequest
    {
        public IReadOnlyList<string>? Urls { get; init; }

        public Guid? CampaignId { get; init; }
    }

    public sealed class BulkLinkResult
    {
        public required string Url { get; init; }

        public LinkResponse? Link { get; init; }

        /// <summary>Ma loi neu dong nay khong tao duoc (vd ten_mien_bi_chan).</summary>
        public string? ErrorCode { get; init; }

        public string? Error { get; init; }
    }

    public sealed class UpdateLinkRequest
    {
        /// <summary>true = doi mat khau theo truong Password (Password trong = bo mat khau).</summary>
        public bool ChangePassword { get; init; }

        public string? Password { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public Guid? CampaignId { get; init; }
    }

    public sealed class LinkResponse
    {
        public required Guid Id { get; init; }

        public required string Code { get; init; }

        /// <summary>Link rut gon de chia se, vd http://localhost:8080/g/aB3xY9k.</summary>
        public required string ShortUrl { get; init; }

        public required string DestinationUrl { get; init; }

        public required string Domain { get; init; }

        public required LinkStatus Status { get; init; }

        public string? StatusReason { get; init; }

        public required bool HasPassword { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public Guid? CampaignId { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    public sealed class CreateCampaignRequest
    {
        public string? Name { get; init; }
    }

    public sealed class CampaignResponse
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required int LinkCount { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class ApiKeyResponse
    {
        /// <summary>Chi co khi VUA tao: key day du, hien mot lan duy nhat.</summary>
        public string? ApiKey { get; init; }

        public required string Prefix { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class ReportLinkRequest
    {
        public string? Reason { get; init; }
    }

    public sealed class DisableLinkRequest
    {
        public string? Reason { get; init; }

        /// <summary>Vi pham da xac nhan: giu lai doanh thu dang treo cua link (mac dinh true).</summary>
        public bool? WithholdRevenue { get; init; }
    }

    public sealed class ReviewLinkResponse
    {
        public required LinkResponse Link { get; init; }

        public required Guid OwnerId { get; init; }

        public required int ReportCount { get; init; }

        public required IReadOnlyList<string> RecentReasons { get; init; }
    }

    public sealed class BlockDomainRequest
    {
        public string? Domain { get; init; }

        public string? Reason { get; init; }
    }

    public sealed class BlockedDomainResponse
    {
        public required string Domain { get; init; }

        public required string Reason { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>So link dang chay bi vo hieu hoa ngay khi chan.</summary>
        public int DisabledLinkCount { get; init; }
    }

    public sealed class ReferralInfoResponse
    {
        public required string Code { get; init; }

        public required int ReferredCount { get; init; }

        public Guid? ReferredBy { get; init; }
    }

    public sealed class ClaimReferralRequest
    {
        public string? Code { get; init; }
    }
}
