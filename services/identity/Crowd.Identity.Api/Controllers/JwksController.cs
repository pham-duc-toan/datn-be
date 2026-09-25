using System;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Services;
using Crowd.Identity.Api.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Cong bo KHOA CONG KHAI de 16 service kiem token ma khong can hoi lai
    /// identity-svc cho tung request.
    ///
    /// Theo chuan OpenID Connect Discovery: service kiem token doc tai lieu
    /// discovery, tu do tim ra jwks_uri, tai khoa ve va cache. Khi identity xoay
    /// khoa, token moi mang kid moi; service gap kid la se tu tai lai JWKS.
    /// Khong phai deploy lai ai ca.
    /// </summary>
    [ApiController]
    [Route(".well-known")]
    public sealed class JwksController : ControllerBase
    {
        /// <summary>Chi RS256 — khop voi ValidAlgorithms phia kiem token.</summary>
        private static readonly string[] ThuatToanHoTro = new string[] { "RS256" };

        private readonly SigningKeyProvider _khoa;
        private readonly JwtIssuerOptions _options;

        public JwksController(SigningKeyProvider khoa, IOptions<JwtIssuerOptions> options)
        {
            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _khoa = khoa;
            _options = options.Value;
        }

        /// <summary>GET /.well-known/openid-configuration</summary>
        [HttpGet("openid-configuration")]
        public IActionResult TaiLieuDiscovery()
        {
            // jwks_uri dung CHINH dia chi ma ben hoi da dung de goi toi day.
            // Trinh duyet hoi qua localhost:8101 thi nhan localhost:8101; service
            // trong Docker hoi qua identity-svc:8101 thi nhan identity-svc:8101.
            // Ca hai deu tai duoc khoa, khong phai cau hinh cung dia chi nao.
            string goc = Request.Scheme + "://" + Request.Host;

            DiscoveryResponse phanHoi = new DiscoveryResponse
            {
                Issuer = _options.Issuer,
                JwksUri = goc + "/.well-known/jwks.json",
                SigningAlgValuesSupported = ThuatToanHoTro,
            };

            return Ok(phanHoi);
        }

        /// <summary>GET /.well-known/jwks.json</summary>
        [HttpGet("jwks.json")]
        public IActionResult DanhSachKhoa()
        {
            JwkResponse khoa = new JwkResponse
            {
                Kty = "RSA",
                Use = "sig",
                Alg = "RS256",
                Kid = _khoa.KeyId,
                N = _khoa.Modulus,
                E = _khoa.Exponent,
            };

            JwksResponse phanHoi = new JwksResponse
            {
                Keys = new JwkResponse[] { khoa },
            };

            return Ok(phanHoi);
        }
    }
}
