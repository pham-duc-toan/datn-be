using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Dtos
{
    // ---------------- xac thuc email, mat khau (FC-01, FC-03) ----------------

    public sealed class TokenOnlyRequest
    {
        public string? Token { get; init; }
    }

    public sealed class ForgotPasswordRequest
    {
        public string? Email { get; init; }
    }

    public sealed class ResetPasswordRequest
    {
        public string? Token { get; init; }

        public string? NewPassword { get; init; }
    }

    public sealed class ChangePasswordRequest
    {
        public string? CurrentPassword { get; init; }

        public string? NewPassword { get; init; }
    }

    public sealed class EmailVerifiedResponse
    {
        public required bool EmailVerified { get; init; }
    }

    // ---------------- ho so (FC-05, FL-01) ----------------

    public sealed class SkillsDto
    {
        public IReadOnlyList<string>? Domains { get; init; }

        public IReadOnlyList<string>? Languages { get; init; }

        /// <summary>image | text | audio | video | pair</summary>
        public IReadOnlyList<string>? Modalities { get; init; }
    }

    public sealed class ProfileResponse
    {
        public required Guid UserId { get; init; }

        public required string Email { get; init; }

        public required bool EmailVerified { get; init; }

        public required string DisplayName { get; init; }

        public string? Bio { get; init; }

        /// <summary>Link xem anh dai dien, song theo storage.view_link_ttl; null = chua co anh.</summary>
        public string? AvatarUrl { get; init; }

        public required IReadOnlyList<string> Roles { get; init; }

        public required SkillsDto Skills { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class UpdateProfileRequest
    {
        public string? DisplayName { get; init; }

        public string? Bio { get; init; }
    }

    public sealed class AvatarUploadRequest
    {
        public string? FileName { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed class AvatarUploadResponse
    {
        public required string Key { get; init; }

        public required string UploadUrl { get; init; }

        public required string Method { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    public sealed class SetAvatarRequest
    {
        /// <summary>Key vua tai len; null = go anh dai dien.</summary>
        public string? Key { get; init; }
    }

    // ---------------- cap do, huy hieu, xep hang (FL-12) ----------------

    public sealed class BadgeDto
    {
        public required string Code { get; init; }

        public required string Name { get; init; }

        public required DateTimeOffset AwardedAt { get; init; }
    }

    public sealed class LevelInfoResponse
    {
        public required int Level { get; init; }

        public required int ApprovedCount { get; init; }

        public int? Reputation { get; init; }

        /// <summary>Dieu kien cap ke tiep; null = da cap cao nhat.</summary>
        public NextLevelDto? NextLevel { get; init; }

        public required IReadOnlyList<BadgeDto> Badges { get; init; }
    }

    public sealed class NextLevelDto
    {
        public required int Level { get; init; }

        public required int MinApproved { get; init; }

        public required int MinReputation { get; init; }
    }

    public sealed class PublicProfileResponse
    {
        public required Guid UserId { get; init; }

        public required string DisplayName { get; init; }

        public string? AvatarUrl { get; init; }

        public required int Level { get; init; }

        public required IReadOnlyList<BadgeDto> Badges { get; init; }
    }

    public sealed class LeaderboardEntry
    {
        public required int Rank { get; init; }

        public required Guid UserId { get; init; }

        public required string DisplayName { get; init; }

        public string? AvatarUrl { get; init; }

        public required int Level { get; init; }

        public required int ApprovedCount { get; init; }
    }

    // ---------------- noi bo: gateway xin moc thu hoi phien ----------------

    public sealed class SessionRevocationDto
    {
        public required Guid UserId { get; init; }

        public required DateTimeOffset RevokedAt { get; init; }
    }

    // ---------------- noi bo: project-svc tra cuu nguoi duoc moi / xin tham gia ----------------

    public sealed class UserLookupRequest
    {
        public IReadOnlyList<Guid>? UserIds { get; init; }

        /// <summary>Email bat ky hoa / thuong, co khoang trang — identity tu chuan hoa.</summary>
        public IReadOnlyList<string>? Emails { get; init; }
    }

    /// <summary>
    /// Thong tin du de project-svc quyet "nguoi nay vao du an duoc khong": vai tro, khoa,
    /// cap do, uy tin (cung nguon task-svc dung khi cap task).
    /// </summary>
    public sealed class UserLookupDto
    {
        public required Guid UserId { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        public required IReadOnlyList<string> Roles { get; init; }

        public required bool Locked { get; init; }

        /// <summary>Chua co thong ke (chua tung co nhan duoc duyet) = cap 1, nhu GET /me/level.</summary>
        public required int Level { get; init; }

        /// <summary>null = quality-svc chua cham.</summary>
        public int? Reputation { get; init; }
    }
}
