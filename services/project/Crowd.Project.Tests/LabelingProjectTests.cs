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
    ///
    /// Du an lam trung tam (docs 3.18): tien theo CA DU AN (thuong moi nguoi dat x so suat), nguong t
    /// trong [x, y], dong thu bai → Dang cham → Hoan thanh.
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

        /// <summary>Du an cau hinh day du: thuong 100.000d, 5 suat, nguong 80%.</summary>
        private static LabelingProject DuAnSanSang()
        {
            LabelingProject p = DuAnMoi();
            p.DatLabelSchema(PhanLoaiAnh("cho", "meo"), Luc);
            p.DatHuongDan(Guideline.Tao("# Huong dan", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(100000, 5, 80, Luc.AddDays(30), Luc, QuyDinhMau.DuAn);
            return p;
        }

        private static IReadOnlyList<string> Thieu(LabelingProject p, int soCauThat, int soVangTest)
        {
            return p.NhungGiConThieu(soCauThat, soVangTest, false, QuyDinhMau.DuAn, null, Luc);
        }

        private static void Publish(LabelingProject p, int soCauThat)
        {
            p.YeuCauPublish(soCauThat, 0, Phi, false, QuyDinhMau.DuAn, null, Luc);
        }

        private static LabelingProject DuAnDangChay()
        {
            LabelingProject p = DuAnSanSang();
            Publish(p, 100);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);
            return p;
        }

        [Fact]
        public void Du_an_moi_o_trang_thai_Nhap_voi_mac_dinh_hop_ly()
        {
            LabelingProject p = DuAnMoi();

            Assert.Equal(ProjectStatus.Draft, p.Status);
            Assert.True(p.AllowProfessional);
            Assert.False(p.WasEscrowed);

            // Nguong mac dinh 80% nam trong [x, y] = [60, 95].
            Assert.Equal(80, p.PassThresholdPercent);
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
            IReadOnlyList<string> thieu = Thieu(DuAnMoi(), 0, 0);

            Assert.Contains("chua_co_tap_nhan", thieu);
            Assert.Contains("chua_co_huong_dan", thieu);
            Assert.Contains("chua_cau_hinh_gia", thieu);
            Assert.Contains("chua_co_du_lieu", thieu);
        }

        [Fact]
        public void So_cau_toi_da_moi_du_an_vi_moi_labeler_lam_het()
        {
            LabelingProject p = DuAnSanSang();

            // QuyDinhMau: toi da 500 cau.
            Assert.Empty(Thieu(p, 500, 0));
            Assert.Contains("qua_nhieu_cau", Thieu(p, 501, 0));
        }

        [Fact]
        public void Nguong_dat_phai_trong_khoang_x_y_admin_dat()
        {
            LabelingProject p = DuAnMoi();

            InvalidValueException duoi = Assert.Throws<InvalidValueException>(() =>
                p.DatCauHinhGia(100000, 5, 59, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));
            Assert.Equal("nguong_dat_ngoai_khoang", duoi.Code);

            Assert.Throws<InvalidValueException>(() => p.DatCauHinhGia(100000, 5, 96, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));

            p.DatCauHinhGia(100000, 5, 60, Luc.AddDays(30), Luc, QuyDinhMau.DuAn);
            p.DatCauHinhGia(100000, 5, 95, Luc.AddDays(30), Luc, QuyDinhMau.DuAn);
            Assert.Equal(95, p.PassThresholdPercent);
        }

        [Fact]
        public void Admin_thu_hep_khoang_sau_khi_cau_hinh_thi_chan_publish()
        {
            LabelingProject p = DuAnSanSang();
            QuyDinhDuAn hep = new QuyDinhDuAn
            {
                DoDaiTenToiDa = 200,
                DoDaiMoTaToiDa = 5000,
                NguongDatToiThieu = 85,
                NguongDatToiDa = 95,
                SoCauToiDa = 500,
                SoSuatToiThieu = 3,
                SoSuatToiDa = 50,
                SoCauTestMacDinh = 10,
                NguongDauMacDinh = 80,
                SoCauTestToiDa = 500,
                DoDaiHuongDanToiDa = 20000,
                SoViDuToiDa = 20,
                DoDaiGiaiThichToiDa = 1000,
            };

            Assert.Contains("nguong_dat_ngoai_khoang", p.NhungGiConThieu(100, 0, false, hep, null, Luc));
        }

        [Fact]
        public void So_suat_va_thuong_toi_thieu()
        {
            LabelingProject p = DuAnMoi();

            InvalidValueException it = Assert.Throws<InvalidValueException>(() =>
                p.DatCauHinhGia(100000, 2, 80, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));
            Assert.Equal("so_suat_khong_hop_le", it.Code);

            InvalidValueException re = Assert.Throws<InvalidValueException>(() =>
                p.DatCauHinhGia(999, 5, 80, Luc.AddDays(30), Luc, QuyDinhMau.DuAn));
            Assert.Equal("thuong_duoi_toi_thieu", re.Code);
        }

        [Fact]
        public void Ky_quy_bang_so_suat_nhan_thuong_cong_phi_cong_ngan_sach_cong_link()
        {
            LabelingProject p = DuAnSanSang();

            // 5 suat x (100.000 + 30.000 phi) = 650.000d — khong phu thuoc so cau.
            Assert.Equal(650000, p.ChiPhiUocTinhVnd(Phi));

            p.DatKenhPhanPhoi(true, true, false, 200, 50000, Luc);
            Assert.Equal(700000, p.ChiPhiUocTinhVnd(Phi));

            // Tat cong link → gia / ngan sach cong link ve 0.
            p.DatKenhPhanPhoi(true, false, false, 200, 50000, Luc);
            Assert.Equal(0, p.GateBudgetVnd);
            Assert.Equal(650000, p.ChiPhiUocTinhVnd(Phi));
        }

        [Fact]
        public void Bat_cong_link_ma_chua_dat_gia_thi_chua_publish_duoc()
        {
            LabelingProject p = DuAnSanSang();
            p.DatKenhPhanPhoi(true, true, false, null, null, Luc);

            Assert.Contains("chua_cau_hinh_cong_link", Thieu(p, 10, 0));
        }

        [Fact]
        public void Yeu_cau_test_thi_phai_du_cau_hoi_vang_cho_test()
        {
            LabelingProject p = DuAnSanSang();
            p.DatDieuKienThamGia(null, null, true, 10, 80, Luc, QuyDinhMau.DuAn);

            Assert.Contains("thieu_cau_hoi_vang_cho_test", Thieu(p, 100, 9));
            Assert.Empty(Thieu(p, 100, 10));
        }

        [Fact]
        public void Khong_co_cau_vang_kiem_tra_chi_la_canh_bao()
        {
            Assert.Contains("khong_co_cau_vang_kiem_tra", LabelingProject.CanhBao(0));
            Assert.Empty(LabelingProject.CanhBao(1));
        }

        [Fact]
        public void Tap_nhan_co_cau_tra_loi_mo_thi_canh_bao_cham_lau()
        {
            LabelSchema coMo = LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]},"
                + "{\"name\":\"mo_ta\",\"kind\":\"freeText\"}]}");
            LabelSchema khongMo = LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}");

            Assert.Contains("cham_cau_tra_loi_mo_lau", LabelingProject.CanhBao(1, coMo));
            Assert.Empty(LabelingProject.CanhBao(1, khongMo));
        }

        [Fact]
        public void Video_dang_chuan_bi_thi_chua_publish_duoc_video_loi_chi_canh_bao()
        {
            LabelingProject p = DuAnSanSang();

            Assert.Contains("video_dang_chuan_bi", p.NhungGiConThieu(100, 0, false, QuyDinhMau.DuAn, null, Luc, 2));
            Assert.Empty(p.NhungGiConThieu(100, 0, false, QuyDinhMau.DuAn, null, Luc, 0));
            Assert.Throws<RuleViolationException>(() => p.YeuCauPublish(100, 0, Phi, false, QuyDinhMau.DuAn, null, Luc, 1));
            Assert.Equal(ProjectStatus.Draft, p.Status);

            Assert.Contains("video_khong_xu_ly_duoc", LabelingProject.CanhBao(1, null, 1));
            Assert.Empty(LabelingProject.CanhBao(1, null, 0));
        }

        [Fact]
        public void Duong_di_hanh_phuc_cua_saga_Nhap_toi_Dang_chay()
        {
            LabelingProject p = DuAnSanSang();

            Publish(p, 100);
            Assert.Equal(ProjectStatus.PendingEscrow, p.Status);
            Assert.Equal(100, p.QuestionCount);

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
            Publish(p, 100);

            p.KyQuyBiTuChoi("So du khong du", Luc);

            Assert.Equal(ProjectStatus.Draft, p.Status);
            Assert.Equal("So du khong du", p.StatusReason);
            Assert.False(p.WasEscrowed);
        }

        [Fact]
        public void Khong_the_publish_hai_lan()
        {
            LabelingProject p = DuAnSanSang();
            Publish(p, 100);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => Publish(p, 100));
            Assert.Equal("chuyen_trang_thai_khong_hop_le", ex.Code);
        }

        [Fact]
        public void Khong_sua_duoc_cau_hinh_khi_da_roi_Nhap()
        {
            LabelingProject p = DuAnSanSang();
            Publish(p, 100);

            Assert.Throws<RuleViolationException>(() => p.DatCauHinhGia(100000, 5, 80, Luc.AddDays(1), Luc, QuyDinhMau.DuAn));
            Assert.Throws<RuleViolationException>(() => p.KiemTraCoTheNapDuLieu());
        }

        [Fact]
        public void Khong_cho_huy_khi_dang_cho_ky_quy()
        {
            LabelingProject p = DuAnSanSang();
            Publish(p, 100);

            Assert.Throws<RuleViolationException>(() => p.Huy("doi y", Luc));
        }

        [Fact]
        public void Qua_72_gio_cho_duyet_thi_tu_huy()
        {
            TimeSpan han = TimeSpan.FromHours(72);
            LabelingProject p = DuAnSanSang();
            Publish(p, 100);
            p.XacNhanDaKyQuy(Luc);

            Assert.False(p.DaQuaHanChoDuyet(han, Luc.AddHours(71)));
            Assert.Throws<RuleViolationException>(() => p.HuyDoQuaHanChoDuyet(han, Luc.AddHours(71)));

            p.HuyDoQuaHanChoDuyet(han, Luc.AddHours(72));
            Assert.Equal(ProjectStatus.Cancelled, p.Status);
            Assert.True(p.WasEscrowed);
        }

        [Fact]
        public void Dong_thu_bai_thi_Dang_cham_roi_Hoan_thanh_khi_chot()
        {
            LabelingProject p = DuAnDangChay();

            p.TamDung(Luc);
            Assert.Equal(ProjectStatus.Paused, p.Status);
            p.TiepTuc(Luc);

            // Hoan thanh chi den tu project.settled, sau Dang cham.
            Assert.Throws<RuleViolationException>(() => p.HoanThanh(Luc));

            Assert.True(p.BatDauCham(Luc.AddDays(1)));
            Assert.Equal(ProjectStatus.Grading, p.Status);
            Assert.Equal(Luc.AddDays(1), p.GradingStartedAt);

            // Event giao lai: khong lam gi.
            Assert.False(p.BatDauCham(Luc.AddDays(1)));

            Assert.True(p.HoanThanh(Luc.AddDays(4)));
            Assert.Equal(ProjectStatus.Completed, p.Status);
            Assert.True(p.DaKetThuc());
            Assert.False(p.HoanThanh(Luc.AddDays(4)));

            Assert.Throws<RuleViolationException>(() => p.TamDung(Luc));
        }

        [Fact]
        public void Ket_thuc_som_chi_mot_lan_va_chi_khi_dang_chay_hoac_tam_dung()
        {
            LabelingProject nhap = DuAnSanSang();
            Assert.Throws<RuleViolationException>(() => nhap.YeuCauKetThucSom(Luc));

            LabelingProject p = DuAnDangChay();
            p.YeuCauKetThucSom(Luc);
            Assert.Equal(Luc, p.FinishRequestedAt);

            RuleViolationException lai = Assert.Throws<RuleViolationException>(() => p.YeuCauKetThucSom(Luc));
            Assert.Equal("da_yeu_cau_ket_thuc", lai.Code);
        }

        [Fact]
        public void Huy_du_an_dang_chay_phai_tam_dung_truoc()
        {
            LabelingProject p = DuAnDangChay();

            RuleViolationException loi = Assert.Throws<RuleViolationException>(() => p.Huy("doi y", Luc));
            Assert.Equal("can_tam_dung_truoc", loi.Code);

            p.TamDung(Luc);
            p.Huy("doi y", Luc);
            Assert.Equal(ProjectStatus.Cancelled, p.Status);
        }

        [Fact]
        public void Cau_hoi_vang_chi_sua_khi_Nhap_de_moi_bai_cham_cung_luat()
        {
            LabelingProject p = DuAnDangChay();
            p.TamDung(Luc);

            Assert.Throws<RuleViolationException>(() => p.KiemTraCoTheSuaCauHoiVang());
        }

        [Fact]
        public void Khong_tu_tham_gia_duoc_du_an_rieng_tu()
        {
            LabelingProject p = LabelingProject.Tao(Owner, "rieng", null, Modalities.Image, ProjectVisibility.Private, Luc, QuyDinhMau.DuAn);
            p.DatLabelSchema(PhanLoaiAnh("a", "b"), Luc);
            p.DatHuongDan(Guideline.Tao("x", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(100000, 3, 80, Luc.AddDays(1), Luc, QuyDinhMau.DuAn);
            Publish(p, 1);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.KiemTraChoTuThamGia());
            Assert.Equal("du_an_rieng_tu", ex.Code);
        }

        [Fact]
        public void Phi_cong_them_duoc_chot_luc_publish_VD_M_15()
        {
            // 200.000d + 30% = 60.000d phi tren moi nguoi dat.
            Assert.Equal(60000, LabelingProject.TinhPhiVnd(200000, 30));

            LabelingProject p = DuAnSanSang();
            Publish(p, 100);
            Assert.Equal(Phi, p.PlatformFeePercent);
        }

        [Fact]
        public void Chi_phi_tran_so_thi_nem_loi_thay_vi_thanh_so_am()
        {
            LabelingProject p = DuAnMoi();
            p.DatCauHinhGia(long.MaxValue / 4, 50, 80, Luc.AddDays(1), Luc, QuyDinhMau.DuAn);

            Assert.Throws<OverflowException>(() => p.ChiPhiUocTinhVnd(Phi));
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
