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

        // Thoi han access / refresh token: setting identity.access_token_lifetime /
        // identity.refresh_token_lifetime (admin-svc) — khong con o day.

        /// <summary>
        /// Duong dan file khoa rieng, tinh tu thu muc goc cua project.
        /// Thu muc secrets/ va duoi .pem deu da nam trong .gitignore.
        /// </summary>
        public string SigningKeyPath { get; set; } = "secrets/signing-key.pem";
    }
}
