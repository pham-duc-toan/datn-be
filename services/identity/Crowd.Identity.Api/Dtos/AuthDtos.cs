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

        public string? Password { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>Mot hoac nhieu trong: business, labeler, sharer.</summary>
        public IReadOnlyList<string>? Roles { get; init; }
    }

    public sealed class LoginRequest
    {
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

    /// <summary>Loi nghiep vu don gian, vd 409 email da ton tai.</summary>
    public sealed class ErrorResponse
    {
        public required string Error { get; init; }
    }
}
