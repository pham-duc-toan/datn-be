using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.Identity.Api.Services
{
    /// <summary>Phat access token va refresh token.</summary>
    public sealed class TokenIssuer
    {
        private readonly SigningKeyProvider _khoa;
        private readonly JwtIssuerOptions _options;
        private readonly TimeProvider _clock;
        private readonly JsonWebTokenHandler _handler;

        public TokenIssuer(
            SigningKeyProvider khoa,
            IOptions<JwtIssuerOptions> options,
            TimeProvider clock)
        {
            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _khoa = khoa;
            _options = options.Value;
            _clock = clock;
            _handler = new JsonWebTokenHandler();
        }

        public TimeSpan AccessTokenLifetime
        {
            get { return _options.AccessTokenLifetime; }
        }

        public TimeSpan RefreshTokenLifetime
        {
            get { return _options.RefreshTokenLifetime; }
        }

        /// <summary>
        /// Tao access token. No TU CHUA moi thu service khac can biet — ai,
        /// vai tro gi, het han luc nao — nen service kiem no KHONG phai goi lai
        /// identity-svc. Do la dieu lam auth chiu duoc tai: neu moi request
        /// deu phai hoi identity-svc thi no thanh nut that cua ca 17 service.
        /// </summary>
        public string TaoAccessToken(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(CrowdClaims.Subject, user.Id.ToString()));
            claims.Add(new Claim(CrowdClaims.Email, user.Email));
            claims.Add(new Claim(CrowdClaims.Name, user.DisplayName));
            // Mot tai khoan co the co nhieu vai tro (labeler + sharer, muc 1.2).
            // Moi vai tro mot claim; thu vien tu gop thanh mang "role": [..].
            foreach (string vaiTro in user.Roles)
            {
                claims.Add(new Claim(CrowdClaims.Role, vaiTro));
            }

            // Ma dinh danh cua CHINH token nay — nen de sau nay lam danh sach
            // thu hoi (VD-S-11) ma khong phai thu hoi ca tai khoan.
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()));

            SecurityTokenDescriptor moTa = new SecurityTokenDescriptor();
            moTa.Issuer = _options.Issuer;
            moTa.Audience = _options.Audience;
            moTa.IssuedAt = bayGio.UtcDateTime;
            moTa.NotBefore = bayGio.UtcDateTime;
            moTa.Expires = (bayGio + _options.AccessTokenLifetime).UtcDateTime;
            moTa.Subject = new ClaimsIdentity(claims);
            moTa.SigningCredentials = new SigningCredentials(_khoa.KhoaKy, SecurityAlgorithms.RsaSha256);

            return _handler.CreateToken(moTa);
        }

        /// <summary>
        /// 32 byte ngau nhien tu bo sinh an toan mat ma — 256 bit, doan mo la
        /// bat kha thi. Tra cho client MOT LAN DUY NHAT; database chi giu hash.
        /// </summary>
        public static string TaoRefreshTokenGoc()
        {
            byte[] ngauNhien = RandomNumberGenerator.GetBytes(32);
            return Base64UrlEncoder.Encode(ngauNhien);
        }

        /// <summary>SHA-256 dang hex. Tat dinh: cung token goc luon ra cung hash.</summary>
        public static string BamRefreshToken(string tokenGoc)
        {
            if (tokenGoc == null)
            {
                throw new ArgumentNullException(nameof(tokenGoc));
            }

            byte[] bam = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenGoc));
            return Convert.ToHexStringLower(bam);
        }
    }
}
