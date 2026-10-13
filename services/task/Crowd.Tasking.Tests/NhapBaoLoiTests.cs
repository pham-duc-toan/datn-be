using System;
using Crowd.Labeling;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Reports;
using Crowd.Tasking.Domain.Tasks;

namespace Crowd.Tasking.Tests
{
    public sealed class NhapBaoLoiTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        private static readonly RawJson KhongMetadata = SampleMetadata.Rong.ToRawJson();

        private static LabelingTask TaskMoi()
        {
            return LabelingTask.Tao(Guid.NewGuid(), Guid.NewGuid(), Modalities.Image, "k.png", null, KhongMetadata, 3, Luc);
        }

        [Fact]
        public void Luu_nhap_khi_con_giu_task()
        {
            Assignment a = Assignment.Tao(TaskMoi(), Guid.NewGuid(), Luc, TimeSpan.FromMinutes(15));

            a.LuuNhap("{\"loai\":{\"labelIds\":[\"a\"]}}", Luc.AddMinutes(1));

            Assert.Equal("{\"loai\":{\"labelIds\":[\"a\"]}}", a.DraftJson);
            Assert.Equal(Luc.AddMinutes(1), a.DraftSavedAt);
        }

        [Fact]
        public void Het_lease_thi_khong_luu_nhap_duoc()
        {
            Assignment a = Assignment.Tao(TaskMoi(), Guid.NewGuid(), Luc, TimeSpan.FromMinutes(15));

            Assert.Equal("lease_khong_con", Assert.Throws<RuleViolationException>(() => a.LuuNhap("{}", Luc.AddMinutes(16))).Code);
        }

        [Fact]
        public void Huy_task_chi_khi_khong_ai_giu()
        {
            LabelingTask t = TaskMoi();
            Assignment a = Assignment.Tao(t, Guid.NewGuid(), Luc, TimeSpan.FromMinutes(15));

            Assert.False(t.HuyNeuKhongAiGiu(Luc));
            a.BoQua(t, Luc);
            Assert.True(t.HuyNeuKhongAiGiu(Luc));
            Assert.Equal(TaskState.Cancelled, t.State);
        }

        [Theory]
        [InlineData("broken", null)]
        [InlineData("UNCLEAR", null)]
        [InlineData("other", "Anh bi den")]
        public void Ly_do_bao_loi_hop_le(string lyDo, string? ghiChu)
        {
            TaskReport r = TaskReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), lyDo, ghiChu, Luc);

            Assert.Equal(lyDo.ToLowerInvariant(), r.Reason);
            Assert.Equal(ReportState.Open, r.State);
        }

        [Fact]
        public void Ly_do_khac_phai_kem_ghi_chu_va_ly_do_la_bi_tu_choi()
        {
            Assert.Equal("thieu_ghi_chu", Assert.Throws<InvalidValueException>(
                () => TaskReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "other", " ", Luc)).Code);
            Assert.Equal("ly_do_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => TaskReport.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "spam", null, Luc)).Code);
        }

        [Fact]
        public void Mo_khoa_den_truoc_khoa_thi_khoa_cu_bi_bo_qua()
        {
            LabelerProfile p = LabelerProfile.Tao(Guid.NewGuid());

            Assert.True(p.MoKhoa(Luc.AddMinutes(5)));
            Assert.False(p.Khoa(Luc));
            Assert.False(p.Blocked);
        }

        [Fact]
        public void Uoc_tinh_xong_theo_toc_do_24_gio()
        {
            Assert.Equal(Luc.AddHours(12), LeaseService.UocTinhXong(50, 100, Luc));
            Assert.Equal(Luc, LeaseService.UocTinhXong(0, 0, Luc));
            Assert.Null(LeaseService.UocTinhXong(10, 0, Luc));
        }
    }
}
