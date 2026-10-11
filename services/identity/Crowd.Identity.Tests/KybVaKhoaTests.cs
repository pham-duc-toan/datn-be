using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;

namespace Crowd.Identity.Tests
{
    public sealed class BusinessProfileTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

        private static BusinessProfile DuDieuKien()
        {
            BusinessProfile p = BusinessProfile.Tao(Guid.NewGuid(), T);
            p.CapNhat("Cong ty Test", "0123456789", "Ha Noi", T);
            p.GanGiayPhep("kyb/x/y.pdf", "giay-phep.pdf", T);
            return p;
        }

        [Fact]
        public void Nop_duyet_thanh_verified_va_khoa_khong_sua_duoc()
        {
            BusinessProfile p = DuDieuKien();
            p.Nop(T);
            Assert.Equal(KybStatus.Submitted, p.Status);

            p.Duyet(Guid.NewGuid(), T);
            Assert.Equal(KybStatus.Verified, p.Status);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.CapNhat("Khac", "0123456789", "HCM", T));
            Assert.Equal("da_xac_minh", ex.Code);
        }

        [Fact]
        public void Dang_cho_duyet_thi_khong_sua_khong_nop_lai()
        {
            BusinessProfile p = DuDieuKien();
            p.Nop(T);

            Assert.Equal("ho_so_dang_cho_duyet", Assert.Throws<RuleViolationException>(() => p.GanGiayPhep("k", "f.pdf", T)).Code);
            Assert.Equal("ho_so_dang_cho_duyet", Assert.Throws<RuleViolationException>(() => p.Nop(T)).Code);
        }

        [Fact]
        public void Tu_choi_bat_buoc_ly_do_roi_sua_va_nop_lai_duoc()
        {
            BusinessProfile p = DuDieuKien();
            p.Nop(T);

            Assert.Equal("thieu_ly_do", Assert.Throws<InvalidValueException>(() => p.TuChoi(Guid.NewGuid(), " ", T)).Code);
            p.TuChoi(Guid.NewGuid(), "Giay phep mo", T);
            Assert.Equal(KybStatus.Rejected, p.Status);
            Assert.Equal("Giay phep mo", p.RejectReason);

            p.GanGiayPhep("kyb/x/z.pdf", "ro-hon.pdf", T);
            p.Nop(T);
            Assert.Equal(KybStatus.Submitted, p.Status);
            Assert.Null(p.RejectReason);
        }

        [Fact]
        public void Nop_thieu_thong_tin_hoac_giay_phep_bi_chan()
        {
            BusinessProfile chuaKhai = BusinessProfile.Tao(Guid.NewGuid(), T);
            Assert.Equal("ho_so_chua_du", Assert.Throws<RuleViolationException>(() => chuaKhai.Nop(T)).Code);

            BusinessProfile chuaGiayPhep = BusinessProfile.Tao(Guid.NewGuid(), T);
            chuaGiayPhep.CapNhat("Cong ty", "0123456789", "Ha Noi", T);
            Assert.Equal("thieu_giay_phep", Assert.Throws<RuleViolationException>(() => chuaGiayPhep.Nop(T)).Code);
        }

        [Theory]
        [InlineData("012345678")]
        [InlineData("01234567890")]
        [InlineData("0123456789-01")]
        [InlineData("abcdefghij")]
        public void Ma_so_thue_sai_dinh_dang_bi_tu_choi(string mst)
        {
            BusinessProfile p = BusinessProfile.Tao(Guid.NewGuid(), T);
            Assert.Equal("ma_so_thue_khong_hop_le", Assert.Throws<InvalidValueException>(() => p.CapNhat("Cong ty", mst, "Ha Noi", T)).Code);
        }

        [Fact]
        public void Ma_so_thue_chi_nhanh_hop_le()
        {
            BusinessProfile p = BusinessProfile.Tao(Guid.NewGuid(), T);
            p.CapNhat("Cong ty", "0123456789-001", "Ha Noi", T);
            Assert.Equal("0123456789-001", p.TaxCode);
        }

        [Fact]
        public void Chi_duyet_tu_choi_ho_so_dang_cho()
        {
            BusinessProfile p = DuDieuKien();
            Assert.Equal("khong_cho_duyet", Assert.Throws<RuleViolationException>(() => p.Duyet(Guid.NewGuid(), T)).Code);
        }
    }

    public sealed class KhoaTaiKhoanTests
    {
        [Fact]
        public void Khoa_mo_khoa_idempotent()
        {
            User u = User.Tao("a@b.com", "A", new List<string> { "labeler" }, DateTimeOffset.UtcNow);

            Assert.True(u.Khoa("Spam", DateTimeOffset.UtcNow));
            Assert.Equal(UserStatus.Blocked, u.Status);
            Assert.Equal("Spam", u.BlockedReason);
            Assert.False(u.Khoa("Lan hai", DateTimeOffset.UtcNow));

            Assert.True(u.MoKhoa());
            Assert.Equal(UserStatus.Active, u.Status);
            Assert.Null(u.BlockedReason);
            Assert.False(u.MoKhoa());
        }
    }
}
