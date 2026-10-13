using System;
using Crowd.BuildingBlocks.Auth.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Crowd.Gateway.Api.Sessions
{
    /// <summary>
    /// Doc nhanh sub / iat cua access token MA KHONG kiem chu ky — chi de TU CHOI them (token da
    /// thu hoi) hoac chia ngan gioi han tan suat theo nguoi dung. Khong bao gio dung de CHO PHEP:
    /// viec do van la cua Ocelot + service (kiem chu ky RS256 day du).
    /// Token gia mao ghi sub cua nguoi khac chi lam ton han muc cua chinh request do, roi bi 401.
    /// </summary>
    public sealed class TokenChuaKiem
    {
        private static readonly JsonWebTokenHandler Handler = new JsonWebTokenHandler();

        private TokenChuaKiem(Guid userId, DateTimeOffset issuedAt)
        {
            UserId = userId;
            IssuedAt = issuedAt;
        }

        public Guid UserId { get; }

        public DateTimeOffset IssuedAt { get; }

        /// <summary>null khi khong co header Bearer hoac token khong doc duoc.</summary>
        public static TokenChuaKiem? Doc(HttpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string? header = request.Headers.Authorization;
            if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string token = header.Substring("Bearer ".Length).Trim();
            if (token.Length == 0 || !Handler.CanReadToken(token))
            {
                return null;
            }

            try
            {
                JsonWebToken jwt = Handler.ReadJsonWebToken(token);
                string? sub;
                Guid userId;
                if (!jwt.TryGetPayloadValue(CrowdClaims.Subject, out sub) || sub == null || !Guid.TryParse(sub, out userId))
                {
                    return null;
                }

                return new TokenChuaKiem(userId, new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
