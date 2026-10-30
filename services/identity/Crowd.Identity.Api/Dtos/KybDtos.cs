using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Dtos
{
    /// <summary>PUT /kyb/me — thong tin doanh nghiep.</summary>
    public sealed class UpdateKybRequest
    {
        public string? CompanyName { get; init; }

        public string? TaxCode { get; init; }

        public string? Address { get; init; }
    }

    /// <summary>POST /kyb/me/license — xin link tai giay phep (PUT thang len kho file).</summary>
    public sealed class LicenseUploadRequest
    {
        public string? FileName { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed class LicenseUploadResponse
    {
        public required string UploadUrl { get; init; }

        public required string Method { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    public sealed class KybResponse
    {
        public required Guid BusinessId { get; init; }

        /// <summary>draft | submitted | verified | rejected</summary>
        public required string Status { get; init; }

        public required string CompanyName { get; init; }

        public required string TaxCode { get; init; }

        public required string Address { get; init; }

        public required bool HasLicense { get; init; }

        public string? LicenseFileName { get; init; }

        /// <summary>Link xem giay phep (song ngan) — chi tra cho CHINH CHU o GET /kyb/me (admin dung KybAdminResponse.LicenseUrl).</summary>
        public string? LicenseUrl { get; init; }

        public string? RejectReason { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public DateTimeOffset? ReviewedAt { get; init; }
    }

    /// <summary>Hang doi duyet KYB cua admin; chi tiet them email + link xem giay phep (song ngan).</summary>
    public sealed class KybAdminResponse
    {
        public required KybResponse Profile { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        public required string AccountStatus { get; init; }

        /// <summary>Chi co o API chi tiet; song theo storage.view_link_ttl (S-07).</summary>
        public string? LicenseUrl { get; init; }
    }

    public sealed class KybRejectRequest
    {
        public string? Reason { get; init; }
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    // ---------------- quan tri tai khoan (FM-01) ----------------

    public sealed class AdminUserResponse
    {
        public required Guid UserId { get; init; }

        public required string Email { get; init; }

        public required string Username { get; init; }

        public required string DisplayName { get; init; }

        public required IReadOnlyList<string> Roles { get; init; }

        /// <summary>active | blocked</summary>
        public required string Status { get; init; }

        public DateTimeOffset? BlockedAt { get; init; }

        public string? BlockedReason { get; init; }

        /// <summary>Chi voi tai khoan business: trang thai KYB (null neu chua co ho so).</summary>
        public string? KybStatus { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class BlockUserRequest
    {
        public string? Reason { get; init; }
    }

    /// <summary>GET /internal/businesses/{id}: ho so KYB cho ledger in hoa don.</summary>
    public sealed class BusinessInternalDto
    {
        public required Guid BusinessId { get; init; }

        /// <summary>draft | submitted | verified | rejected</summary>
        public required string Status { get; init; }

        public required string CompanyName { get; init; }

        public required string TaxCode { get; init; }

        public required string Address { get; init; }

        public required string Email { get; init; }
    }
}
