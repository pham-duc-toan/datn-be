using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Settings;
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

        private readonly ISettings _settings;

        public TokenIssuer(
            SigningKeyProvider khoa,
            IOptions<JwtIssuerOptions> options,
            TimeProvider clock,
            ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

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

        /// <summary>
        /// Ngan co chu dich. Access token KHONG thu hoi duoc — no tu chua moi
        /// thu, service kiem no khong hoi lai ai. Tai khoan bi khoa van dung
        /// duoc token cu toi khi het han (VD-S-11). Setting identity.access_token_lifetime.
        /// </summary>
        public TimeSpan AccessTokenLifetime
        {
            get { return _settings.ThoiGian(SettingKeys.IdentityAccessTokenLifetime); }
        }

        /// <summary>Dai hon nhieu vi refresh token THU HOI DUOC. Setting identity.refresh_token_lifetime.</summary>
        public TimeSpan RefreshTokenLifetime
        {
            get { return _settings.ThoiGian(SettingKeys.IdentityRefreshTokenLifetime); }
        }

        /// <summary>
        /// Tao access token. No TU CHUA moi thu service khac can biet — ai,
        /// vai tro gi, het han luc nao — nen service kiem no KHONG phai goi lai
        /// identity-svc. Do la dieu lam auth chiu duoc tai: neu moi request
        /// deu phai hoi identity-svc thi no thanh nut that cua ca 17 service.
        /// </summary>
        public string TaoAccessToken(User user)
        {
            return TaoAccessToken(user, false);
        }

        /// <param name="kybDaDuyet">Ho so doanh nghiep cua user da duoc admin duyet (claim kyb_verified).</param>
        public string TaoAccessToken(User user, bool kybDaDuyet)
        {
            return TaoAccessToken(user, kybDaDuyet, false, false);
        }

        /// <param name="kybDaDuyet">Ho so doanh nghiep cua user da duoc admin duyet (claim kyb_verified).</param>
        /// <param name="daXacMinhDanhTinh">Ho so xac minh danh tinh ca nhan da duoc duyet (claim identity_verified).</param>
        /// <param name="duTuoi">Da xac minh va du tuoi tinh toi luc phat token (claim adult).</param>
        public string TaoAccessToken(User user, bool kybDaDuyet, bool daXacMinhDanhTinh, bool duTuoi)
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

            // Hai su that mot chieu, service khac doc tu token thay vi giu ban sao (CrowdClaims).
            claims.Add(new Claim(CrowdClaims.EmailVerified, user.EmailVerifiedAt.HasValue ? "true" : "false", ClaimValueTypes.Boolean));
            claims.Add(new Claim(CrowdClaims.KybVerified, kybDaDuyet ? "true" : "false", ClaimValueTypes.Boolean));
            claims.Add(new Claim(CrowdClaims.IdentityVerified, daXacMinhDanhTinh ? "true" : "false", ClaimValueTypes.Boolean));
            claims.Add(new Claim(CrowdClaims.Adult, duTuoi ? "true" : "false", ClaimValueTypes.Boolean));

            // Ma dinh danh cua CHINH token nay — nen de sau nay lam danh sach
            // thu hoi (VD-S-11) ma khong phai thu hoi ca tai khoan.
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()));

            SecurityTokenDescriptor moTa = new SecurityTokenDescriptor();
            moTa.Issuer = _options.Issuer;
            moTa.Audience = _options.Audience;
            moTa.IssuedAt = bayGio.UtcDateTime;
            moTa.NotBefore = bayGio.UtcDateTime;
            moTa.Expires = (bayGio + AccessTokenLifetime).UtcDateTime;
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

        /// <summary>
        /// Refresh token cua mot phien: "{ma phien base64url}.{32 byte ngau nhien base64url}". Ma phien de
        /// tim dong login_sessions bang khoa chinh; phan ngau nhien la bi mat (database chi giu hash ca chuoi).
        /// </summary>
        public static string TaoRefreshToken(Guid phienId)
        {
            return Base64UrlEncoder.Encode(phienId.ToByteArray()) + "." + TaoRefreshTokenGoc();
        }

        /// <summary>Doc ma phien tu refresh token. false neu token khong dung dinh dang.</summary>
        public static bool DocPhien(string? token, out Guid phienId)
        {
            phienId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            int cham = token.IndexOf('.', StringComparison.Ordinal);
            if (cham <= 0 || cham == token.Length - 1)
            {
                return false;
            }

            try
            {
                byte[] du = Base64UrlEncoder.DecodeBytes(token.Substring(0, cham));
                if (du.Length != 16)
                {
                    return false;
                }

                phienId = new Guid(du);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
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
