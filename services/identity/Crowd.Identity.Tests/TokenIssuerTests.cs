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
using Crowd.Settings;
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
                TimeProvider.System,
                new SettingsStore());

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
        public void Token_mang_claim_xac_thuc_email_va_KYB()
        {
            // Tai khoan moi: chua xac thuc email, chua KYB.
            JsonWebToken chua = new JsonWebToken(_issuer.TaoAccessToken(_user));
            ClaimsPrincipal p0 = new ClaimsPrincipal(new ClaimsIdentity(chua.Claims));
            Assert.False(CrowdClaims.DaXacThucEmail(p0));
            Assert.False(CrowdClaims.DaXacMinhDoanhNghiep(p0));

            _user.XacThucEmail(DateTimeOffset.UtcNow);
            JsonWebToken roi = new JsonWebToken(_issuer.TaoAccessToken(_user, true));
            ClaimsPrincipal p1 = new ClaimsPrincipal(new ClaimsIdentity(roi.Claims));
            Assert.True(CrowdClaims.DaXacThucEmail(p1));
            Assert.True(CrowdClaims.DaXacMinhDoanhNghiep(p1));
        }

        [Fact]
        public void Refresh_token_mang_ma_phien_doc_lai_duoc()
        {
            Guid phien = Guid.CreateVersion7();
            string token = TokenIssuer.TaoRefreshToken(phien);

            Guid doc;
            Assert.True(TokenIssuer.DocPhien(token, out doc));
            Assert.Equal(phien, doc);
            // Hai token cua cung phien khac nhau (phan bi mat ngau nhien).
            Assert.NotEqual(token, TokenIssuer.TaoRefreshToken(phien));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("khong-co-dau-cham")]
        [InlineData(".chi-co-bi-mat")]
        [InlineData("AAAA.bi-mat")]
        [InlineData("!!!!.bi-mat")]
        public void Refresh_token_sai_dinh_dang_thi_khong_doc_duoc_phien(string? token)
        {
            Guid doc;
            Assert.False(TokenIssuer.DocPhien(token, out doc));
        }

        [Fact]
        public void Phien_xoay_tai_cho_token_cu_khong_con_la_token_hien_tai()
        {
            DateTimeOffset luc = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
            Guid id = Guid.CreateVersion7();
            string t0 = TokenIssuer.TaoRefreshToken(id);
            PhienDangNhap p = PhienDangNhap.Mo(id, _user.Id, TokenIssuer.BamRefreshToken(t0), luc, TimeSpan.FromDays(14));

            Assert.True(p.ConDung(luc));
            Assert.True(p.LaTokenHienTai(TokenIssuer.BamRefreshToken(t0)));

            string t1 = TokenIssuer.TaoRefreshToken(id);
            p.Xoay(TokenIssuer.BamRefreshToken(t1), luc.AddMinutes(15), TimeSpan.FromDays(14));

            // Token cu (dung ma phien, hash khac) = dau hieu dung lai; token moi la hien tai.
            Assert.False(p.LaTokenHienTai(TokenIssuer.BamRefreshToken(t0)));
            Assert.True(p.LaTokenHienTai(TokenIssuer.BamRefreshToken(t1)));
            Assert.Equal(1, p.Generation);
            // Han truot theo lan lam moi cuoi.
            Assert.Equal(luc.AddMinutes(15).AddDays(14), p.ExpiresAt);

            p.ThuHoi(luc.AddHours(1));
            Assert.False(p.ConDung(luc.AddHours(1)));
            Assert.False(p.ConDung(luc.AddDays(30)));
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
