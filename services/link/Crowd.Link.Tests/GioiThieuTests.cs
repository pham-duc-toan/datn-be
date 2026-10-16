using Crowd.Link.Api.Services;
using Xunit;

namespace Crowd.Link.Tests
{
    public sealed class GioiThieuTests
    {
        [Fact]
        public void Binh_thuong_khong_co_dau_hieu()
        {
            Assert.Null(ReferralService.DauHieuLamDung(false, 0, 0, 2, 10));
            Assert.Null(ReferralService.DauHieuLamDung(false, 1, 9, 2, 10));
        }

        [Fact]
        public void Tu_moi_chinh_minh_cung_ip()
        {
            Assert.Equal("trung_ip_nguoi_gioi_thieu", ReferralService.DauHieuLamDung(true, 0, 0, 2, 10));
        }

        [Fact]
        public void Nhieu_tai_khoan_mot_ip_va_dot_bien()
        {
            Assert.Equal("nhieu_tai_khoan_cung_ip", ReferralService.DauHieuLamDung(false, 2, 2, 2, 10));
            Assert.Equal("dot_bien", ReferralService.DauHieuLamDung(false, 0, 10, 2, 10));
            Assert.Equal("trung_ip_nguoi_gioi_thieu,nhieu_tai_khoan_cung_ip,dot_bien", ReferralService.DauHieuLamDung(true, 5, 50, 2, 10));
        }
    }
}
