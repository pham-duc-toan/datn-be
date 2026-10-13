using System;
using System.Collections.Generic;
using System.Security.Claims;
using Crowd.BuildingBlocks.Settings;
using Crowd.Gateway.Api.RateLimiting;
using Crowd.Gateway.Api.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.Gateway.Tests
{
    public sealed class ThuHoiPhienTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Token_phat_truoc_moc_bi_thu_hoi_phat_sau_hoac_cung_giay_thi_khong()
        {
            ThuHoiPhienStore store = new ThuHoiPhienStore(new SettingMacDinh(), new DongHoCoDinh(T));
            Guid u = Guid.NewGuid();
            store.Ghi(u, T);

            Assert.True(store.DaThuHoi(u, T.AddSeconds(-1)));
            Assert.False(store.DaThuHoi(u, T));
            Assert.False(store.DaThuHoi(u, T.AddSeconds(1)));
            Assert.False(store.DaThuHoi(Guid.NewGuid(), T.AddDays(-1)));
        }

        [Fact]
        public void Giu_moc_muon_nhat_khi_event_den_lech_thu_tu()
        {
            ThuHoiPhienStore store = new ThuHoiPhienStore(new SettingMacDinh(), new DongHoCoDinh(T));
            Guid u = Guid.NewGuid();
            store.Ghi(u, T.AddMinutes(5));
            store.Ghi(u, T);

            Assert.True(store.DaThuHoi(u, T.AddMinutes(4)));
        }

        [Fact]
        public void Don_moc_cu_hon_tuoi_access_token()
        {
            // access token mac dinh 15 phut: moc 20 phut truoc het tac dung, moc 5 phut truoc con.
            ThuHoiPhienStore store = new ThuHoiPhienStore(new SettingMacDinh(), new DongHoCoDinh(T));
            store.Ghi(Guid.NewGuid(), T.AddMinutes(-20));
            store.Ghi(Guid.NewGuid(), T.AddMinutes(-5));

            Assert.Equal(1, store.DonMocCu());
            Assert.Equal(1, store.SoMoc);
        }

        [Fact]
        public void Doc_sub_va_iat_tu_token_khong_can_chu_ky()
        {
            Guid u = Guid.NewGuid();
            string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new List<Claim> { new Claim("sub", u.ToString()) }),
                IssuedAt = T.UtcDateTime,
                NotBefore = T.UtcDateTime,
                Expires = T.AddMinutes(15).UtcDateTime,
            });
            DefaultHttpContext ctx = new DefaultHttpContext();
            ctx.Request.Headers.Authorization = "Bearer " + token;

            TokenChuaKiem? doc = TokenChuaKiem.Doc(ctx.Request);

            Assert.NotNull(doc);
            Assert.Equal(u, doc.UserId);
            Assert.Equal(T, doc.IssuedAt);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Basic abc")]
        [InlineData("Bearer khong-phai-jwt")]
        public void Header_khong_doc_duoc_thi_null(string? header)
        {
            DefaultHttpContext ctx = new DefaultHttpContext();
            if (header != null)
            {
                ctx.Request.Headers.Authorization = header;
            }

            Assert.Null(TokenChuaKiem.Doc(ctx.Request));
        }

        [Theory]
        [InlineData("POST", "/auth/login", true)]
        [InlineData("POST", "/auth/forgot-password", true)]
        [InlineData("POST", "/auth/refresh", false)]
        [InlineData("GET", "/auth/login", false)]
        public void Phan_loai_duong_xac_thuc(string method, string path, bool mongDoi)
        {
            DefaultHttpContext ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;

            Assert.Equal(mongDoi, GioiHanTanSuat.LaXacThuc(ctx.Request));
        }

        [Theory]
        [InlineData("POST", "/ledger/withdrawals", true)]
        [InlineData("POST", "/payments/deposits/manual", true)]
        [InlineData("GET", "/ledger/withdrawals/mine", false)]
        public void Phan_loai_duong_tao_lenh_tien(string method, string path, bool mongDoi)
        {
            DefaultHttpContext ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;

            Assert.Equal(mongDoi, GioiHanTanSuat.LaTaoLenhTien(ctx.Request));
        }

        /// <summary>Setting = gia tri mac dinh trong catalog.</summary>
        private sealed class SettingMacDinh : ISettings
        {
            public bool DungSai(string key)
            {
                return SettingCatalog.Lay(key).DefaultValue.GetValue<bool>();
            }

            public int SoNguyen(string key)
            {
                return (int)SettingCatalog.Lay(key).DefaultValue.GetValue<double>();
            }

            public long SoLon(string key)
            {
                return (long)SettingCatalog.Lay(key).DefaultValue.GetValue<double>();
            }

            public double SoThuc(string key)
            {
                return SettingCatalog.Lay(key).DefaultValue.GetValue<double>();
            }

            public TimeSpan ThoiGian(string key)
            {
                return TimeSpan.FromSeconds(SoThuc(key));
            }

            public string Chuoi(string key)
            {
                return SettingCatalog.Lay(key).DefaultValue.GetValue<string>();
            }
        }

        private sealed class DongHoCoDinh : TimeProvider
        {
            private readonly DateTimeOffset _luc;

            public DongHoCoDinh(DateTimeOffset luc)
            {
                _luc = luc;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _luc;
            }
        }
    }
}
