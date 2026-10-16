using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Services;
using Microsoft.Extensions.Options;

namespace Crowd.Identity.Tests
{
    public sealed class TotpTests
    {
        // Bi mat cua phu luc B, RFC 6238 (SHA-1): "12345678901234567890".
        private static readonly byte[] BiMatRfc = Encoding.ASCII.GetBytes("12345678901234567890");

        [Theory]
        // RFC 6238 phu luc B cho ma 8 chu so; ma 6 chu so = 6 chu so cuoi (cung phep cat dong, mod 10^6).
        [InlineData(59L, "287082")]
        [InlineData(1111111109L, "081804")]
        [InlineData(1111111111L, "050471")]
        [InlineData(1234567890L, "005924")]
        [InlineData(2000000000L, "279037")]
        public void Khop_vector_chuan_RFC_6238(long unixGiay, string ma)
        {
            long buoc = Totp.BuocHienTai(DateTimeOffset.FromUnixTimeSeconds(unixGiay));
            Assert.Equal(ma, Totp.TinhMa(BiMatRfc, buoc));
        }

        [Fact]
        public void Chap_nhan_lech_mot_buoc_va_tu_choi_lech_hai_buoc()
        {
            DateTimeOffset luc = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            long buoc = Totp.BuocHienTai(luc);

            Assert.Equal(buoc - 1, Totp.Kiem(BiMatRfc, Totp.TinhMa(BiMatRfc, buoc - 1), luc, 0));
            Assert.Equal(buoc + 1, Totp.Kiem(BiMatRfc, Totp.TinhMa(BiMatRfc, buoc + 1), luc, 0));
            Assert.Equal(-1, Totp.Kiem(BiMatRfc, Totp.TinhMa(BiMatRfc, buoc - 2), luc, 0));
        }

        [Fact]
        public void Ma_da_dung_khong_dung_lai_duoc()
        {
            DateTimeOffset luc = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            long buoc = Totp.BuocHienTai(luc);
            string ma = Totp.TinhMa(BiMatRfc, buoc);

            Assert.Equal(buoc, Totp.Kiem(BiMatRfc, ma, luc, 0));
            // Lan sau truyen buoc vua dung → cung ma bi tu choi (chong dung trom ma trong 30 giay).
            Assert.Equal(-1, Totp.Kiem(BiMatRfc, ma, luc, buoc));
        }

        [Theory]
        [InlineData("123 456", "123456")]
        [InlineData("123-456", "123456")]
        [InlineData("12345", null)]
        [InlineData("12345a", null)]
        [InlineData(null, null)]
        public void Chuan_hoa_ma_nhap(string? vao, string? ra)
        {
            Assert.Equal(ra, Totp.ChuanHoaMa(vao));
        }

        [Fact]
        public void Base32_ma_va_giai_nguoc_lai_duoc_va_dung_vector_RFC_4648()
        {
            Assert.Equal("MZXW6YTBOI", Base32.Ma(Encoding.ASCII.GetBytes("foobar")));
            byte[] ngauNhien = Totp.TaoBiMat();
            Assert.Equal(ngauNhien, Base32.Giai(Base32.Ma(ngauNhien)));
        }

        [Fact]
        public void Link_otpauth_co_du_tham_so()
        {
            string link = Totp.TaoLinkOtpAuth("DATN Crowd", "a@b.c", BiMatRfc);
            Assert.StartsWith("otpauth://totp/DATN%20Crowd:a%40b.c?secret=", link);
            Assert.Contains("&issuer=DATN%20Crowd", link);
            Assert.Contains("&digits=6&period=30", link);
        }
    }

    public sealed class KhoaDuLieuTests
    {
        private static KhoaDuLieu Tao(byte[] goc)
        {
            return new KhoaDuLieu(Options.Create(new KhoaDuLieuOptions { Key = Convert.ToBase64String(goc) }));
        }

        [Fact]
        public void Ma_hoa_giai_ma_duoc_va_moi_lan_ban_ma_khac_nhau()
        {
            KhoaDuLieu k = Tao(RandomNumberGenerator.GetBytes(32));
            byte[] biMat = Totp.TaoBiMat();

            string a = k.MaHoa(biMat);
            string b = k.MaHoa(biMat);
            Assert.NotEqual(a, b);
            Assert.Equal(biMat, k.GiaiMa(a));
        }

        [Fact]
        public void Ban_ma_bi_sua_hoac_sai_khoa_thi_khong_giai_duoc()
        {
            KhoaDuLieu k = Tao(RandomNumberGenerator.GetBytes(32));
            byte[] tatCa = Convert.FromBase64String(k.MaHoa(Totp.TaoBiMat()));
            tatCa[15] ^= 1;
            Assert.ThrowsAny<CryptographicException>(() => k.GiaiMa(Convert.ToBase64String(tatCa)));

            KhoaDuLieu khac = Tao(RandomNumberGenerator.GetBytes(32));
            Assert.ThrowsAny<CryptographicException>(() => khac.GiaiMa(k.MaHoa(Totp.TaoBiMat())));
        }

        [Fact]
        public void Bam_co_khoa_tat_dinh_theo_khoa()
        {
            byte[] goc = RandomNumberGenerator.GetBytes(32);
            Assert.Equal(Tao(goc).BamCoKhoa("001099012345"), Tao(goc).BamCoKhoa("001099012345"));
            Assert.NotEqual(Tao(goc).BamCoKhoa("001099012345"), Tao(RandomNumberGenerator.GetBytes(32)).BamCoKhoa("001099012345"));
        }

        [Fact]
        public void Khoa_sai_do_dai_thi_bao_loi_luc_khoi_dong()
        {
            Assert.Throws<InvalidOperationException>(() => Tao(new byte[16]));
            Assert.Throws<InvalidOperationException>(() => new KhoaDuLieu(Options.Create(new KhoaDuLieuOptions { Key = "khong-phai-base64" })));
        }
    }

    public sealed class HaiLopTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("abcde-fghjk", "ABCDEFGHJK")]
        [InlineData("ABCDE FGHJK", "ABCDEFGHJK")]
        [InlineData("ABCDE-FGHJ", null)]
        [InlineData("ABCDE-FGHJ0", null)]
        public void Chuan_hoa_ma_du_phong(string vao, string? ra)
        {
            Assert.Equal(ra, HaiLopService.ChuanHoaMaDuPhong(vao));
        }

        [Fact]
        public void Ma_du_phong_dung_mot_lan()
        {
            HaiLop h = HaiLop.BatDauCaiDat(Guid.NewGuid(), "x", T);
            h.Bat(10, new List<string> { "h1", "h2" }, T);

            Assert.True(h.DangBat);
            Assert.True(h.DungMaDuPhong("h1"));
            Assert.False(h.DungMaDuPhong("h1"));
            Assert.Single(h.RecoveryHashes);
        }

        [Fact]
        public void Dang_bat_thi_khong_doi_bi_mat_duoc()
        {
            HaiLop h = HaiLop.BatDauCaiDat(Guid.NewGuid(), "x", T);
            h.DoiBiMat("y", T);
            h.Bat(1, new List<string>(), T);
            Assert.Throws<InvalidOperationException>(() => h.DoiBiMat("z", T));
        }

        [Fact]
        public void Thu_thach_het_luot_sau_so_lan_sai_toi_da()
        {
            MaTaiKhoan m = MaTaiKhoan.Tao(Guid.NewGuid(), MucDichMa.TwoFactorLogin, "h", T, TimeSpan.FromMinutes(5));
            Assert.False(m.GhiSai(3, T));
            Assert.False(m.GhiSai(3, T));
            Assert.True(m.GhiSai(3, T));
            Assert.False(m.DungDuoc(T));
        }
    }

    public sealed class XacMinhDanhTinhTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        private static readonly DateOnly HomNay = new DateOnly(2026, 10, 14);

        private static XacMinhDanhTinh DuDieuKien(DateOnly ngaySinh)
        {
            XacMinhDanhTinh x = XacMinhDanhTinh.Tao(Guid.NewGuid(), T);
            string so = XacMinhDanhTinh.ChuanHoaSoGiayTo("001 099 012 345");
            x.CapNhat("Nguyen Van A", ngaySinh, so, "bam", HomNay, T);
            x.GanAnh(true, "idv/a/front.jpg", T);
            x.GanAnh(false, "idv/a/back.jpg", T);
            return x;
        }

        [Theory]
        [InlineData("001 099-012.345", "001099012345")]
        [InlineData("123456789", "123456789")]
        [InlineData("c1234567", "C1234567")]
        public void Chuan_hoa_so_giay_to_hop_le(string vao, string ra)
        {
            Assert.Equal(ra, XacMinhDanhTinh.ChuanHoaSoGiayTo(vao));
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("1234567890")]
        [InlineData("00109901234@")]
        [InlineData("")]
        public void So_giay_to_sai_bi_tu_choi(string vao)
        {
            Assert.Equal("so_giay_to_khong_hop_le", Assert.Throws<InvalidValueException>(() => XacMinhDanhTinh.ChuanHoaSoGiayTo(vao)).Code);
        }

        [Fact]
        public void Chi_luu_bon_so_cuoi()
        {
            XacMinhDanhTinh x = DuDieuKien(new DateOnly(2000, 1, 1));
            Assert.Equal("2345", x.IdNumberLast4);
        }

        [Fact]
        public void Tinh_tuoi_dung_ngay_sinh_nhat()
        {
            Assert.Equal(17, XacMinhDanhTinh.TinhTuoi(new DateOnly(2008, 10, 15), HomNay));
            Assert.Equal(18, XacMinhDanhTinh.TinhTuoi(new DateOnly(2008, 10, 14), HomNay));
            // Sinh 29/2: nam khong nhuan thi du tuoi tu 28/2 (AddYears cua .NET).
            Assert.Equal(18, XacMinhDanhTinh.TinhTuoi(new DateOnly(2008, 2, 29), new DateOnly(2026, 2, 28)));
        }

        [Fact]
        public void Du_tuoi_chi_khi_da_xac_minh()
        {
            XacMinhDanhTinh x = DuDieuKien(new DateOnly(2000, 1, 1));
            Assert.False(x.DuTuoi(18, HomNay));

            x.Nop(T);
            Assert.False(x.DuTuoi(18, HomNay));

            x.Duyet(Guid.NewGuid(), T);
            Assert.True(x.DuTuoi(18, HomNay));
        }

        [Fact]
        public void Da_xac_minh_nhung_chua_du_tuoi()
        {
            XacMinhDanhTinh x = DuDieuKien(new DateOnly(2010, 5, 1));
            x.Nop(T);
            x.Duyet(Guid.NewGuid(), T);
            Assert.False(x.DuTuoi(18, HomNay));
        }

        [Fact]
        public void Nop_khi_thieu_anh_bi_tu_choi()
        {
            XacMinhDanhTinh x = XacMinhDanhTinh.Tao(Guid.NewGuid(), T);
            x.CapNhat("A", new DateOnly(2000, 1, 1), "001099012345", "bam", HomNay, T);
            x.GanAnh(true, "f", T);
            Assert.Equal("thieu_anh_giay_to", Assert.Throws<RuleViolationException>(() => x.Nop(T)).Code);
        }

        [Fact]
        public void Ngay_sinh_tuong_lai_bi_tu_choi()
        {
            XacMinhDanhTinh x = XacMinhDanhTinh.Tao(Guid.NewGuid(), T);
            Assert.Equal("ngay_sinh_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => x.CapNhat("A", new DateOnly(2030, 1, 1), "001099012345", "bam", HomNay, T)).Code);
        }

        [Fact]
        public void Da_xac_minh_thi_khoa_tu_choi_bat_buoc_ly_do()
        {
            XacMinhDanhTinh x = DuDieuKien(new DateOnly(2000, 1, 1));
            x.Nop(T);
            Assert.Equal("thieu_ly_do", Assert.Throws<InvalidValueException>(() => x.TuChoi(Guid.NewGuid(), " ", T)).Code);
            x.Duyet(Guid.NewGuid(), T);
            Assert.Equal("da_xac_minh", Assert.Throws<RuleViolationException>(() => x.GanAnh(true, "k", T)).Code);
        }
    }
}
