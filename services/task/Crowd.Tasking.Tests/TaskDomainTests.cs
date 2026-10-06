using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Eligibility;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;

namespace Crowd.Tasking.Tests
{
    public sealed class LeaseTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan MuoiLamPhut = TimeSpan.FromMinutes(15);
        private static readonly LabelPayload Cho = LabelPayload.PhanLoai("cho");

        private static LabelingTask Task3Nguoi()
        {
            return LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), "k.png", 3, Luc);
        }

        [Fact]
        public void Khong_cap_qua_redundancy_tinh_ca_nguoi_dang_giu()
        {
            LabelingTask t = Task3Nguoi();

            Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);

            Assert.False(t.CoTheCapThem());
            Assert.Throws<RuleViolationException>(() => Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut));
        }

        [Fact]
        public void Task_chua_co_redundancy_thi_chua_cap()
        {
            LabelingTask t = LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), "k.png", 0, Luc);
            Assert.False(t.CoTheCapThem());
        }

        [Fact]
        public void Nguoi_thu_ba_nop_thi_task_hoan_thanh()
        {
            LabelingTask t = Task3Nguoi();
            Assignment a1 = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            Assignment a2 = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            Assignment a3 = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);

            Assert.False(a1.Nop(t, Cho, Luc.AddMinutes(1)));
            Assert.False(a2.Nop(t, Cho, Luc.AddMinutes(2)));
            Assert.True(a3.Nop(t, Cho, Luc.AddMinutes(3)));

            Assert.Equal(TaskState.Completed, t.State);
            Assert.Equal(3, t.SubmittedCount);
            Assert.Equal(0, t.ActiveLeaseCount);
        }

        [Fact]
        public void Nop_sau_han_bi_tu_choi_VD_T_01()
        {
            LabelingTask t = Task3Nguoi();
            Assignment a = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.Nop(t, Cho, Luc + MuoiLamPhut));
            Assert.Equal("lease_het_han", ex.Code);
        }

        [Fact]
        public void Reaper_thu_lai_roi_thi_nguoi_cu_khong_nop_duoc_va_cho_duoc_tra_ve_pool()
        {
            LabelingTask t = Task3Nguoi();
            Assignment a = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            Assert.Equal(1, t.ActiveLeaseCount);

            a.HetHan(t, Luc + MuoiLamPhut);

            Assert.Equal(AssignmentState.Expired, a.State);
            Assert.Equal(0, t.ActiveLeaseCount);
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.Nop(t, Cho, Luc.AddMinutes(1)));
            Assert.Equal("lease_khong_con", ex.Code);
        }

        [Fact]
        public void Reaper_khong_thu_lease_chua_het_han()
        {
            LabelingTask t = Task3Nguoi();
            Assignment a = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);

            Assert.Throws<RuleViolationException>(() => a.HetHan(t, Luc.AddMinutes(14)));
        }

        [Fact]
        public void Bo_qua_tra_cho_ngay_va_khong_nop_duoc_nua()
        {
            LabelingTask t = Task3Nguoi();
            Assignment a = Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);

            a.BoQua(t, Luc.AddMinutes(1));

            Assert.Equal(0, t.ActiveLeaseCount);
            Assert.True(t.CoTheCapThem());
            Assert.Throws<RuleViolationException>(() => a.Nop(t, Cho, Luc.AddMinutes(2)));
        }

        [Fact]
        public void Mau_vang_bi_loai_khoi_pool_chi_khi_chua_ai_dung_toi()
        {
            LabelingTask t = Task3Nguoi();
            t.LoaiTruVi(true);
            Assert.Equal(TaskState.Excluded, t.State);

            t.LoaiTruVi(false);
            Assert.Equal(TaskState.Open, t.State);

            Assignment.Tao(t, Guid.NewGuid(), Luc, MuoiLamPhut);
            t.LoaiTruVi(true);
            Assert.Equal(TaskState.Open, t.State);
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
            s.ApDungPublished(Guid.NewGuid(), LabelTaskTypes.ImageClassification, new List<string> { "a", "b" }, false, 1000, 3, Luc.AddDays(10), true, false, minLevel, minReputation, Luc);
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
        public void Du_an_tam_dung_hoac_qua_deadline_thi_khong_cap()
        {
            ProjectSnapshot s = DuAnChay(null, null);
            Assert.Equal("da_qua_deadline", EligibilityPolicy.LyDoTuChoi(s, Labeler(CachedMemberState.Active), null, Luc.AddDays(11)));

            s.TamDung(Luc.AddMinutes(1));
            Assert.Equal("du_an_khong_chay", EligibilityPolicy.LyDoTuChoi(s, Labeler(CachedMemberState.Active), null, Luc));
        }

        [Fact]
        public void Chu_du_an_khong_nhan_task_cua_chinh_minh()
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
            s.ApDungPublished(Guid.NewGuid(), LabelTaskTypes.ImageClassification, new List<string> { "a", "b" }, false, 1000, 3, T2.AddDays(1), true, false, null, null, T1);

            Assert.Equal(SnapshotStatus.Paused, s.Status);
            Assert.True(s.IsConfigured);
            Assert.Equal(3, s.Redundancy);
        }

        [Fact]
        public void Du_an_da_dong_khong_mo_lai_duoc()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);
            s.Dong(T1);

            Assert.False(s.TiepTuc(T2));
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
        public void Kiem_nhan_theo_tap_nhan_cua_du_an()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);
            s.ApDungPublished(Guid.NewGuid(), LabelTaskTypes.ImageClassification, new List<string> { "cho", "meo" }, false, 1000, 3, T2, true, false, null, null, T1);

            Assert.True(s.LaBoNhanHopLe(new List<string> { "cho" }));
            Assert.False(s.LaBoNhanHopLe(new List<string> { "cho", "meo" }));
            Assert.False(s.LaBoNhanHopLe(new List<string> { "voi" }));

            // Cung luat, qua dinh dang nhan chung.
            Assert.True(s.LaNhanHopLe(LabelPayload.PhanLoai("cho")));
            Assert.False(s.LaNhanHopLe(LabelPayload.PhanLoai("cho", "meo")));
            Assert.False(s.LaNhanHopLe(LabelPayload.PhanLoai("voi")));
        }

        [Fact]
        public void Du_an_chua_publish_thi_khong_nhan_nhan_nao()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), T1);

            // Chua co loai nhan → nhan nao cung sai loai.
            Assert.False(s.LaNhanHopLe(LabelPayload.PhanLoai("cho")));
        }
    }
}
