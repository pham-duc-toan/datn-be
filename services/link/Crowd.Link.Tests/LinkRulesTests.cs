using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Security;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;

namespace Crowd.Link.Tests
{
    public sealed class LinkRulesTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Chuan_hoa_link_dich_va_ten_mien()
        {
            UrlChuan u = UrlRules.ChuanHoa("  https://Sub.Example.COM/a?b=1  ");
            Assert.Equal("https://sub.example.com/a?b=1", u.Url);
            Assert.Equal("sub.example.com", u.TenMien);
        }

        [Theory]
        [InlineData("")]
        [InlineData("ftp://example.com/x")]
        [InlineData("example.com")]
        [InlineData("https://google.com@evil.com/")]
        [InlineData("http://localhost:8080/")]
        [InlineData("http://192.168.1.10/admin")]
        [InlineData("http://127.0.0.1/")]
        public void Tu_choi_link_dich_khong_hop_le(string url)
        {
            LinkException ex = Assert.Throws<LinkException>(() => UrlRules.ChuanHoa(url));
            Assert.Equal("url_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Chan_ten_mien_chan_ca_ten_mien_con_nhung_khong_chan_ten_giong()
        {
            List<string> chan = new List<string> { "casino.com" };
            Assert.Equal("casino.com", UrlRules.TenMienBiChan("casino.com", chan));
            Assert.Equal("casino.com", UrlRules.TenMienBiChan("vip.casino.com", chan));
            Assert.Null(UrlRules.TenMienBiChan("notcasino.com", chan));

            Assert.Equal(new List<string> { "a.b.x.com", "b.x.com", "x.com", "com" }, UrlRules.CacTenMienCha("a.b.x.com"));
        }

        [Fact]
        public void Alias_chi_gom_chu_so_gach()
        {
            UrlRules.KiemAlias("khuyen-mai_10");
            Assert.Throws<LinkException>(() => UrlRules.KiemAlias("ab"));
            Assert.Throws<LinkException>(() => UrlRules.KiemAlias("co dau cach"));
            Assert.Throws<LinkException>(() => UrlRules.KiemAlias("../admin"));
            Assert.Equal(9, UrlRules.SinhMa(9).Length);
        }

        [Fact]
        public void Bao_cao_vuot_nguong_vao_hang_doi_mot_lan()
        {
            ShortLink l = ShortLink.Tao(Guid.NewGuid(), "abc1234", "https://x.com/", "x.com", null, null, null, null, Luc);
            l.KichHoat(Luc);

            Assert.False(l.GhiBaoCao(2, Luc));
            Assert.True(l.GhiBaoCao(2, Luc));
            Assert.True(l.NeedsReview);
            Assert.False(l.GhiBaoCao(2, Luc));

            l.BoQuaBaoCao(Luc);
            Assert.False(l.NeedsReview);
        }

        [Fact]
        public void Link_cho_quet_moi_kich_hoat_hoac_chan_duoc()
        {
            ShortLink l = ShortLink.Tao(Guid.NewGuid(), "abc1234", "https://x.com/", "x.com", null, null, null, null, Luc);
            l.Chan("doc hai", Luc);
            Assert.Equal(LinkStatus.Blocked, l.Status);
            Assert.Throws<LinkException>(() => l.KichHoat(Luc));
            Assert.False(l.VoHieuHoa("x", Luc));

            Assert.Throws<LinkException>(() =>
                ShortLink.Tao(Guid.NewGuid(), "abc", "https://x.com/", "x.com", null, Luc.AddMinutes(-1), null, null, Luc));
        }

        [Fact]
        public void Mat_khau_link_bam_va_kiem()
        {
            string bam = MatKhauLink.Bam("bi-mat-123");
            Assert.StartsWith("pbkdf2-sha256$", bam);
            Assert.True(MatKhauLink.Kiem("bi-mat-123", bam));
            Assert.False(MatKhauLink.Kiem("bi-mat-124", bam));
            Assert.False(MatKhauLink.Kiem("bi-mat-123", "hong"));
            Assert.NotEqual(bam, MatKhauLink.Bam("bi-mat-123"));
        }

        [Fact]
        public void Bam_ip_co_muoi_va_doi_theo_ngay()
        {
            Assert.Equal(BamIp.Bam("1.2.3.4", "m"), BamIp.Bam("1.2.3.4", "m"));
            Assert.NotEqual(BamIp.Bam("1.2.3.4", "m"), BamIp.Bam("1.2.3.4", "khac"));
            Assert.NotEqual(BamIp.BamTheoNgay("1.2.3.4", "m", Luc), BamIp.BamTheoNgay("1.2.3.4", "m", Luc.AddDays(1)));
            Assert.Equal(BamIp.BamTheoNgay("1.2.3.4", "m", Luc), BamIp.BamTheoNgay("1.2.3.4", "m", Luc.AddHours(3)));
        }
    }
}
