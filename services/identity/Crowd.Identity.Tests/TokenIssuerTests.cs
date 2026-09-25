using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Services;
using Crowd.Identity.Api.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.Identity.Tests
{
    public sealed class TokenIssuerTests : IDisposable
    {
        private static readonly string[] HaiVaiTro = new string[] { "labeler", "sharer" };

        private readonly SigningKeyProvider _khoa;
        private readonly TokenIssuer _issuer;
        private readonly User _user;

        public TokenIssuerTests()
        {
            _khoa = new SigningKeyProvider(RSA.Create(2048));

            _issuer = new TokenIssuer(
                _khoa,
                Options.Create(new JwtIssuerOptions()),
                TimeProvider.System);

            _user = User.Tao(
                "an@example.com",
                "An",
                new List<string> { CrowdRoles.Labeler, CrowdRoles.Sharer },
                DateTimeOffset.UtcNow);
        }

        public void Dispose()
        {
            _khoa.Dispose();
        }

        /// <summary>Kiem token y het cach AddCrowdJwtAuthentication kiem.</summary>
        private static TokenValidationParameters ThamSoKiem(SecurityKey khoaCongKhai)
        {
            TokenValidationParameters p = new TokenValidationParameters();
            p.ValidIssuer = "crowd-identity";
            p.ValidAudience = "crowd-api";
            p.IssuerSigningKey = khoaCongKhai;
            p.ValidAlgorithms = new string[] { SecurityAlgorithms.RsaSha256 };
            p.ClockSkew = TimeSpan.FromSeconds(30);
            return p;
        }

        private static RsaSecurityKey ChiPhanCongKhai(RSA rsa)
        {
            return new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false));
        }

        [Fact]
        public async Task Token_kiem_duoc_bang_khoa_CONG_KHAI_va_mang_dung_claim()
        {
            // Mo phong dung dieu 16 service kia lam: chi co phan cong khai.
            RSA congKhai = RSA.Create();
            congKhai.ImportParameters(((RSA)_khoa.KhoaKy.Rsa).ExportParameters(false));

            string token = _issuer.TaoAccessToken(_user);

            TokenValidationResult ketQua = await new JsonWebTokenHandler()
                .ValidateTokenAsync(token, ThamSoKiem(new RsaSecurityKey(congKhai)));

            Assert.True(ketQua.IsValid, ketQua.Exception?.Message);
            Assert.Equal(_user.Id.ToString(), ketQua.Claims[CrowdClaims.Subject]);
            Assert.Equal("an@example.com", ketQua.Claims[CrowdClaims.Email]);
            Assert.Equal("An", ketQua.Claims[CrowdClaims.Name]);
            List<string> vaiTro = ketQua.ClaimsIdentity.FindAll(CrowdClaims.Role)
                .Select(c => c.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(HaiVaiTro, vaiTro);
        }

        [Fact]
        public async Task Token_bi_tu_choi_khi_kiem_bang_khoa_KHAC()
        {
            // Ke gia mao tu ky token bang khoa cua minh — service kiem bang khoa
            // that cua identity phai tu choi.
            using (RSA khoaKhac = RSA.Create(2048))
            {
                string token = _issuer.TaoAccessToken(_user);

                TokenValidationResult ketQua = await new JsonWebTokenHandler()
                    .ValidateTokenAsync(token, ThamSoKiem(ChiPhanCongKhai(khoaKhac)));

                Assert.False(ketQua.IsValid);
            }
        }

        [Fact]
        public void Token_ky_bang_RS256_va_mang_kid()
        {
            JsonWebToken jwt = new JsonWebToken(_issuer.TaoAccessToken(_user));

            Assert.Equal("RS256", jwt.Alg);

            // kid phai trung voi JWKS de service kiem biet dung khoa nao.
            Assert.Equal(_khoa.KeyId, jwt.Kid);
        }

        [Fact]
        public void Token_song_dung_15_phut()
        {
            JsonWebToken jwt = new JsonWebToken(_issuer.TaoAccessToken(_user));

            Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        }

        [Fact]
        public void JWKS_khong_bao_gio_chua_phan_rieng_cua_khoa()
        {
            // Modulus va Exponent la phan cong khai; neu lop nay vo tinh xuat ca
            // phan rieng thi ai cung tu phat token duoc.
            RSAParameters phanRieng = ((RSA)_khoa.KhoaKy.Rsa).ExportParameters(true);

            string d = Base64UrlEncoder.Encode(phanRieng.D);

            Assert.NotEqual(d, _khoa.Modulus);
            Assert.NotEqual(d, _khoa.Exponent);
            Assert.Equal(Base64UrlEncoder.Encode(phanRieng.Modulus), _khoa.Modulus);
        }

        [Fact]
        public void Hash_refresh_token_tat_dinh_va_dai_64_ky_tu_hex()
        {
            string goc = TokenIssuer.TaoRefreshTokenGoc();

            string lan1 = TokenIssuer.BamRefreshToken(goc);
            string lan2 = TokenIssuer.BamRefreshToken(goc);

            // Tat dinh: phai tra cuu lai duoc trong database bang hash.
            Assert.Equal(lan1, lan2);
            Assert.Equal(64, lan1.Length);
            Assert.NotEqual(goc, lan1);
        }

        [Fact]
        public void Refresh_token_goc_khong_bao_gio_trung()
        {
            HashSet<string> daThay = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < 1000; i++)
            {
                Assert.True(daThay.Add(TokenIssuer.TaoRefreshTokenGoc()));
            }
        }
    }
}
