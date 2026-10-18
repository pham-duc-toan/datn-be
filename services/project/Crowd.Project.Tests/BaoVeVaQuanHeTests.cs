using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Tests
{
    public sealed class BaoVeVaQuanHeTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Owner = Guid.NewGuid();

        private static LabelingProject DuAn()
        {
            return LabelingProject.Tao(Owner, "Kiem duyet", null, Modalities.Image, ProjectVisibility.Public, Luc, QuyDinhMau.DuAn);
        }

        [Fact]
        public void Thuong_duoi_toi_thieu_bi_tu_choi_va_admin_nang_muc_thi_chan_publish()
        {
            QuyDinhDuAn q = new QuyDinhDuAn
            {
                DoDaiTenToiDa = 200,
                DoDaiMoTaToiDa = 5000,
                NguongDatToiThieu = 60,
                NguongDatToiDa = 95,
                SoCauToiDa = 500,
                SoSuatToiThieu = 3,
                SoSuatToiDa = 50,
                SoCauTestMacDinh = 5,
                NguongDauMacDinh = 80,
                SoCauTestToiDa = 50,
                DoDaiHuongDanToiDa = 20000,
                SoViDuToiDa = 20,
                DoDaiGiaiThichToiDa = 500,
                ThuongToiThieu = 50000,
            };
            LabelingProject p = DuAn();
            Assert.Equal("thuong_duoi_toi_thieu", Assert.Throws<InvalidValueException>(
                () => p.DatCauHinhGia(49999, 3, 80, Luc.AddDays(5), Luc, q)).Code);
            p.DatCauHinhGia(50000, 3, 80, Luc.AddDays(5), Luc, q);

            QuyDinhDuAn nangMuc = new QuyDinhDuAn
            {
                DoDaiTenToiDa = 200,
                DoDaiMoTaToiDa = 5000,
                NguongDatToiThieu = 60,
                NguongDatToiDa = 95,
                SoCauToiDa = 500,
                SoSuatToiThieu = 3,
                SoSuatToiDa = 50,
                SoCauTestMacDinh = 5,
                NguongDauMacDinh = 80,
                SoCauTestToiDa = 50,
                DoDaiHuongDanToiDa = 20000,
                SoViDuToiDa = 20,
                DoDaiGiaiThichToiDa = 500,
                ThuongToiThieu = 100000,
            };
            Assert.Contains("thuong_duoi_toi_thieu", p.NhungGiConThieu(0, 0, false, nangMuc, null, Luc));
        }

        [Fact]
        public void Nhay_cam_bat_buoc_canh_bao_va_NDA_bat_buoc_van_ban()
        {
            LabelingProject p = DuAn();
            Assert.Equal("thieu_canh_bao", Assert.Throws<InvalidValueException>(() => p.DatBaoVe(true, " ", false, null, false, Luc)).Code);
            Assert.Equal("thieu_van_ban_nda", Assert.Throws<InvalidValueException>(() => p.DatBaoVe(false, null, true, "", false, Luc)).Code);

            p.DatBaoVe(true, "Co hinh anh bao luc", true, "Khong tiet lo du lieu.", false, Luc);
            Assert.True(p.CanDongYCaNhan());
            Assert.Equal(1, p.NdaVersion);

            p.DatBaoVe(true, "Co hinh anh bao luc", true, "Khong tiet lo du lieu.", false, Luc);
            Assert.Equal(1, p.NdaVersion);
            p.DatBaoVe(true, "Co hinh anh bao luc", true, "Ban moi.", false, Luc);
            Assert.Equal(2, p.NdaVersion);
        }

        [Fact]
        public void Thu_tu_dieu_kien_bao_ve()
        {
            LabelingProject p = DuAn();
            p.DatBaoVe(true, "canh bao", false, null, false, Luc);

            Assert.Equal("can_xac_minh_danh_tinh", p.LyDoKhongDuDieuKienBaoVe(false, false, false));
            Assert.Equal("chua_du_tuoi", p.LyDoKhongDuDieuKienBaoVe(true, false, false));
            Assert.Equal("can_dong_y_dieu_khoan", p.LyDoKhongDuDieuKienBaoVe(true, true, false));
            Assert.Null(p.LyDoKhongDuDieuKienBaoVe(true, true, true));

            LabelingProject chiXacMinh = DuAn();
            chiXacMinh.DatBaoVe(false, null, false, null, true, Luc);
            Assert.False(chiXacMinh.CanDongYCaNhan());
            Assert.Equal("can_xac_minh_danh_tinh", chiXacMinh.LyDoKhongDuDieuKienBaoVe(false, false, false));
            Assert.Null(chiXacMinh.LyDoKhongDuDieuKienBaoVe(true, false, false));
        }

        [Fact]
        public void Dong_y_gan_voi_phien_ban_va_noi_dung_NDA()
        {
            LabelingProject p = DuAn();
            p.DatBaoVe(true, "canh bao", true, "Dieu khoan v1", false, Luc);
            ProjectConsent c = ProjectConsent.Tao(p.Id, Guid.NewGuid(), Luc);

            Assert.Equal("chua_dong_y_canh_bao", Assert.Throws<InvalidValueException>(() => c.DongY(p, false, true, "Nguyen Van A", null, null, Luc)).Code);
            Assert.Equal("thieu_ho_ten_ky", Assert.Throws<InvalidValueException>(() => c.DongY(p, true, true, " ", null, null, Luc)).Code);

            c.DongY(p, true, true, "Nguyen Van A", "1.2.3.4", "Firefox", Luc);
            Assert.True(c.ConHieuLuc(p));
            Assert.Equal(ProjectConsent.BamVanBan("Dieu khoan v1"), c.NdaTextHash);

            p.DatBaoVe(true, "canh bao", true, "Dieu khoan v2", false, Luc);
            Assert.False(c.ConHieuLuc(p));
        }

        [Fact]
        public void Loi_moi_vong_doi()
        {
            ProjectInvitation i = ProjectInvitation.Tao(Guid.NewGuid(), "a@b.c", null, MemberRole.Labeler, Owner, true, Luc, TimeSpan.FromDays(14));
            Assert.True(i.ConHieuLuc(Luc.AddDays(13)));
            Assert.False(i.ConHieuLuc(Luc.AddDays(15)));
            Assert.Equal("loi_moi_het_han", Assert.Throws<RuleViolationException>(() => i.ChapNhan(Luc.AddDays(15))).Code);

            Guid u = Guid.NewGuid();
            i.GanTaiKhoan(u);
            Assert.Equal("loi_moi_cua_nguoi_khac", Assert.Throws<RuleViolationException>(() => i.GanTaiKhoan(Guid.NewGuid())).Code);
            i.ChapNhan(Luc.AddDays(1));
            Assert.Equal(InvitationStatus.Accepted, i.Status);
            Assert.Equal("loi_moi_da_xu_ly", Assert.Throws<RuleViolationException>(() => i.TuChoi(Luc.AddDays(1))).Code);
        }

        [Fact]
        public void Chu_du_an_khong_thay_userId_cua_loi_moi_email_toi_khi_chap_nhan()
        {
            LabelingProject p = DuAn();
            Guid u = Guid.NewGuid();
            ProjectInvitation i = ProjectInvitation.Tao(p.Id, "a@b.c", u, MemberRole.Labeler, Owner, true, Luc, TimeSpan.FromDays(14));

            InvitationResponse chu = MemberService.TaoLoiMoiResponse(i, p, true);
            Assert.Null(chu.UserId);
            Assert.Equal("a@b.c", chu.Email);
            Assert.True(chu.ByEmail);

            InvitationResponse nguoiDuocMoi = MemberService.TaoLoiMoiResponse(i, p, false);
            Assert.Equal(u, nguoiDuocMoi.UserId);

            i.ChapNhan(Luc.AddDays(1));
            Assert.Equal(u, MemberService.TaoLoiMoiResponse(i, p, true).UserId);
        }

        [Fact]
        public void Chu_du_an_khong_thay_email_cua_loi_moi_theo_userId()
        {
            LabelingProject p = DuAn();
            Guid u = Guid.NewGuid();
            ProjectInvitation i = ProjectInvitation.Tao(p.Id, "a@b.c", u, MemberRole.Labeler, Owner, false, Luc, TimeSpan.FromDays(14));

            InvitationResponse chu = MemberService.TaoLoiMoiResponse(i, p, true);
            Assert.Null(chu.Email);
            Assert.Equal(u, chu.UserId);
            Assert.Equal("a@b.c", MemberService.TaoLoiMoiResponse(i, p, false).Email);
        }

        [Fact]
        public void Chan_theo_danh_sach_chi_mo_dong_bi_chan_vi_danh_sach()
        {
            ProjectMember a = ProjectMember.TaoThanhVien(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Labeler, Luc);
            Assert.True(a.ChanTheoDanhSach(Luc));
            Assert.True(a.BoChanTheoDanhSach(Luc));
            Assert.Equal(MemberState.Active, a.State);

            ProjectMember b = ProjectMember.TaoThanhVien(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Labeler, Luc);
            b.Chan(Luc);
            Assert.False(b.ChanTheoDanhSach(Luc));
            Assert.False(b.BoChanTheoDanhSach(Luc));
            Assert.Equal(MemberState.Blocked, b.State);
            Assert.Equal("bi_chan", Assert.Throws<RuleViolationException>(() => b.KiemTraTuRoiDuoc()).Code);
        }

        [Fact]
        public void Danh_gia_tu_1_den_5_sao()
        {
            Assert.Equal("so_sao_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => LabelerRating.Tao(Guid.NewGuid(), Guid.NewGuid(), Owner, 6, null, Luc)).Code);
            LabelerRating r = LabelerRating.Tao(Guid.NewGuid(), Guid.NewGuid(), Owner, 4, " tot ", Luc);
            Assert.Equal("tot", r.Comment);
            Assert.Equal("khong_tu_them", Assert.Throws<InvalidValueException>(
                () => OwnerLabelerEntry.Tao(Owner, Owner, OwnerListKind.Blocked, null, Luc)).Code);
        }

        [Fact]
        public void Don_xin_het_han()
        {
            JoinRequest d = JoinRequest.Tao(Guid.NewGuid(), Guid.NewGuid(), null, null, null, Luc);
            d.HetHan(Luc);
            Assert.Equal(JoinRequestStatus.Expired, d.Status);
            Assert.Null(d.DuocXinLaiTu(TimeSpan.FromDays(1)));
        }

        [Fact]
        public void Nhan_goi_y_chuan_hoa_bo_trung()
        {
            LabelingProject p = DuAn();
            p.DatNhan(new List<string> { "Y te", "y te", " giao thong " }, new List<string> { "vi" }, Luc);
            Assert.Equal(2, p.Domains.Count);
            Assert.Equal("Y te", p.Domains[0]);
            Assert.Equal("giao thong", p.Domains[1]);
            Assert.Equal("vi", Assert.Single(p.Languages));
        }
            [Fact]
        public void Cong_link_chi_cho_cong_cu_lam_nhanh()
        {
            HashSet<string> choPhep = new HashSet<string> { "classification", "bbox" };
            LabelingProject p = DuAn();
            p.DatLabelSchema(LabelSchema.Doc("{\"modality\":\"image\",\"tools\":[{\"name\":\"lop\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]},"
                + "{\"name\":\"vung\",\"kind\":\"mask\",\"classes\":[\"x\"]}]}"), Luc);

            // Chua bat cong link: khong kiem.
            p.DatKenhPhanPhoi(true, false, false, null, null, Luc);
            Assert.Empty(p.CongCuKhongHopCongLink(choPhep));
            Assert.DoesNotContain("cong_cu_khong_hop_cong_link", p.NhungGiConThieu(0, 0, false, QuyDinhMau.DuAn, choPhep, Luc));

            p.DatKenhPhanPhoi(true, true, false, 200, 10000, Luc);
            Assert.Equal("vung", Assert.Single(p.CongCuKhongHopCongLink(choPhep)));
            Assert.Contains("cong_cu_khong_hop_cong_link", p.NhungGiConThieu(0, 0, false, QuyDinhMau.DuAn, choPhep, Luc));
            // Khong truyen danh sach → khong kiem.
            Assert.DoesNotContain("cong_cu_khong_hop_cong_link", p.NhungGiConThieu(0, 0, false, QuyDinhMau.DuAn, null, Luc));
        }
}
}
