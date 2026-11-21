using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Helpers;
using Crowd.Settings;
using Xunit;

namespace Crowd.Identity.Tests
{
    /// <summary>So thich chon MOT LAN luc dang ky (kien truc 3.38): chi labeler, toi da 5 linh vuc / 3 ngon ngu.</summary>
    public sealed class SoThichDangKyTests
    {
        private static readonly string[] Labeler = new string[] { "labeler" };
        private static readonly string[] DoanhNghiep = new string[] { "business" };

        private static RegisterRequest DangKy(string[] vaiTro, SkillsDto? soThich)
        {
            return new RegisterRequest
            {
                Email = "lan@vidu.vn",
                Username = "lan_nguyen",
                Password = "matkhau123",
                DisplayName = "Lan",
                Roles = vaiTro,
                Skills = soThich,
            };
        }

        private static Dictionary<string, string[]> Kiem(RegisterRequest body)
        {
            return AuthRequestValidator.KiemDangKy(body, new SettingsStore());
        }

        [Fact]
        public void Labeler_chon_so_thich_trong_gioi_han_thi_hop_le()
        {
            SkillsDto soThich = new SkillsDto
            {
                Domains = new string[] { "y-te", "dong-vat" },
                Languages = new string[] { "vi", "en", "ja" },
                Modalities = new string[] { "image", "text" },
            };

            Assert.Empty(Kiem(DangKy(Labeler, soThich)));
            Assert.Empty(Kiem(DangKy(Labeler, null)));
        }

        [Fact]
        public void Vuot_gioi_han_linh_vuc_hoac_ngon_ngu_bi_tu_choi()
        {
            SkillsDto nhieuLinhVuc = new SkillsDto { Domains = new string[] { "a", "b", "c", "d", "e", "f" } };
            Assert.True(Kiem(DangKy(Labeler, nhieuLinhVuc)).ContainsKey("skills.domains"));

            SkillsDto nhieuNgonNgu = new SkillsDto { Languages = new string[] { "vi", "en", "ja", "ko" } };
            Assert.True(Kiem(DangKy(Labeler, nhieuNgonNgu)).ContainsKey("skills.languages"));

            Assert.Equal(5, KyNang.LinhVucDangKyToiDa);
            Assert.Equal(3, KyNang.NgonNguDangKyToiDa);
        }

        [Fact]
        public void Chi_nguoi_gan_nhan_moi_chon_so_thich()
        {
            SkillsDto soThich = new SkillsDto { Domains = new string[] { "y-te" } };
            Assert.True(Kiem(DangKy(DoanhNghiep, soThich)).ContainsKey("skills"));

            // Doanh nghiep gui so thich rong thi khong sao (form gui object rong).
            Assert.Empty(Kiem(DangKy(DoanhNghiep, new SkillsDto())));
        }
    }
}
