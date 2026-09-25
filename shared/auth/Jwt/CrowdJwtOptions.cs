namespace Crowd.BuildingBlocks.Auth.Jwt
{
    /// <summary>
    /// Thong so de MOT service kiem access token do identity-svc phat ra.
    /// Doc tu muc "Auth" trong appsettings.
    /// </summary>
    public sealed class CrowdJwtOptions
    {
        public const string SectionName = "Auth";

        /// <summary>
        /// Ten logic cua ben phat token, KHONG phai URL.
        ///
        /// Vi sao khong dung URL: trinh duyet goi identity qua localhost:8101,
        /// con service trong Docker goi qua identity-svc:8101. Neu issuer la URL
        /// thi hai ben thay hai gia tri khac nhau va token bi tu choi. Ten logic
        /// thi giong nhau o moi noi.
        /// </summary>
        public string Issuer { get; set; } = "crowd-identity";

        /// <summary>Token phat cho API cua he thong nay, khong dung duoc o noi khac.</summary>
        public string Audience { get; set; } = "crowd-api";

        /// <summary>
        /// Dia chi tai lieu discovery cua identity-svc. Tu day service tu tai
        /// khoa cong khai (JWKS) va tu tai lai khi identity xoay khoa — khong
        /// phai deploy lai 16 service.
        /// </summary>
        public string MetadataAddress { get; set; } =
            "http://localhost:8101/.well-known/openid-configuration";

        /// <summary>Chi tat o moi truong dev chay http thuan.</summary>
        public bool RequireHttpsMetadata { get; set; } = true;
    }
}
