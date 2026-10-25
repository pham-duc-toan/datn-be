using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Dtos
{
    // Hinh dang du lieu HTTP. Ten truong tieng Anh cho khop voi envelope va
    // payload — day la hop dong ra ngoai, thong nhat mot ngon ngu.
    //
    // Moi truong request deu nullable vi client gui gi len cung duoc — viec
    // kiem tra nam o endpoint, khong tin vao kieu du lieu.

    public sealed class RegisterRequest
    {
        public string? Email { get; init; }

        /// <summary>Ten dang nhap khong trung: 3–30 ky tu a-z 0-9 . _ (User.LoiUsername).</summary>
        public string? Username { get; init; }

        public string? Password { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>Mot hoac nhieu trong: business, labeler.</summary>
        public IReadOnlyList<string>? Roles { get; init; }
    }

    public sealed class LoginRequest
    {
        /// <summary>Email HOAC username (co "@" o giua = email).</summary>
        public string? Email { get; init; }

        public string? Password { get; init; }
    }

    public sealed class RefreshRequest
    {
        public string? RefreshToken { get; init; }
    }

    public sealed class TokenResponse
    {
        public required string AccessToken { get; init; }

        public required string RefreshToken { get; init; }

        public required string TokenType { get; init; }

        /// <summary>Access token con song bao nhieu giay.</summary>
        public required int ExpiresIn { get; init; }
    }

    /// <summary>
    /// Dang nhap dung mat khau cua tai khoan bat 2FA (FC-04): CHUA co token, client hoi ma 6 so roi
    /// goi POST /auth/2fa/verify kem challengeToken.
    /// </summary>
    public sealed class TwoFactorChallengeResponse
    {
        public required bool TwoFactorRequired { get; init; }

        public required string ChallengeToken { get; init; }

        /// <summary>Ma thu thach con song bao nhieu giay.</summary>
        public required int ExpiresIn { get; init; }
    }

    public sealed class TwoFactorVerifyRequest
    {
        public string? ChallengeToken { get; init; }

        /// <summary>Ma 6 so tu ung dung xac thuc. Gui MOT trong hai: code hoac recoveryCode.</summary>
        public string? Code { get; init; }

        /// <summary>Ma du phong (dung mot lan) khi mat dien thoai.</summary>
        public string? RecoveryCode { get; init; }
    }

    public sealed class RegisterResponse
    {
        public required Guid UserId { get; init; }
    }

    public sealed class MeResponse
    {
        public required Guid UserId { get; init; }

        public string? Email { get; init; }

        public required IReadOnlyList<string> Roles { get; init; }

        public string? Name { get; init; }
    }
}
