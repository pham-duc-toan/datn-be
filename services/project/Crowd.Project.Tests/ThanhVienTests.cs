using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Cac duong vao du an: che do hien thi (public / private / approval), ma moi, don xin tham
    /// gia, dieu kien labeler. Test thuan, khong database.
    /// </summary>
    public sealed class ThanhVienTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Owner = Guid.CreateVersion7();
        private static readonly Guid Labeler = Guid.CreateVersion7();

        private static LabelingProject DuAnDangChay(ProjectVisibility cheDo, int? minLevel, int? minReputation)
        {
            LabelingProject p = LabelingProject.Tao(Owner, "du an", null, Modalities.Image, cheDo, Luc, QuyDinhMau.DuAn);
            p.DatLabelSchema(LabelingProjectTests.PhanLoaiAnh("a", "b"), Luc);
            p.DatHuongDan(Guideline.Tao("x", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(100000, 3, 80, Luc.AddDays(1), Luc, QuyDinhMau.DuAn);
            p.DatDieuKienThamGia(minLevel, minReputation, false, 1, 80, Luc, QuyDinhMau.DuAn);
            p.YeuCauPublish(1, 0, 30, false, QuyDinhMau.DuAn, null, Luc);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);
            return p;
        }

        private static HoSoNguoiDung HoSo(int cap, int? uyTin)
        {
            return new HoSoNguoiDung(Labeler, true, false, cap, uyTin);
        }

        // ------------------------------------------------------------ che do hien thi

        [Fact]
        public void Du_an_can_duyet_khong_join_thang_nhung_xin_duoc()
        {
            LabelingProject p = DuAnDangChay(ProjectVisibility.Approval, null, null);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.KiemTraChoTuThamGia());
            Assert.Equal("can_xin_duyet", ex.Code);
            p.KiemTraChoXinThamGia();
        }

        [Fact]
        public void Du_an_rieng_tu_khong_xin_duoc_nhung_vao_duoc_bang_ma_moi()
        {
            LabelingProject p = DuAnDangChay(ProjectVisibility.Private, null, null);

            Assert.Equal("du_an_rieng_tu", Assert.Throws<RuleViolationException>(() => p.KiemTraChoXinThamGia()).Code);
            p.KiemTraChoVaoBangMaMoi();
        }

        [Fact]
        public void Ma_moi_dung_duoc_truoc_khi_publish_nhung_khong_sau_khi_ket_thuc()
        {
            LabelingProject nhap = LabelingProject.Tao(Owner, "nhap", null, Modalities.Image, ProjectVisibility.Private, Luc, QuyDinhMau.DuAn);
            nhap.KiemTraChoVaoBangMaMoi();

            nhap.Huy(null, Luc);
            Assert.Equal("du_an_da_ket_thuc", Assert.Throws<RuleViolationException>(() => nhap.KiemTraChoVaoBangMaMoi()).Code);
        }

        // ------------------------------------------------------------ dieu kien labeler

        [Fact]
        public void Dieu_kien_labeler_cung_ma_loi_voi_task_svc()
        {
            LabelingProject p = DuAnDangChay(ProjectVisibility.Public, 3, 70);

            Assert.Null(p.LyDoLabelerKhongDuDieuKien(HoSo(3, 70)));
            Assert.Equal("chua_du_cap_do", p.LyDoLabelerKhongDuDieuKien(HoSo(2, 90)));
            Assert.Equal("chua_du_uy_tin", p.LyDoLabelerKhongDuDieuKien(HoSo(5, 69)));

            // Chua duoc cham uy tin ma du an doi uy tin → khong du (khong "khong biet thi cho qua").
            Assert.Equal("chua_du_uy_tin", p.LyDoLabelerKhongDuDieuKien(HoSo(5, null)));
            Assert.Equal("tai_khoan_bi_khoa", p.LyDoLabelerKhongDuDieuKien(new HoSoNguoiDung(Labeler, true, true, 5, 90)));
            Assert.Equal("khong_phai_labeler", p.LyDoLabelerKhongDuDieuKien(new HoSoNguoiDung(Labeler, false, false, 5, 90)));

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => p.KiemTraLabelerDuDieuKien(HoSo(1, 90)));
            Assert.Equal("chua_du_cap_do", ex.Code);
            Assert.Contains("tối thiểu 3, hiện tại là 1", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Du_an_khong_dat_dieu_kien_thi_ai_cung_du()
        {
            LabelingProject p = DuAnDangChay(ProjectVisibility.Public, null, null);
            Assert.Null(p.LyDoLabelerKhongDuDieuKien(HoSo(1, null)));
        }

        // ------------------------------------------------------------ ma moi

        [Fact]
        public void Ma_moi_16_ky_tu_de_doc_va_chuan_hoa_khi_go_tay()
        {
            InviteCode m = InviteCode.Tao(Guid.NewGuid(), Owner, null, TimeSpan.FromDays(7), " nhom A ", Luc, QuyDinhMau.ThanhVien);

            Assert.Equal(InviteCode.DoDaiMa, m.Code.Length);
            Assert.DoesNotContain('O', m.Code);
            Assert.DoesNotContain('0', m.Code);
            Assert.DoesNotContain('I', m.Code);
            Assert.DoesNotContain('1', m.Code);
            Assert.Equal("nhom A", m.Note);
            Assert.Equal(Luc.AddDays(7), m.ExpiresAt);

            string goTay = m.Code.Substring(0, 4).ToLowerInvariant() + "-" + m.Code.Substring(4, 4) + " " + m.Code.Substring(8);
            Assert.Equal(m.Code, InviteCode.ChuanHoa(goTay));
        }

        [Fact]
        public void Ma_moi_het_luot_het_han_thu_hoi()
        {
            InviteCode m = InviteCode.Tao(Guid.NewGuid(), Owner, 2, TimeSpan.FromHours(1), null, Luc, QuyDinhMau.ThanhVien);

            m.Dung(Luc);
            m.Dung(Luc);
            Assert.Equal("exhausted", m.TrangThai(Luc));
            Assert.Equal("ma_moi_het_luot", Assert.Throws<RuleViolationException>(() => m.Dung(Luc)).Code);

            InviteCode h = InviteCode.Tao(Guid.NewGuid(), Owner, null, TimeSpan.FromHours(1), null, Luc, QuyDinhMau.ThanhVien);
            Assert.Equal("active", h.TrangThai(Luc.AddMinutes(59)));
            Assert.Equal("ma_moi_het_han", Assert.Throws<RuleViolationException>(() => h.Dung(Luc.AddHours(1))).Code);

            h.ThuHoi(Luc);
            Assert.Equal("revoked", h.TrangThai(Luc));
            Assert.Equal("ma_moi_da_thu_hoi", Assert.Throws<RuleViolationException>(() => h.ThuHoi(Luc)).Code);
        }

        [Fact]
        public void Ma_moi_kiem_han_va_so_luot()
        {
            QuyDinhThanhVien q = QuyDinhMau.ThanhVien;

            Assert.Equal("han_ma_moi_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => InviteCode.Tao(Guid.NewGuid(), Owner, null, TimeSpan.FromMinutes(30), null, Luc, q)).Code);
            Assert.Equal("han_ma_moi_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => InviteCode.Tao(Guid.NewGuid(), Owner, null, q.HanMaMoiToiDa + TimeSpan.FromHours(1), null, Luc, q)).Code);
            Assert.Equal("so_luot_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => InviteCode.Tao(Guid.NewGuid(), Owner, 0, TimeSpan.FromDays(1), null, Luc, q)).Code);
        }

        [Fact]
        public void Hai_ma_khac_nhau()
        {
            InviteCode a = InviteCode.Tao(Guid.NewGuid(), Owner, null, TimeSpan.FromDays(1), null, Luc, QuyDinhMau.ThanhVien);
            InviteCode b = InviteCode.Tao(Guid.NewGuid(), Owner, null, TimeSpan.FromDays(1), null, Luc, QuyDinhMau.ThanhVien);
            Assert.NotEqual(a.Code, b.Code);
        }

        // ------------------------------------------------------------ don xin tham gia

        [Fact]
        public void Don_chi_xu_ly_mot_lan()
        {
            JoinRequest d = JoinRequest.Tao(Guid.NewGuid(), Labeler, "  cho em vao  ", null, null, Luc);
            Assert.Equal("cho em vao", d.Message);
            Assert.Equal(JoinRequestStatus.Pending, d.Status);

            d.Duyet(Owner, Luc);
            Assert.Equal(JoinRequestStatus.Approved, d.Status);
            Assert.Equal(Owner, d.DecidedBy);
            Assert.Equal("don_da_xu_ly", Assert.Throws<RuleViolationException>(() => d.TuChoi(Owner, null, Luc)).Code);
            Assert.Equal("don_da_xu_ly", Assert.Throws<RuleViolationException>(() => d.Huy(Luc)).Code);
        }

        [Fact]
        public void Bi_tu_choi_thi_cho_moi_xin_lai()
        {
            JoinRequest d = JoinRequest.Tao(Guid.NewGuid(), Labeler, null, null, null, Luc);
            Assert.Null(d.DuocXinLaiTu(TimeSpan.FromDays(1)));

            d.TuChoi(Owner, "chua du kinh nghiem", Luc.AddHours(2));
            Assert.Equal("chua du kinh nghiem", d.RejectReason);
            Assert.Equal(Luc.AddHours(26), d.DuocXinLaiTu(TimeSpan.FromDays(1)));

            JoinRequest rut = JoinRequest.Tao(Guid.NewGuid(), Labeler, null, null, null, Luc);
            rut.Huy(Luc);
            Assert.Equal(JoinRequestStatus.Cancelled, rut.Status);
            Assert.Null(rut.DuocXinLaiTu(TimeSpan.FromDays(1)));
        }

        [Fact]
        public void Loi_nhan_qua_dai_bi_tu_choi()
        {
            Assert.Equal("loi_nhan_qua_dai", Assert.Throws<InvalidValueException>(
                () => JoinRequest.Tao(Guid.NewGuid(), Labeler, new string('a', JoinRequest.DoDaiLoiNhanToiDa + 1), null, null, Luc)).Code);
        }

        [Fact]
        public void Don_tu_bai_test_mang_diem()
        {
            Guid lan = Guid.NewGuid();
            JoinRequest d = JoinRequest.Tao(Guid.NewGuid(), Labeler, null, lan, 90, Luc);
            Assert.Equal(lan, d.EntranceAttemptId);
            Assert.Equal(90, d.ScorePercent);
        }
    }
}
