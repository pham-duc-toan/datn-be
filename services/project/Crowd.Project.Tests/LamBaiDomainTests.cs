using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Eligibility;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Domain.Reports;

namespace Crowd.Project.Tests
{
    /// <summary>Dung du an o cac trang thai cho test lam bai (khong database).</summary>
    internal static class DuAnMau
    {
        public static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        public static LabelSchema TapNhanAnh(params string[] lop)
        {
            return LabelSchema.Doc("{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\",\"classes\":"
                                   + JsonSerializer.Serialize(lop) + "}]}");
        }

        /// <summary>Du an Dang chay: thuong 100.000d, 5 suat, nguong 80%, han chot Luc + 10 ngay.</summary>
        public static LabelingProject DangChay(int? minLevel, int? minReputation)
        {
            LabelingProject p = LabelingProject.Tao(Guid.NewGuid(), "Phan loai", "mo ta", Modalities.Image, ProjectVisibility.Public, Luc, QuyDinhMau.DuAn);
            p.DatLabelSchema(TapNhanAnh("cho", "meo"), Luc);
            p.DatHuongDan(Guideline.Tao("# Huong dan", null, QuyDinhMau.DuAn), Luc);
            p.DatCauHinhGia(100000, 5, 80, Luc.AddDays(10), Luc, QuyDinhMau.DuAn);
            p.DatDieuKienThamGia(minLevel, minReputation, false, 10, 80, Luc, QuyDinhMau.DuAn);
            p.YeuCauPublish(20, 0, 30, false, QuyDinhMau.DuAn, null, Luc);
            p.XacNhanDaKyQuy(Luc);
            p.Duyet(Luc);
            return p;
        }
    }

    /// <summary>Bai lam ca du an (docs 3.18): bat dau → nop ca bai (nhap o frontend) → cham → khieu nai.</summary>
    public sealed class AttemptTests
    {
        private static readonly DateTimeOffset Luc = DuAnMau.Luc;

        private static Attempt BaiMoi()
        {
            return Attempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }, Luc, Luc.AddHours(48));
        }

        private static Attempt BaiDaCham(bool dat)
        {
            Attempt a = BaiMoi();
            a.Nop(1, dat ? 1 : 0, 3, null, null, null, Luc.AddHours(1));
            a.GhiKetQua(10, dat ? 9 : 5, dat ? 90 : 50, dat, dat, Luc.AddDays(1));
            return a;
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
        public void Nop_ca_bai_mot_lan_chup_so_cau_va_cau_vang()
        {
            Attempt a = BaiMoi();
            Assert.True(a.DangLam);
            Assert.Null(a.AnsweredCount);

            a.Nop(2, 1, 3, "{\"x\":{\"label\":{\"labelIds\":[\"cho\"]}}}", "1.2.3.4", "thiet-bi", Luc.AddHours(2));
            Assert.Equal(AttemptState.Submitted, a.State);
            Assert.Equal(2, a.GoldTotal);
            Assert.Equal(1, a.GoldCorrect);
            Assert.Equal(3, a.AnsweredCount);
            Assert.Contains("cho", a.GoldAnswersJson);

            RuleViolationException lai = Assert.Throws<RuleViolationException>(() => a.Nop(2, 2, 3, null, null, null, Luc.AddHours(3)));
            Assert.Equal("bai_da_nop", lai.Code);
            Assert.Throws<RuleViolationException>(() => a.KiemConLamDuoc(Luc.AddHours(3)));
        }

        [Fact]
        public void So_cau_da_tra_loi_khong_vuot_so_cau_cua_de()
        {
            Attempt a = BaiMoi();
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Nop(0, 0, 4, null, null, null, Luc.AddHours(1)));
        }

        [Fact]
        public void Het_thoi_gian_giu_bai_thi_khong_nop_duoc_VD_T_01()
        {
            Attempt a = BaiMoi();

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.Nop(0, 0, 0, null, null, null, Luc.AddHours(48)));
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
            daNop.Nop(0, 0, 0, null, null, null, Luc.AddHours(1));
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
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Nop(1, 2, 0, null, null, null, Luc.AddHours(1)));
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
            hetSuat.Nop(1, 1, 3, null, null, null, Luc.AddHours(1));
            hetSuat.GhiKetQua(10, 9, 90, true, false, Luc.AddDays(1));
            Assert.True(hetSuat.DatCuoiCung);
            Assert.False(hetSuat.DuocThuong);

            // Lap suat trong: bai chua dat van duoc thuong, khong khieu nai nua.
            Attempt lapSuat = BaiMoi();
            lapSuat.Nop(1, 1, 3, null, null, null, Luc.AddHours(1));
            lapSuat.GhiKetQua(10, 7, 70, false, true, Luc.AddDays(1));
            Assert.False(lapSuat.DatCuoiCung);
            Assert.True(lapSuat.DuocThuong);
            Assert.Equal("bai_da_duoc_thuong",
                Assert.Throws<RuleViolationException>(() => lapSuat.KhieuNai("Xem lai", Luc.AddDays(4), Luc.AddDays(2))).Code);

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
        public void Lich_su_bai_cat_ghi_chu_qua_dai()
        {
            AttemptHistoryEntry h = AttemptHistoryEntry.Tao(Guid.NewGuid(), Guid.NewGuid(), AttemptHistoryEntry.KhieuNai, null,
                new string('x', AttemptHistoryEntry.DoDaiGhiChuToiDa + 10), Luc);
            Assert.Equal(AttemptHistoryEntry.DoDaiGhiChuToiDa, h.Note!.Length);
            Assert.Throws<ArgumentException>(() => AttemptHistoryEntry.Tao(Guid.NewGuid(), Guid.NewGuid(), " ", null, null, Luc));
        }
    }

    /// <summary>Dot thu bai: thu bai → cham → han khieu nai → chot (docs 3.18).</summary>
    public sealed class CollectionRoundTests
    {
        private static readonly DateTimeOffset Luc = DuAnMau.Luc;

        [Fact]
        public void Tran_nguoi_lam_la_so_suat_nhan_he_so_va_khong_duoi_so_suat()
        {
            Assert.Equal(10, CollectionRound.Mo(Guid.NewGuid(), 5, 2, Luc).MaxParticipants);
            Assert.Equal(5, CollectionRound.Mo(Guid.NewGuid(), 5, 0, Luc).MaxParticipants);
            Assert.Throws<ArgumentOutOfRangeException>(() => CollectionRound.Mo(Guid.NewGuid(), 0, 2, Luc));
        }

        [Fact]
        public void Nhan_bai_khi_du_an_dang_chay_va_chua_dong_thu_bai()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, p.Slots, 2, Luc);

            Assert.True(CollectionRound.ConNhanBai(p, d, Luc.AddDays(1)));
            Assert.False(CollectionRound.ConNhanBai(p, d, Luc.AddDays(10)));
            Assert.False(CollectionRound.ConNhanBai(p, null, Luc.AddDays(1)));

            p.TamDung(Luc.AddHours(1));
            Assert.False(CollectionRound.ConNhanBai(p, d, Luc.AddDays(1)));
        }

        [Fact]
        public void Dong_thu_bai_mot_lan_roi_khong_nhan_bai_nua()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, p.Slots, 2, Luc);

            Assert.True(d.DongThuBai("du_suat"));
            Assert.False(d.DongThuBai("het_han"));
            Assert.Equal("du_suat", d.FinishReason);
            Assert.False(CollectionRound.ConNhanBai(p, d, Luc.AddDays(1)));
            Assert.Equal("finished", d.GiaiDoan());
        }

        [Fact]
        public void Cham_mo_han_khieu_nai_va_chot_mot_lan()
        {
            CollectionRound d = CollectionRound.Mo(Guid.NewGuid(), 5, 2, Luc);
            d.DongThuBai("du_suat");

            // Chua cham thi chua chot duoc.
            Assert.False(d.Chot(3, Luc.AddDays(2)));

            Assert.True(d.GhiDaCham(Luc.AddDays(1), TimeSpan.FromDays(3)));
            Assert.False(d.GhiDaCham(Luc.AddDays(2), TimeSpan.FromDays(3)));
            Assert.Equal(Luc.AddDays(4), d.AppealDeadline);
            Assert.Equal("graded", d.GiaiDoan());

            Assert.True(d.Chot(3, Luc.AddDays(4)));
            Assert.False(d.Chot(4, Luc.AddDays(5)));
            Assert.Equal(3, d.PassedCount);
            Assert.Equal("settled", d.GiaiDoan());
        }

        [Fact]
        public void Tiep_tuc_tra_ve_thoi_gian_da_tam_dung()
        {
            CollectionRound d = CollectionRound.Mo(Guid.NewGuid(), 5, 2, Luc);
            d.GhiTamDung(Luc.AddHours(1));

            Assert.Equal(TimeSpan.FromHours(3), d.TiepTuc(Luc.AddHours(4)));
            Assert.Null(d.PausedAt);

            // Chua tam dung (hoac goi lai) → khong cong bu.
            Assert.Equal(TimeSpan.Zero, d.TiepTuc(Luc.AddHours(5)));
        }

        [Fact]
        public void Du_an_dang_chay_khong_duoc_chup_tien_do_cuoi()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, p.Slots, 2, Luc);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => d.ChupTienDoCuoi(p, "{}"));
            Assert.Equal("du_an_chua_dong", ex.Code);
        }

        [Fact]
        public void Du_an_da_dong_chup_mot_lan_va_danh_dau_da_don()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, p.Slots, 2, Luc);
            Assert.Throws<InvalidOperationException>(() => d.DanhDauDaDon(Luc));

            p.TamDung(Luc.AddHours(1));
            p.Huy("thu", Luc.AddHours(2));
            d.ChupTienDoCuoi(p, "{\"lan\":1}");
            d.ChupTienDoCuoi(p, "{\"lan\":2}");
            Assert.Contains("1", d.FinalProgressJson);

            d.DanhDauDaDon(Luc.AddDays(1));
            d.DanhDauDaDon(Luc.AddDays(2));
            Assert.Equal(Luc.AddDays(1), d.DataPurgedAt);
        }

        [Fact]
        public void Ban_chup_tien_do_doc_lai_duoc_nguyen_ven()
        {
            ProgressResponse goc = new ProgressResponse
            {
                ProjectId = Guid.NewGuid(),
                Slots = 5,
                MaxParticipants = 10,
                QuestionCount = 20,
                InProgress = 0,
                Submitted = 7,
                Expired = 1,
                Cancelled = 2,
                Phase = "settled",
                FinishReason = "het_han",
                Deadline = Luc.AddDays(10),
                CollectionFinishedAt = Luc.AddDays(10),
                GradedAt = Luc.AddDays(10),
                AppealDeadline = Luc.AddDays(13),
                SettledAt = Luc.AddDays(13),
                PassedCount = 4,
                OpenAppeals = 0,
                EstimatedCompletionAt = null,
                FrozenAt = Luc.AddDays(100),
            };

            ProgressResponse? lai = JsonSerializer.Deserialize<ProgressResponse>(JsonSerializer.Serialize(goc, JsonSerializerOptions.Web), JsonSerializerOptions.Web);
            Assert.NotNull(lai);
            Assert.Equal(7, lai!.Submitted);
            Assert.Equal(4, lai.PassedCount);
            Assert.Equal(Luc.AddDays(100), lai.FrozenAt);
        }

        [Fact]
        public void Uoc_tinh_xong_theo_toc_do_24_gio()
        {
            Assert.Equal(Luc, LamBaiService.UocTinhXong(0, 5, Luc));
            Assert.Null(LamBaiService.UocTinhXong(4, 0, Luc));
            Assert.Equal(Luc.AddHours(48), LamBaiService.UocTinhXong(4, 2, Luc));
        }
    }

    /// <summary>Dieu kien lam bai: bang goc (du an, dot, thanh vien) + ho so identity (hoi qua gRPC luc mo bai, 3.24).</summary>
    public sealed class EligibilityTests
    {
        private static readonly DateTimeOffset Luc = DuAnMau.Luc;
        private static readonly Guid UserId = Guid.NewGuid();

        private static ProjectMember Labeler(LabelingProject p)
        {
            return ProjectMember.TaoThanhVien(p.Id, UserId, MemberRole.Labeler, Luc);
        }

        /// <summary>Ho so identity: labeler, khong khoa, cap 1, chua co uy tin (nguoi moi).</summary>
        private static readonly HoSoNguoiDung NguoiMoi = new HoSoNguoiDung(UserId, true, false, 1, null);

        private static string? LyDo(LabelingProject p, CollectionRound? d, ProjectMember? m, HoSoNguoiDung? h, DateTimeOffset luc)
        {
            return EligibilityPolicy.LyDoTuChoi(p, d, m, h ?? NguoiMoi, luc);
        }

        [Fact]
        public void Du_dieu_kien_thi_null()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            Assert.Null(LyDo(p, CollectionRound.Mo(p.Id, 5, 2, Luc), Labeler(p), null, Luc));
        }

        [Fact]
        public void Chua_duyet_hoac_khong_phai_thanh_vien_thi_tu_choi()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, 5, 2, Luc);

            Assert.Equal("du_an_chua_san_sang", LyDo(p, null, Labeler(p), null, Luc));
            Assert.Equal("khong_phai_thanh_vien", LyDo(p, d, null, null, Luc));
        }

        [Fact]
        public void Cap_cua_identity_duoi_nguong_thi_tu_choi()
        {
            LabelingProject cap2 = DuAnMau.DangChay(2, null);
            Assert.Equal("chua_du_cap_do", LyDo(cap2, CollectionRound.Mo(cap2.Id, 5, 2, Luc), Labeler(cap2), null, Luc));

            // Nguoi moi (identity tinh cap 1): du an chi doi cap 1 thi khong chan.
            LabelingProject cap1 = DuAnMau.DangChay(1, null);
            Assert.Null(LyDo(cap1, CollectionRound.Mo(cap1.Id, 5, 2, Luc), Labeler(cap1), null, Luc));
        }

        [Fact]
        public void Bi_chan_khoi_du_an_hoac_bi_khoa_tai_khoan()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, 5, 2, Luc);

            ProjectMember biChan = Labeler(p);
            biChan.Chan(Luc);
            Assert.Equal("bi_chan_khoi_du_an", LyDo(p, d, biChan, null, Luc));

            HoSoNguoiDung khoa = new HoSoNguoiDung(UserId, true, true, 1, null);
            Assert.Equal("tai_khoan_bi_khoa", LyDo(p, d, Labeler(p), khoa, Luc));
        }

        [Fact]
        public void Loc_theo_cap_do_va_uy_tin()
        {
            HoSoNguoiDung l = new HoSoNguoiDung(UserId, true, false, 3, 70);

            LabelingProject vua = DuAnMau.DangChay(3, 70);
            Assert.Null(LyDo(vua, CollectionRound.Mo(vua.Id, 5, 2, Luc), Labeler(vua), l, Luc));

            LabelingProject cao = DuAnMau.DangChay(3, 71);
            Assert.Equal("chua_du_uy_tin", LyDo(cao, CollectionRound.Mo(cao.Id, 5, 2, Luc), Labeler(cao), l, Luc));
        }

        [Fact]
        public void Qua_deadline_tam_dung_hoac_da_dong_thu_bai_thi_khong_cho_lam()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            CollectionRound d = CollectionRound.Mo(p.Id, 5, 2, Luc);
            Assert.Equal("da_qua_deadline", LyDo(p, d, Labeler(p), null, Luc.AddDays(11)));

            d.DongThuBai("du_suat");
            Assert.Equal("du_an_da_dong_thu_bai", LyDo(p, d, Labeler(p), null, Luc));

            LabelingProject dung = DuAnMau.DangChay(null, null);
            dung.TamDung(Luc.AddMinutes(1));
            Assert.Equal("du_an_khong_chay", LyDo(dung, CollectionRound.Mo(dung.Id, 5, 2, Luc), Labeler(dung), null, Luc));
        }

        [Fact]
        public void Chu_du_an_khong_lam_bai_cua_chinh_minh()
        {
            LabelingProject p = DuAnMau.DangChay(null, null);
            ProjectMember chu = ProjectMember.TaoChuSoHuu(p.Id, p.OwnerId, Luc);
            Assert.Equal("khong_phai_labeler", LyDo(p, CollectionRound.Mo(p.Id, 5, 2, Luc), chu, null, Luc));
        }

        [Fact]
        public void Moi_ly_do_tu_choi_co_cau_giai_thich_rieng()
        {
            LabelingProject p = DuAnMau.DangChay(3, 70);
            CollectionRound d = CollectionRound.Mo(p.Id, 5, 2, Luc);

            Assert.Contains("thành viên", EligibilityPolicy.GiaiThich(LyDo(p, d, null, null, Luc)!, p, NguoiMoi));
            Assert.Contains("hạn chót", EligibilityPolicy.GiaiThich(LyDo(p, d, Labeler(p), null, Luc.AddDays(11))!, p, NguoiMoi));
            Assert.Equal("Dự án yêu cầu cấp độ tối thiểu 3, hiện tại là 1.",
                EligibilityPolicy.GiaiThich(LyDo(p, d, Labeler(p), null, Luc)!, p, NguoiMoi));

            LabelingProject dung = DuAnMau.DangChay(null, null);
            dung.TamDung(Luc.AddMinutes(1));
            Assert.Equal("Dự án đang tạm dừng.", EligibilityPolicy.GiaiThich("du_an_khong_chay", dung, NguoiMoi));

            d.DongThuBai("du_suat");
            Assert.Contains("ngừng nhận bài", EligibilityPolicy.GiaiThich(LyDo(p, d, Labeler(p), null, Luc)!, p, NguoiMoi));
        }
    }

    /// <summary>Bao loi mau (FL-07), loai mau.</summary>
    public sealed class BaoLoiVaLoaiMauTests
    {
        private static readonly DateTimeOffset Luc = DuAnMau.Luc;

        [Theory]
        [InlineData("broken", null)]
        [InlineData("INAPPROPRIATE", null)]
        [InlineData("unclear", "mo ta mo ho")]
        [InlineData("other", "anh bi trung")]
        public void Ly_do_bao_loi_hop_le(string lyDo, string? ghiChu)
        {
            SampleReport r = SampleReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lyDo, ghiChu, Luc);
            Assert.Equal(lyDo.ToLowerInvariant(), r.Reason);
            Assert.Equal(ReportState.Open, r.State);
        }

        [Fact]
        public void Ly_do_khac_phai_kem_ghi_chu_va_ly_do_la_bi_tu_choi()
        {
            Assert.Equal("thieu_ghi_chu", Assert.Throws<InvalidValueException>(() =>
                SampleReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "other", "  ", Luc)).Code);
            Assert.Equal("ly_do_khong_hop_le", Assert.Throws<InvalidValueException>(() =>
                SampleReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "spam", null, Luc)).Code);
        }

        [Fact]
        public void Xu_ly_bao_loi_mot_lan()
        {
            SampleReport r = SampleReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "broken", null, Luc);
            r.XuLy(ReportState.Dismissed, Guid.NewGuid(), Luc.AddHours(1));
            r.XuLy(ReportState.Excluded, null, Luc.AddHours(2));
            Assert.Equal(ReportState.Dismissed, r.State);
            Assert.Throws<ArgumentOutOfRangeException>(() => r.XuLy(ReportState.Open, null, Luc));
        }

        [Fact]
        public void Loai_mau_mot_lan_va_phai_co_ly_do()
        {
            Sample s = Sample.TaoTuNoiDung(
                Guid.NewGuid(), Modalities.Text, RawJson.Tu("{\"text\":\"x\"}"), "dong 1", "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);

            Assert.Throws<ArgumentException>(() => s.LoaiBo(" ", Luc));
            Assert.True(s.LoaiBo(BaoLoiService.LoaiDoChuDuAn, Luc));
            Assert.False(s.LoaiBo(BaoLoiService.LoaiDoNhieuNguoiBao, Luc.AddHours(1)));
            Assert.Equal(BaoLoiService.LoaiDoChuDuAn, s.ExcludedReason);
            Assert.Equal(Luc, s.ExcludedAt);
        }
    }
}
