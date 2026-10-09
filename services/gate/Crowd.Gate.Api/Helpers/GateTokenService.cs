using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.Gate.Api.Helpers
{
    /// <summary>
    /// Token mo link dich (dac ta 2.4 buoc 3): JWT HS256 {jti, lnk, aud=gate, exp}.
    /// Ky va kiem CHI o gate-svc nen dung khoa doi xung. Dung mot lan nho jti trong Redis.
    /// </summary>
    public sealed class GateTokenService
    {
        private const string Audience = "crowd-gate";
        private const string Issuer = "crowd-gate";

        private readonly SymmetricSecurityKey _khoa;
        private readonly JsonWebTokenHandler _handler = new JsonWebTokenHandler();

        public GateTokenService(IOptions<GateOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            byte[] khoa = Encoding.UTF8.GetBytes(options.Value.TokenSigningKey ?? string.Empty);
            if (khoa.Length < 32)
            {
                throw new InvalidOperationException("Gate:TokenSigningKey phai dai it nhat 32 byte.");
            }

            _khoa = new SymmetricSecurityKey(khoa);
        }

        public string Tao(string jti, Guid linkId, DateTimeOffset hetHan)
        {
            SecurityTokenDescriptor moTa = new SecurityTokenDescriptor();
            moTa.Issuer = Issuer;
            moTa.Audience = Audience;
            moTa.Expires = hetHan.UtcDateTime;
            moTa.Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = jti,
                ["lnk"] = linkId.ToString("N"),
            };
            moTa.SigningCredentials = new SigningCredentials(_khoa, SecurityAlgorithms.HmacSha256);
            return _handler.CreateToken(moTa);
        }

        /// <summary>Kiem chu ky + han + aud + dung link. Tra ve jti, null neu khong hop le.</summary>
        public async Task<string?> KiemAsync(string? token, Guid linkId)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            {
                return null;
            }

            TokenValidationParameters p = new TokenValidationParameters();
            p.ValidIssuer = Issuer;
            p.ValidAudience = Audience;
            p.IssuerSigningKey = _khoa;
            p.ValidAlgorithms = new string[] { SecurityAlgorithms.HmacSha256 };
            p.ClockSkew = TimeSpan.FromSeconds(5);

            TokenValidationResult kq = await _handler.ValidateTokenAsync(token, p);
            if (!kq.IsValid)
            {
                return null;
            }

            object? lnk;
            object? jti;
            if (!kq.Claims.TryGetValue("lnk", out lnk) || !kq.Claims.TryGetValue(JwtRegisteredClaimNames.Jti, out jti))
            {
                return null;
            }

            if (!string.Equals(Convert.ToString(lnk, System.Globalization.CultureInfo.InvariantCulture), linkId.ToString("N"), StringComparison.Ordinal))
            {
                return null;
            }

            return Convert.ToString(jti, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
