using System;
using System.Collections.Generic;
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

        private static LabelingProject DuAnMoi()
        {
            return LabelingProject.Tao(Owner, "Phan loai cho meo", "mo ta", TaskType.ImageClassification, ProjectVisibility.Public, Luc);
        }

        /// <summary>Du an cau hinh day du, san sang publish voi 100 mau.</summary>
        private static LabelingProject DuAnSanSang()
        {
            LabelingProject p = DuAnMoi();
            p.DatLabelSchema(LabelSchema.TaoPhanLoai(new List<string> { "cho", "meo" }, false), Luc);
            p.DatHuongDan(Guideline.Tao("# Huong dan", null), Luc);
            p.DatCauHinhGia(1000, 3, 300000, Luc.AddDays(30), Luc);
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

        [Fact]
        public void Chi_ho_tro_phan_loai_anh()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                LabelingProject.Tao(Owner, "x", null, TaskType.BoundingBox, ProjectVisibility.Public, Luc));

            Assert.Equal("loai_bai_toan_chua_ho_tro", ex.Code);
        }

        [Fact]
        public void Checklist_liet_ke_dung_nhung_gi_con_thieu()
        {
            IReadOnlyList<string> thieu = DuAnMoi().NhungGiConThieu(0, 0, Luc);

            Assert.Contains("chua_co_tap_nhan", thieu);
            Assert.Contains("chua_co_huong_dan", thieu);
            Assert.Contains("chua_cau_hinh_gia", thieu);
            Assert.Contains("chua_co_du_lieu", thieu);
        }

        [Fact]
        public void Ngan_sach_phai_du_cho_so_mau_nhan_redundancy_nhan_don_gia()
        {
            LabelingProject p = DuAnSanSang();

            // 100 mau x 3 nguoi x 1000d = 300.000d = dung bang ngan sach → du.
            Assert.Empty(p.NhungGiConThieu(100, 0, Luc));

            // 101 mau → 303.000d > 300.000d.
            Assert.Contains("ngan_sach_khong_du", p.NhungGiConThieu(101, 0, Luc));
        }

        [Fact]
        public void Yeu_cau_test_thi_phai_du_cau_hoi_vang_cho_test()
        {
            LabelingProject p = DuAnSanSang();
            p.DatDieuKienThamGia(null, null, true, 10, 80, Luc);

            Assert.Contains("thieu_cau_hoi_vang_cho_test", p.NhungGiConThieu(100, 9, Luc));
            Assert.Empty(p.NhungGiConThieu(100, 10, Luc));
        }

        [Fact]
        public void Duong_di_hanh_phuc_cua_saga_Nhap_toi_Dang_chay()
        {
            LabelingProject p = DuAnSanSang();

            p.YeuCauPublish(100, 0, Luc);
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
            p.YeuCauPublish(100, 0, Luc);

            p.KyQuyBiTuChoi("So du khong du", Luc);

            Assert.Equal(ProjectStatus.Draft, p.Status);
            Assert.Equal("So du khong du", p.StatusReason);
            Assert.False(p.WasEscrowed);
        }

        [Fact]
        public void Khong_the_publish_hai_lan()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.YeuCauPublish(100, 0, Luc));
            Assert.Equal("chuyen_trang_thai_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Khong_sua_duoc_cau_hinh_khi_da_roi_Nhap()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Luc);

            Assert.Throws<RuleViolationException>(() => p.DatCauHinhGia(1, 1, 1, Luc.AddDays(1), Luc));
            Assert.Throws<RuleViolationException>(() => p.KiemTraCoTheNapDuLieu());
        }

        [Fact]
        public void Khong_cho_huy_khi_dang_cho_ky_quy()
        {
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Luc);

            Assert.Throws<RuleViolationException>(() => p.Huy("doi y", Luc));
        }

        [Fact]
        public void Qua_72_gio_cho_duyet_thi_tu_huy()
        {
            TimeSpan han = TimeSpan.FromHours(72);
            LabelingProject p = DuAnSanSang();
            p.YeuCauPublish(100, 0, Luc);
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
            p.YeuCauPublish(100, 0, Luc);
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
            LabelingProject p = LabelingProject.Tao(Owner, "rieng", null, TaskType.ImageClassification, ProjectVisibility.Private, Luc);
            p.DatLabelSchema(LabelSchema.TaoPhanLoai(new List<string> { "a", "b" }, false), Luc);
            p.DatHuongDan(Guideline.Tao("x", null), Luc);
            p.DatCauHinhGia(1000, 1, 1000, Luc.AddDays(1), Luc);
            p.YeuCauPublish(1, 0, Luc);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.KiemTraChoTuThamGia());
            Assert.Equal("du_an_rieng_tu", ex.Code);
        }

        [Fact]
        public void Chi_phi_tran_so_thi_nem_loi_thay_vi_thanh_so_am()
        {
            LabelingProject p = DuAnMoi();
            p.DatCauHinhGia(long.MaxValue / 2, 3, 1, Luc.AddDays(1), Luc);

            Assert.Throws<OverflowException>(() => p.ChiPhiUocTinhVnd(10));
        }

        [Fact]
        public void Vi_du_trong_huong_dan_phai_dung_lop_co_trong_tap_nhan()
        {
            LabelingProject p = DuAnSanSang();
            Guideline sai = Guideline.Tao("x", new List<GuidelineExample> { new GuidelineExample(null, "voi", true, "giai thich") });

            Assert.Throws<InvalidValueException>(() => p.DatHuongDan(sai, Luc));
        }
    }
}
