using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// May trang thai du an — test THUAN, khong database, khong HTTP. Day chinh
    /// la ly do project-svc tach Domain thanh project rieng.
    /// </summary>
    public sealed class LabelingProjectTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Owner = Guid.CreateVersion7();

        /// <summary>Phi nen tang 30% nhu vi du trong VD-M-15.</summary>
        private const int Phi = 30;

        private static LabelingProject DuAnMoi()
        {
            return LabelingProject.Tao(Owner, "Phan loai cho meo", "mo ta", Modalities.Image, ProjectVisibility.Public, Luc, QuyDinhMau.DuAn);
        }

        /// <summary>Du an cau hinh day du, san sang publish voi 100 mau.</summary>
        private static LabelingProject DuAnSanSang()
        {
            LabelingProject p = DuAnMoi();
            p.DatLabelSchema(PhanLoaiAnh("cho", "meo"), Luc);
            p.DatHuongDan(Guideline.Tao("# Huong dan", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(1000, 3, 390000, Luc.AddDays(30), Luc, QuyDinhMau.DuAn);
            return p;
        }

        [Fact]
        public void Du_an_moi_o_trang_thai_Nhap_voi_mac_dinh_hop_ly()
        {
            LabelingProject p = DuAnMoi();

            Assert.Equal(ProjectStatus.Draft, p.Status);
            Assert.True(p.AllowProfessional);
            Assert.False(p.WasEscrowed);
        }

        /// <summary>Tap nhan anh mot cong cu phan loai "label".</summary>
        internal static LabelSchema PhanLoaiAnh(string a, string b)
        {
            return LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\",\"classes\":[\""
                + a + "\",\"" + b + "\"]}]}");
        }

        [Fact]
        public void Loai_du_lieu_phai_hop_le()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                LabelingProject.Tao(Owner, "x", null, "hologram", ProjectVisibility.Public, Luc, QuyDinhMau.DuAn));

            Assert.Equal("loai_du_lieu_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Tap_nhan_phai_cung_loai_du_lieu_voi_du_an()
        {
            LabelingProject p = LabelingProject.Tao(Owner, "van ban", null, Modalities.Text, ProjectVisibility.Public, Luc, QuyDinhMau.DuAn);

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => p.DatLabelSchema(PhanLoaiAnh("a", "b"), Luc));
            Assert.Equal("loai_du_lieu_khong_khop", ex.Code);

            // Cung loai thi nhan, ke ca nhieu cong cu.
            p.DatLabelSchema(
                LabelSchema.Doc(
                    "{\"modality\":\"text\",\"tools\":["
                    + "{\"name\":\"cam_xuc\",\"kind\":\"classification\",\"classes\":[\"vui\",\"buon\"]},"
                    + "{\"name\":\"thuc_the\",\"kind\":\"span\",\"classes\":[\"nguoi\",\"noi\"]}]}"),
                Luc);
            Assert.Equal(2, p.LabelSchema!.Tools.Count);
        }

        [Fact]
        public void Checklist_liet_ke_dung_nhung_gi_con_thieu()
        {
            IReadOnlyList<string> thieu = DuAnMoi().NhungGiConThieu(0, 0, Phi, Luc);

            Assert.Contains("chua_co_tap_nhan", thieu);
            Assert.Contains("chua_co_huong_dan", thieu);
            Assert.Contains("chua_cau_hinh_gia", thieu);
            Assert.Contains("chua_co_du_lieu", thieu);
        }

        [Fact]
        public void Ngan_sach_phai_du_cho_so_mau_nhan_redundancy_nhan_don_gia()
        {
            LabelingProject p = DuAnSanSang();

            // 100 mau x 3 nguoi x (1.000 + phi 300) = 390.000d = dung bang ngan sach → du.
            Assert.Empty(p.NhungGiConThieu(100, 0, Phi, Luc));

            // 101 mau → 393.900d > 390.000d.
            Assert.Contains("ngan_sach_khong_du", p.NhungGiConThieu(101, 0, Phi, Luc));
        }

        [Fact]
        public void Yeu_cau_test_thi_phai_du_cau_hoi_vang_cho_test()
        {
            LabelingProject p = DuAnSanSang();
            p.DatDieuKienThamGia(null, null, true, 10, 80, Luc, QuyDinhMau.DuAn);

            Assert.Contains("thieu_cau_hoi_vang_cho_test", p.NhungGiConThieu(100, 9, Phi, Luc));
            Assert.Empty(p.NhungGiConThieu(100, 10, Phi, Luc));
        }

        [Fact]
        public void Duong_di_hanh_phuc_cua_saga_Nhap_toi_Dang_chay()
        {
            LabelingProject p = DuAnSanSang();

            p.YeuCauPublish(100, 0, Phi, Luc);
            Assert.Equal(ProjectStatus.PendingEscrow, p.Status);

            p.XacNhanDaKyQuy(Luc.AddSeconds(2));
            Assert.Equal(ProjectStatus.PendingApproval, p.Status);
            Assert.True(p.WasEscrowed);

            p.Duyet(Luc.AddHours(1));
            Assert.Equal(ProjectStatus.Running, p.Status);
            Assert.Equal(Luc.AddHours(1), p.PublishedAt);
        }

        [Fact]
        public void Ledger_tu_choi_ky_quy_thi_ve_Nhap_kem_ly_do()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);

            p.KyQuyBiTuChoi("So du khong du", Luc);

            Assert.Equal(ProjectStatus.Draft, p.Status);
            Assert.Equal("So du khong du", p.StatusReason);
            Assert.False(p.WasEscrowed);
        }

        [Fact]
        public void Khong_the_publish_hai_lan()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.YeuCauPublish(100, 0, Phi, Luc));
            Assert.Equal("chuyen_trang_thai_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Khong_sua_duoc_cau_hinh_khi_da_roi_Nhap()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);

            Assert.Throws<RuleViolationException>(() => p.DatCauHinhGia(1, 1, 1, Luc.AddDays(1), Luc, QuyDinhMau.DuAn));
            Assert.Throws<RuleViolationException>(() => p.KiemTraCoTheNapDuLieu());
        }

        [Fact]
        public void Khong_cho_huy_khi_dang_cho_ky_quy()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);

            Assert.Throws<RuleViolationException>(() => p.Huy("doi y", Luc));
        }

        [Fact]
        public void Qua_72_gio_cho_duyet_thi_tu_huy()
        {
            TimeSpan han = TimeSpan.FromHours(72);
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);
            p.XacNhanDaKyQuy(Luc);

            Assert.False(p.DaQuaHanChoDuyet(han, Luc.AddHours(71)));
            Assert.Throws<RuleViolationException>(() => p.HuyDoQuaHanChoDuyet(han, Luc.AddHours(71)));

            p.HuyDoQuaHanChoDuyet(han, Luc.AddHours(72));
            Assert.Equal(ProjectStatus.Cancelled, p.Status);
            Assert.True(p.WasEscrowed);
        }

        [Fact]
        public void Tam_dung_tiep_tuc_hoan_thanh()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);

            p.TamDung(Luc);
            Assert.Equal(ProjectStatus.Paused, p.Status);

            p.TiepTuc(Luc);
            Assert.Equal(ProjectStatus.Running, p.Status);

            p.HoanThanh(Luc);
            Assert.Equal(ProjectStatus.Completed, p.Status);
            Assert.True(p.DaKetThuc());

            Assert.Throws<RuleViolationException>(() => p.TamDung(Luc));
        }

        [Fact]
        public void Khong_tu_tham_gia_duoc_du_an_rieng_tu()
        {
            LabelingProject p = LabelingProject.Tao(Owner, "rieng", null, Modalities.Image, ProjectVisibility.Private, Luc, QuyDinhMau.DuAn);
            p.DatLabelSchema(PhanLoaiAnh("a", "b"), Luc);
            p.DatHuongDan(Guideline.Tao("x", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(1000, 1, 1300, Luc.AddDays(1), Luc, QuyDinhMau.DuAn);
            p.YeuCauPublish(1, 0, Phi, Luc);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.KiemTraChoTuThamGia());
            Assert.Equal("du_an_rieng_tu", ex.Code);
        }

        [Fact]
        public void Ky_quy_toi_thieu_tinh_theo_tran_redundancy()
        {
            LabelingProject p = DuAnSanSang();

            // 100 mau x 3 nguoi x (1.000 + 300).
            Assert.Equal(390000, p.ChiPhiUocTinhVnd(100, Phi));

            p.DatCauHinhGia(1000, 3, 5, 650000, Luc.AddDays(30), Luc, QuyDinhMau.DuAn);
            Assert.Equal(5, p.TranRedundancy());
            Assert.Equal(650000, p.ChiPhiUocTinhVnd(100, Phi));
        }

        [Fact]
        public void Tran_redundancy_khong_nho_hon_redundancy_va_khong_qua_10()
        {
            LabelingProject p = DuAnSanSang();

            InvalidValueException nho = Assert.Throws<InvalidValueException>(() =>
                p.DatCauHinhGia(1000, 3, 2, 500000, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));
            Assert.Equal("tran_redundancy_khong_hop_le", nho.Code);

            Assert.Throws<InvalidValueException>(() =>
                p.DatCauHinhGia(1000, 3, 11, 500000, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));
        }

        [Fact]
        public void Ti_le_cau_vang_kiem_tra_mac_dinh_10_va_toi_da_50()
        {
            LabelingProject p = DuAnSanSang();
            Assert.Equal(10, p.GoldCheckPercent);

            p.DatKiemSoatChatLuong(0, Luc, QuyDinhMau.DuAn);
            Assert.Equal(0, p.GoldCheckPercent);

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => p.DatKiemSoatChatLuong(51, Luc, QuyDinhMau.DuAn));
            Assert.Equal("ti_le_cau_vang_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Phi_cong_them_duoc_chot_luc_publish_VD_M_15()
        {
            // 200.000d + 30% = 60.000d phi → ky quy 260.000d moi nhan.
            Assert.Equal(60000, LabelingProject.PhiMoiNhanVnd(200000, 30));

            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Phi, Luc);
            Assert.Equal(Phi, p.PlatformFeePercent);
        }

        [Fact]
        public void Chi_phi_tran_so_thi_nem_loi_thay_vi_thanh_so_am()
        {
            LabelingProject p = DuAnMoi();
            p.DatCauHinhGia(long.MaxValue / 2, 3, 1, Luc.AddDays(1), Luc, QuyDinhMau.DuAn);

            Assert.Throws<OverflowException>(() => p.ChiPhiUocTinhVnd(10, Phi));
        }

        [Fact]
        public void Vi_du_trong_huong_dan_phai_dung_lop_co_trong_tap_nhan()
        {
            LabelingProject p = DuAnSanSang();
            Guideline sai = Guideline.Tao("x", new List<GuidelineExample> { new GuidelineExample(null, "voi", true, "giai thich") }, QuyDinhMau.DuAn);

            Assert.Throws<InvalidValueException>(() => p.DatHuongDan(sai, Luc));
        }
    }
}
