using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Services;

namespace Crowd.Identity.Tests
{
    public sealed class CapDoTests
    {
        private static readonly IReadOnlyList<DieuKienCap> Luat = CapDo.DocLuat(CapDo.LuatMacDinh)!;
        private static readonly string[] HuyHieuMongDoi = new string[] { "moc_1000", "uy_tin_cao" };

        [Fact]
        public void Doc_luat_mac_dinh()
        {
            Assert.Equal(4, Luat.Count);
            Assert.Equal(2, Luat[0].Cap);
            Assert.Equal(50, Luat[0].SoNhanToiThieu);
            Assert.Equal(50, Luat[0].UyTinToiThieu);
        }

        [Theory]
        [InlineData("")]
        [InlineData("2=50")]
        [InlineData("2=a/50")]
        [InlineData("1=0/0")]
        [InlineData("2=10/101")]
        public void Luat_sai_dinh_dang_tra_null(string luat)
        {
            Assert.Null(CapDo.DocLuat(luat));
        }

        [Theory]
        [InlineData(0, 90, 1)]
        [InlineData(49, 90, 1)]
        [InlineData(50, 50, 2)]
        [InlineData(5000, 70, 3)]   // du nhan cap 5 nhung uy tin chi du cap 3
        [InlineData(5000, 85, 5)]
        public void Cap_can_ca_so_nhan_va_uy_tin(int soNhan, int uyTin, int capMongDoi)
        {
            Assert.Equal(capMongDoi, CapDo.TinhCap(Luat, soNhan, uyTin));
        }

        [Fact]
        public void Chua_co_uy_tin_thi_cap_1()
        {
            Assert.Equal(1, CapDo.TinhCap(Luat, 100000, null));
        }

        [Fact]
        public void Huy_hieu_moi_chi_tra_cai_chua_co()
        {
            HashSet<string> daCo = new HashSet<string> { "moc_100" };

            List<HuyHieu> moi = CapDo.HuyHieuMoi(1000, 95, daCo);

            Assert.Equal(HuyHieuMongDoi, moi.ConvertAll(h => h.Ma));
        }

        [Fact]
        public void Uy_tin_cao_can_du_100_nhan()
        {
            Assert.Empty(CapDo.HuyHieuMoi(99, 99, new HashSet<string>()));
        }
    }

    public sealed class TaiKhoanEntityTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        private static readonly string[] LinhVucNhap = new string[] { "Y tế", "y-te", " Giao thông " };
        private static readonly string[] NgonNguNhap = new string[] { "vi" };
        private static readonly string[] LoaiNhap = new string[] { "IMAGE", "text" };
        private static readonly string[] LoaiSai = new string[] { "3d" };
        private static readonly string[] LinhVucMongDoi = new string[] { "y-te", "giao-thong" };
        private static readonly string[] LoaiMongDoi = new string[] { "image", "text" };

        [Fact]
        public void Xac_thuc_email_mot_lan()
        {
            User u = User.Tao("a@b.com", "A", new List<string> { "labeler" }, T);

            Assert.False(u.DaXacThucEmail);
            Assert.True(u.XacThucEmail(T));
            Assert.False(u.XacThucEmail(T.AddHours(1)));
            Assert.Equal(T, u.EmailVerifiedAt);
        }

        [Fact]
        public void Ky_nang_bo_trung_va_kiem_loai_du_lieu()
        {
            User u = User.Tao("a@b.com", "A", new List<string> { "labeler" }, T);

            u.CapNhatKyNang(LinhVucNhap, NgonNguNhap, LoaiNhap);

            Assert.Equal(LinhVucMongDoi, u.Domains);
            Assert.Equal(LoaiMongDoi, u.Modalities);

            // Gop the "giao-thong" vao "y-te": bo trung.
            Assert.True(u.DoiMaThe("domain", "giao-thong", "y-te"));
            Assert.Equal("y-te", Assert.Single(u.Domains));
            Assert.False(u.DoiMaThe("language", "en", "vi"));
            Assert.Equal("loai_du_lieu_khong_hop_le",
                Assert.Throws<InvalidValueException>(() => u.CapNhatKyNang(null!, null!, LoaiSai)).Code);
        }

        [Fact]
        public void Ho_so_kiem_ten_va_gioi_thieu()
        {
            User u = User.Tao("a@b.com", "A", new List<string> { "labeler" }, T);

            u.CapNhatHoSo("  Nguyen Van B ", "  ");
            Assert.Equal("Nguyen Van B", u.DisplayName);
            Assert.Null(u.Bio);
            Assert.Equal("ten_khong_hop_le", Assert.Throws<InvalidValueException>(() => u.CapNhatHoSo(" ", null)).Code);
            Assert.Equal("gioi_thieu_qua_dai", Assert.Throws<InvalidValueException>(() => u.CapNhatHoSo("B", new string('x', 501))).Code);
        }

        [Fact]
        public void Ma_mot_lan_het_han_hoac_da_dung_thi_khong_dung_duoc()
        {
            MaTaiKhoan m = MaTaiKhoan.Tao(Guid.NewGuid(), MucDichMa.ResetPassword, "hash", T, TimeSpan.FromMinutes(30));

            Assert.True(m.DungDuoc(T.AddMinutes(29)));
            Assert.False(m.DungDuoc(T.AddMinutes(30)));

            m.Dung(T.AddMinutes(1));
            Assert.False(m.DungDuoc(T.AddMinutes(2)));
        }

        [Fact]
        public void Uy_tin_cu_den_tre_bi_bo_qua()
        {
            ThongKeLabeler tk = ThongKeLabeler.Tao(Guid.NewGuid(), T);

            Assert.True(tk.DatUyTin(80, T.AddMinutes(5)));
            Assert.False(tk.DatUyTin(40, T.AddMinutes(1)));
            Assert.Equal(80, tk.Reputation);
        }

        [Fact]
        public void So_nhan_khong_am()
        {
            ThongKeLabeler tk = ThongKeLabeler.Tao(Guid.NewGuid(), T);
            tk.CongNhan(-1, T);

            Assert.Equal(0, tk.ApprovedCount);
        }

        [Fact]
        public void Moc_thu_hoi_lam_tron_giay_va_chi_tang()
        {
            DateTimeOffset coMiliGiay = T.AddMilliseconds(750);
            Assert.Equal(T, PhienService.LamTronGiay(coMiliGiay));

            MocThuHoiPhien moc = MocThuHoiPhien.Tao(Guid.NewGuid(), T.AddMinutes(5));
            moc.DoiMoc(T);
            Assert.Equal(T.AddMinutes(5), moc.RevokedAt);
        }
    }
}
