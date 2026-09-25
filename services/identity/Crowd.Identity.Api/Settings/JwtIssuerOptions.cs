using System;

namespace Crowd.Identity.Api.Settings
{
    /// <summary>
    /// Thong so PHAT token — chi identity-svc co. 16 service con lai chi co
    /// CrowdJwtOptions de KIEM token.
    /// </summary>
    public sealed class JwtIssuerOptions
    {
        public const string SectionName = "JwtIssuer";

        /// <summary>Phai trung voi Auth:Issuer cua moi service kiem token.</summary>
        public string Issuer { get; set; } = "crowd-identity";

        public string Audience { get; set; } = "crowd-api";

        /// <summary>
        /// Ngan co chu dich. Access token KHONG thu hoi duoc — no tu chua moi
        /// thu, service kiem no khong hoi lai ai. Tai khoan bi khoa van dung
        /// duoc token cu toi khi het han. 15 phut la cua so rui ro chap nhan
        /// duoc (VD-S-11).
        /// </summary>
        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>Dai hon nhieu vi refresh token THU HOI DUOC — no nam trong database.</summary>
        public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

        /// <summary>
        /// Duong dan file khoa rieng, tinh tu thu muc goc cua project.
        /// Thu muc secrets/ va duoi .pem deu da nam trong .gitignore.
        /// </summary>
        public string SigningKeyPath { get; set; } = "secrets/signing-key.pem";
    }
}
