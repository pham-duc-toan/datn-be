using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Eligibility;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;

namespace Crowd.Tasking.Tests
{
    /// <summary>Bai lam ca du an (docs 3.18): bat dau → luu nhap → nop ca bai → cham → khieu nai.</summary>
    public sealed class AttemptTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly RawJson KhongMetadata = SampleMetadata.Rong.ToRawJson();

        private static Attempt BaiMoi()
        {
            return Attempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }, Luc, Luc.AddHours(48));
        }

        private static Attempt BaiDaCham(bool dat)
        {
            Attempt a = BaiMoi();
            a.Nop(1, dat ? 1 : 0, null, null, Luc.AddHours(1));
            a.GhiKetQua(10, dat ? 9 : 5, dat ? 90 : 50, dat, dat, Luc.AddDays(1));
            return a;
        }

        [Fact]
        public void Cau_hoi_phai_co_dung_mot_trong_file_hoac_noi_dung()
        {
            Assert.Throws<ArgumentException>(() =>
                LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), Modalities.Image, null, null, KhongMetadata, Luc));
            Assert.Throws<ArgumentException>(() =>
                LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), Modalities.Text, "k.png", RawJson.Tu("{\"text\":\"x\"}"), KhongMetadata, Luc));
        }

        [Fact]
        public void De_rong_hoac_het_han_chot_thi_khong_bat_dau_duoc()
        {
            RuleViolationException rong = Assert.Throws<RuleViolationException>(() =>
                Attempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), new List<Guid>(), Luc, Luc.AddHours(1)));
            Assert.Equal("du_an_chua_co_cau_hoi", rong.Code);

            Assert.Throws<RuleViolationException>(() =>
                Attempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, Luc, Luc));
        }

        [Fact]
        public void Nop_ca_bai_mot_lan_khong_sua_duoc_nua()
        {
            Attempt a = BaiMoi();
            Assert.True(a.DangLam);

            a.Nop(2, 1, "1.2.3.4", "thiet-bi", Luc.AddHours(2));
            Assert.Equal(AttemptState.Submitted, a.State);
            Assert.Equal(2, a.GoldTotal);
            Assert.Equal(1, a.GoldCorrect);

            RuleViolationException lai = Assert.Throws<RuleViolationException>(() => a.Nop(2, 2, null, null, Luc.AddHours(3)));
            Assert.Equal("bai_da_nop", lai.Code);
            Assert.Throws<RuleViolationException>(() => a.KiemConLamDuoc(Luc.AddHours(3)));
        }

        [Fact]
        public void Het_thoi_gian_giu_bai_thi_khong_nop_duoc_VD_T_01()
        {
            Attempt a = BaiMoi();

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.Nop(0, 0, null, null, Luc.AddHours(48)));
            Assert.Equal("bai_da_het_han", ex.Code);

            Assert.True(a.HetHan(Luc.AddHours(48)));
            Assert.Equal(AttemptState.Expired, a.State);
            Assert.False(a.HetHan(Luc.AddHours(49)));
        }

        [Fact]
        public void Huy_chi_bai_dang_lam_bai_da_nop_giu_nguyen()
        {
            Attempt dangLam = BaiMoi();
            Assert.True(dangLam.Huy(Luc));
            Assert.Equal(AttemptState.Cancelled, dangLam.State);

            Attempt daNop = BaiMoi();
            daNop.Nop(0, 0, null, null, Luc.AddHours(1));
            Assert.False(daNop.Huy(Luc.AddHours(2)));
            Assert.Equal(AttemptState.Submitted, daNop.State);
        }

        [Fact]
        public void Tam_dung_duoc_cong_bu_thoi_gian_nhung_khong_qua_han_chot()
        {
            Attempt a = BaiMoi();
            a.CongBuThoiGian(TimeSpan.FromHours(5), Luc.AddDays(10));
            Assert.Equal(Luc.AddHours(53), a.ExpiresAt);

            a.CongBuThoiGian(TimeSpan.FromDays(30), Luc.AddDays(3));
            Assert.Equal(Luc.AddDays(3), a.ExpiresAt);
        }

        [Fact]
        public void Cau_vang_dung_khong_vuot_tong_so_cau_vang()
        {
            Attempt a = BaiMoi();
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Nop(1, 2, null, null, Luc.AddHours(1)));
        }

        [Fact]
        public void Chi_cham_bai_da_nop_va_cham_lai_khong_doi_ket_qua()
        {
            Attempt dangLam = BaiMoi();
            Assert.Throws<RuleViolationException>(() => dangLam.GhiKetQua(1, 1, 100, true, true, Luc));

            Attempt a = BaiDaCham(true);
            a.GhiKetQua(10, 0, 0, false, false, Luc.AddDays(2));
            Assert.True(a.Passed);
            Assert.Equal(90, a.AgreementPercent);
            Assert.True(a.DatCuoiCung);
        }

        [Fact]
        public void Chi_bai_truot_khieu_nai_duoc_mot_lan_truoc_han()
        {
            Attempt dat = BaiDaCham(true);
            Assert.Equal("bai_da_dat", Assert.Throws<RuleViolationException>(() => dat.KhieuNai("x", Luc.AddDays(5), Luc.AddDays(2))).Code);

            Attempt truot = BaiDaCham(false);
            Assert.Equal("het_han_khieu_nai", Assert.Throws<RuleViolationException>(() =>
                truot.KhieuNai("Da so sai o cau 3", Luc.AddDays(4), Luc.AddDays(5))).Code);
            Assert.Throws<InvalidValueException>(() => truot.KhieuNai("   ", Luc.AddDays(4), Luc.AddDays(2)));

            truot.KhieuNai("Da so sai o cau 3", Luc.AddDays(4), Luc.AddDays(2));
            Assert.Equal(AppealState.Open, truot.AppealState);
            Assert.Equal("da_khieu_nai", Assert.Throws<RuleViolationException>(() =>
                truot.KhieuNai("lan nua", Luc.AddDays(4), Luc.AddDays(3))).Code);
        }

        [Fact]
        public void Admin_chap_nhan_khieu_nai_thi_bai_dat_cuoi_cung()
        {
            Attempt a = BaiDaCham(false);
            Assert.False(a.DatCuoiCung);
            Assert.Throws<RuleViolationException>(() => a.PhanXu(true, null, Guid.NewGuid(), true, Luc.AddDays(2)));

            a.KhieuNai("Xem lai giup", Luc.AddDays(4), Luc.AddDays(2));
            a.PhanXu(true, "Da so sai", Guid.NewGuid(), true, Luc.AddDays(3));

            Assert.Equal(AppealState.Accepted, a.AppealState);
            Assert.False(a.Passed);
            Assert.True(a.DatCuoiCung);
        }

        [Fact]
        public void Dat_ma_het_suat_thi_khong_duoc_thuong_khieu_nai_chi_thuong_khi_con_suat()
        {
            Attempt hetSuat = BaiMoi();
            hetSuat.Nop(1, 1, null, null, Luc.AddHours(1));
            hetSuat.GhiKetQua(10, 9, 90, true, false, Luc.AddDays(1));
            Assert.True(hetSuat.DatCuoiCung);
            Assert.False(hetSuat.DuocThuong);

            Attempt truot = BaiMoi();
            truot.Nop(1, 0, null, null, Luc.AddHours(1));
            Assert.Throws<InvalidValueException>(() => truot.GhiKetQua(10, 5, 50, false, true, Luc.AddDays(1)));

            Attempt a = BaiDaCham(false);
            a.KhieuNai("Xem lai", Luc.AddDays(4), Luc.AddDays(2));
            a.PhanXu(true, null, Guid.NewGuid(), false, Luc.AddDays(3));
            Assert.True(a.DatCuoiCung);
            Assert.False(a.DuocThuong);

            Attempt b = BaiDaCham(false);
            b.KhieuNai("Xem lai", Luc.AddDays(4), Luc.AddDays(2));
            b.PhanXu(true, null, Guid.NewGuid(), true, Luc.AddDays(3));
            Assert.True(b.DuocThuong);
        }

        [Fact]
        public void Cau_tra_loi_toi_da_256_KB()
        {
            string lon = "{\"x\":\"" + new string('a', AttemptAnswer.KichThuocToiDa) + "\"}";
            Assert.Throws<InvalidValueException>(() => AttemptAnswer.Tao(Guid.NewGuid(), Guid.NewGuid(), lon, Luc));

            AttemptAnswer ok = AttemptAnswer.Tao(Guid.NewGuid(), Guid.NewGuid(), "{\"label\":{\"labelIds\":[\"cho\"]}}", Luc);
            ok.Ghi("{\"label\":{\"labelIds\":[\"meo\"]}}", Luc.AddMinutes(1));
            Assert.Contains("meo", ok.DataJson);
            Assert.Equal(Luc.AddMinutes(1), ok.UpdatedAt);
        }

        [Fact]
        public void Mau_vang_va_mau_bi_loai()
        {
            LabelingTask t = LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), Modalities.Image, "k.png", null, KhongMetadata, Luc);

            t.LoaiTruVi(true);
            Assert.Equal(TaskState.Excluded, t.State);
            t.LoaiTruVi(false);
            Assert.Equal(TaskState.Open, t.State);

            Assert.True(t.LoaiBo(Luc));
            Assert.Equal(TaskState.Cancelled, t.State);
            Assert.False(t.LoaiBo(Luc));
        }
    }

    /// <summary>Ban sao du an: thu bai → cham → han khieu nai → chot (docs 3.18).</summary>
    public sealed class ThuBaiSnapshotTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        private static ProjectSnapshot DuAnChay()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), Luc);
            s.ApDungPublished(Guid.NewGuid(), OutOfOrderEventTests.TapNhanAnh("a", "b"), 100000, 5, 5, 80, 20, Luc.AddDays(10), true, false, null, null, false, Luc);
            return s;
        }

        [Fact]
        public void Ban_sao_giu_thuong_suat_nguong_so_cau()
        {
            ProjectSnapshot s = DuAnChay();

            Assert.Equal(100000, s.RewardVnd);
            Assert.Equal(5, s.Slots);
            Assert.Equal(80, s.PassThresholdPercent);
            Assert.Equal(20, s.QuestionCount);
            Assert.True(s.ConNhanBai(Luc.AddDays(1)));
            Assert.False(s.ConNhanBai(Luc.AddDays(10)));
        }

        [Fact]
        public void Dong_thu_bai_mot_lan_roi_khong_nhan_bai_nua()
        {
            ProjectSnapshot s = DuAnChay();

            Assert.True(s.DongThuBai("du_suat", Luc.AddDays(1)));
            Assert.False(s.DongThuBai("het_han", Luc.AddDays(2)));
            Assert.Equal("du_suat", s.FinishReason);
            Assert.False(s.ConNhanBai(Luc.AddDays(1)));
        }

        [Fact]
        public void Cham_mo_han_khieu_nai_va_chot_mot_lan()
        {
            ProjectSnapshot s = DuAnChay();
            s.DongThuBai("du_suat", Luc.AddDays(1));

            // Chua cham thi chua chot duoc.
            Assert.False(s.Chot(3, Luc.AddDays(2)));

            Assert.True(s.GhiDaCham(Luc.AddDays(1), TimeSpan.FromDays(3)));
            Assert.False(s.GhiDaCham(Luc.AddDays(2), TimeSpan.FromDays(3)));
            Assert.Equal(Luc.AddDays(4), s.AppealDeadline);

            Assert.True(s.Chot(3, Luc.AddDays(4)));
            Assert.False(s.Chot(4, Luc.AddDays(5)));
            Assert.Equal(3, s.PassedCount);
        }

        [Fact]
        public void Tiep_tuc_tra_ve_thoi_gian_da_tam_dung()
        {
            ProjectSnapshot s = DuAnChay();
            s.TamDung(Luc.AddHours(1));

            Assert.Equal(TimeSpan.FromHours(3), s.TiepTuc(Luc.AddHours(4)));
            Assert.Null(s.PausedAt);

            // Event cu → bo qua.
            Assert.Null(s.TiepTuc(Luc));
        }
    }

    public sealed class EligibilityTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid DuAnId = Guid.NewGuid();
        private static readonly Guid UserId = Guid.NewGuid();

        private static ProjectSnapshot DuAnChay(int? minLevel, int? minReputation)
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(DuAnId, Luc);
            s.ApDungPublished(Guid.NewGuid(), OutOfOrderEventTests.TapNhanAnh("a", "b"), 100000, 3, 3, 80, 10, Luc.AddDays(10), true, false, minLevel, minReputation, false, Luc);
            return s;
        }

        private static MemberCache Labeler(CachedMemberState state)
        {
            return MemberCache.Tao(DuAnId, UserId, CachedMemberRole.Labeler, state, Luc);
        }

        [Fact]
        public void Du_dieu_kien_thi_null()
        {
            Assert.Null(EligibilityPolicy.LyDoTuChoi(DuAnChay(null, null), Labeler(CachedMemberState.Active), null, Luc));
        }

        [Fact]
        public void Thieu_ban_sao_thi_tu_choi_fail_safe_VD_D_04()
        {
            Assert.Equal("du_an_chua_san_sang", EligibilityPolicy.LyDoTuChoi(null, Labeler(CachedMemberState.Active), null, Luc));
            Assert.Equal("khong_phai_thanh_vien", EligibilityPolicy.LyDoTuChoi(DuAnChay(null, null), null, null, Luc));

            // Du an doi cap do ma chua biet cap do cua labeler → tu choi, KHONG cho qua.
            Assert.Equal("chua_du_cap_do", EligibilityPolicy.LyDoTuChoi(DuAnChay(2, null), Labeler(CachedMemberState.Active), null, Luc));

            // Chua biet cap = cap 1 (nhu identity): du an chi doi cap 1 thi khong chan nguoi moi.
            Assert.Null(EligibilityPolicy.LyDoTuChoi(DuAnChay(1, null), Labeler(CachedMemberState.Active), null, Luc));
        }

        [Fact]
        public void Bi_chan_khoi_du_an_hoac_bi_khoa_tai_khoan()
        {
            Assert.Equal("bi_chan_khoi_du_an", EligibilityPolicy.LyDoTuChoi(DuAnChay(null, null), Labeler(CachedMemberState.Blocked), null, Luc));

            LabelerProfile khoa = LabelerProfile.Tao(UserId);
            khoa.Khoa(Luc);
            Assert.Equal("tai_khoan_bi_khoa", EligibilityPolicy.LyDoTuChoi(DuAnChay(null, null), Labeler(CachedMemberState.Active), khoa, Luc));
        }

        [Fact]
        public void Loc_theo_cap_do_va_uy_tin()
        {
            LabelerProfile p = LabelerProfile.Tao(UserId);
            p.DatLevel(3, Luc);
            p.DatReputation(70, Luc);

            Assert.Null(EligibilityPolicy.LyDoTuChoi(DuAnChay(3, 70), Labeler(CachedMemberState.Active), p, Luc));
            Assert.Equal("chua_du_uy_tin", EligibilityPolicy.LyDoTuChoi(DuAnChay(3, 71), Labeler(CachedMemberState.Active), p, Luc));
        }

        [Fact]
        public void Du_an_tam_dung_qua_deadline_hoac_da_dong_thu_bai_thi_khong_cho_lam()
        {
            ProjectSnapshot s = DuAnChay(null, null);
            Assert.Equal("da_qua_deadline", EligibilityPolicy.LyDoTuChoi(s, Labeler(CachedMemberState.Active), null, Luc.AddDays(11)));

            s.DongThuBai("du_suat", Luc);
            Assert.Equal("du_an_da_dong_thu_bai", EligibilityPolicy.LyDoTuChoi(s, Labeler(CachedMemberState.Active), null, Luc));

            ProjectSnapshot d = DuAnChay(null, null);
            d.TamDung(Luc.AddMinutes(1));
            Assert.Equal("du_an_khong_chay", EligibilityPolicy.LyDoTuChoi(d, Labeler(CachedMemberState.Active), null, Luc));
        }

        [Fact]
        public void Chu_du_an_khong_lam_bai_cua_chinh_minh()
        {
            MemberCache chu = MemberCache.Tao(DuAnId, UserId, CachedMemberRole.Owner, CachedMemberState.Active, Luc);
            Assert.Equal("khong_phai_labeler", EligibilityPolicy.LyDoTuChoi(DuAnChay(null, null), chu, null, Luc));
        }
    }

    /// <summary>Event den sai thu tu (VD-D-05): ban cu den tre khong duoc ghi de ban moi.</summary>
    public sealed class OutOfOrderEventTests
    {
        private static readonly DateTimeOffset T1 = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset T2 = T1.AddMinutes(5);

        [Fact]
        public void Paused_toi_truoc_published_cu_hon_thi_van_la_Paused_nhung_co_cau_hinh()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);

            s.TamDung(T2);
            s.ApDungPublished(Guid.NewGuid(), TapNhanAnh("a", "b"), 100000, 3, 3, 80, 10, T2.AddDays(1), true, false, null, null, false, T1);

            Assert.Equal(SnapshotStatus.Paused, s.Status);
            Assert.True(s.IsConfigured);
            Assert.Equal(3, s.Slots);
        }

        [Fact]
        public void Du_an_da_dong_khong_mo_lai_duoc()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);
            s.Dong(T1);

            Assert.Null(s.TiepTuc(T2));
            Assert.Equal(SnapshotStatus.Closed, s.Status);
        }

        [Fact]
        public void Thanh_vien_removed_moi_hon_thang_added_cu()
        {
            MemberCache m = MemberCache.Tao(Guid.NewGuid(), Guid.NewGuid(), CachedMemberRole.Labeler, CachedMemberState.Removed, T2);

            Assert.False(m.ApDung(CachedMemberRole.Labeler, CachedMemberState.Active, T1));
            Assert.Equal(CachedMemberState.Removed, m.State);
        }

        [Fact]
        public void Uy_tin_va_cap_do_co_moc_rieng()
        {
            LabelerProfile p = LabelerProfile.Tao(Guid.NewGuid());
            p.DatReputation(80, T2);
            p.DatLevel(2, T1);

            Assert.False(p.DatReputation(40, T1));
            Assert.Equal(80, p.Reputation);
            Assert.Equal(2, p.Level);
        }

        [Fact]
        public void Ban_sao_giu_tap_nhan_va_doc_lai_duoc()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);
            Assert.Null(s.LabelSchema);

            s.ApDungPublished(Guid.NewGuid(), TapNhanAnh("cho", "meo"), 100000, 3, 3, 80, 10, T2, true, false, null, null, false, T1);

            Assert.Equal(Modalities.Image, s.Modality);
            Assert.NotNull(s.LabelSchema);

            // Nhan nop len kiem theo tap nhan cua ban sao.
            LabelPayload hopLe = LabelPayload.Tao(s.LabelSchema!, "{\"label\":{\"labelIds\":[\"cho\"]}}", null);
            Assert.Equal(Modalities.Image, hopLe.TaskType);

            Assert.Throws<LabelFormatException>(() =>
                LabelPayload.Tao(s.LabelSchema!, "{\"label\":{\"labelIds\":[\"cho\",\"meo\"]}}", null));
            Assert.Throws<LabelFormatException>(() =>
                LabelPayload.Tao(s.LabelSchema!, "{\"label\":{\"labelIds\":[\"voi\"]}}", null));
        }

        internal static LabelSchema TapNhanAnh(string a, string b)
        {
            return LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\",\"classes\":[\""
                + a + "\",\"" + b + "\"]}]}");
        }
    }
}
