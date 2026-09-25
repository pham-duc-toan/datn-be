using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Identity;

namespace Crowd.Identity.Tests
{
    public sealed class MatKhauServiceTests
    {
        private static User TaoUser()
        {
            return User.Tao("an@example.com", "An", new List<string> { "labeler" }, DateTimeOffset.UtcNow);
        }

        [Fact]
        public void Cung_mat_khau_bam_hai_lan_ra_hai_ket_qua_khac_nhau()
        {
            // Co muoi ngau nhien: hai nguoi dung chung mat khau "123456789" khong
            // de lai dau vet giong nhau trong database.
            MatKhauService dichVu = new MatKhauService(new PasswordHasher<User>());
            User user = TaoUser();

            Assert.NotEqual(dichVu.Bam(user, "matkhau123"), dichVu.Bam(user, "matkhau123"));
        }

        [Fact]
        public void Kiem_dung_mat_khau_dung_va_tu_choi_mat_khau_sai()
        {
            MatKhauService dichVu = new MatKhauService(new PasswordHasher<User>());
            User user = TaoUser();
            user.DatPasswordHash(dichVu.Bam(user, "matkhau123"));

            Assert.Equal(PasswordVerificationResult.Success, dichVu.Kiem(user, "matkhau123"));
            Assert.Equal(PasswordVerificationResult.Failed, dichVu.Kiem(user, "matkhau124"));
        }

        [Fact]
        public void Admin_khong_nam_trong_vai_tro_tu_dang_ky_duoc()
        {
            // Chan tu cap quyen admin qua form dang ky.
            Assert.DoesNotContain(CrowdRoles.Admin, CrowdRoles.TuDangKyDuoc);
            Assert.Contains(CrowdRoles.Labeler, CrowdRoles.TuDangKyDuoc);
        }

        [Theory]
        [InlineData("An@Example.COM", "an@example.com")]
        [InlineData("  an@example.com  ", "an@example.com")]
        public void Email_duoc_chuan_hoa_de_khong_thanh_hai_tai_khoan(string vao, string ra)
        {
            Assert.Equal(ra, User.ChuanHoaEmail(vao));
        }
    }
}
