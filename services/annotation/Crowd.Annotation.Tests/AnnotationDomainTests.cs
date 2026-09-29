using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Common;

namespace Crowd.Annotation.Tests
{
    public sealed class ReviewTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Labeler = Guid.NewGuid();
        private static readonly Guid Reviewer = Guid.NewGuid();
        private static readonly Guid Admin = Guid.NewGuid();
        private static readonly string[] NopRoiDuyet = new string[] { "submitted", "approved" };

        private static LabelAnnotation NhanMoi()
        {
            return LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "k.png", Labeler, new List<string> { "cho" }, Luc);
        }

        private static LabelAnnotation NhanBiTuChoi()
        {
            LabelAnnotation a = NhanMoi();
            a.TuChoi(Reviewer, "Sai lop", Luc.AddHours(1));
            return a;
        }

        [Fact]
        public void Duyet_ghi_nhat_ky()
        {
            LabelAnnotation a = NhanMoi();
            a.Duyet(Reviewer, Luc.AddHours(1));

            Assert.Equal(AnnotationStatus.Approved, a.Status);
            Assert.Equal(NopRoiDuyet, a.History.Select(h => h.Action).ToArray());
        }

        [Fact]
        public void Khong_duyet_hai_lan_nen_khong_chi_tien_hai_lan()
        {
            LabelAnnotation a = NhanMoi();
            a.Duyet(Reviewer, Luc);

            Assert.Throws<RuleViolationException>(() => a.Duyet(Reviewer, Luc));
            Assert.Throws<RuleViolationException>(() => a.TuChoi(Reviewer, "x", Luc));
        }

        [Fact]
        public void Tu_choi_bat_buoc_co_ly_do_FB_21()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => NhanMoi().TuChoi(Reviewer, "  ", Luc));
            Assert.Equal("thieu_ly_do", ex.Code);
        }

        [Fact]
        public void Khong_tu_duyet_nhan_cua_chinh_minh()
        {
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => NhanMoi().Duyet(Labeler, Luc));
            Assert.Equal("tu_duyet", ex.Code);
        }

        [Fact]
        public void Khieu_nai_mot_lan_trong_7_ngay_boi_chinh_labeler()
        {
            Assert.Throws<RuleViolationException>(() => NhanBiTuChoi().KhieuNai(Guid.NewGuid(), "Toi dung", Luc.AddDays(1)));

            RuleViolationException tre = Assert.Throws<RuleViolationException>(() =>
                NhanBiTuChoi().KhieuNai(Labeler, "Toi dung", Luc.AddHours(1) + LabelAnnotation.HanKhieuNai + TimeSpan.FromSeconds(1)));
            Assert.Equal("qua_han_khieu_nai", tre.Code);

            Assert.Throws<RuleViolationException>(() => NhanMoi().KhieuNai(Labeler, "x", Luc));

            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1));
            Assert.Equal(AnnotationStatus.Appealed, a.Status);
        }

        [Fact]
        public void Admin_chap_nhan_khieu_nai_thi_thanh_Approved()
        {
            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1));

            Assert.True(a.XuLyKhieuNai(Admin, true, "Dong y", Luc.AddDays(2)));
            Assert.Equal(AnnotationStatus.Approved, a.Status);
        }

        [Fact]
        public void Admin_bac_thi_tu_choi_cuoi_cung_khong_khieu_nai_lai_duoc()
        {
            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1));

            Assert.False(a.XuLyKhieuNai(Admin, false, "Giu nguyen", Luc.AddDays(2)));
            Assert.True(a.LaTuChoiCuoiCung());
            Assert.False(a.ConKhieuNaiDuoc(Luc.AddDays(2)));

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.KhieuNai(Labeler, "lan nua", Luc.AddDays(2)));
            Assert.Equal("da_khieu_nai", ex.Code);
        }
    }

    public sealed class ResultAggregatorTests
    {
        private static readonly Guid Mau = Guid.NewGuid();
        private static readonly string[] ChiCho = new string[] { "cho" };

        private static LabelAnnotation Nhan(params string[] labels)
        {
            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Mau, "k", Guid.NewGuid(), labels, DateTimeOffset.UtcNow);
            a.Duyet(Guid.NewGuid(), DateTimeOffset.UtcNow);
            return a;
        }

        [Fact]
        public void Da_so_tuyet_doi_thang()
        {
            SampleResult r = ResultAggregator.Chot(new[] { Nhan("cho"), Nhan("cho"), Nhan("meo") }).Single();

            Assert.Equal(ChiCho, r.FinalLabels.ToArray());
            Assert.Equal(2, r.Votes["cho"]);
            Assert.False(r.Disputed);
        }

        [Fact]
        public void Hoa_phieu_la_tranh_chap_FB_22()
        {
            SampleResult r = ResultAggregator.Chot(new[] { Nhan("cho"), Nhan("meo") }).Single();

            Assert.True(r.Disputed);
            Assert.Empty(r.FinalLabels);
        }

        [Fact]
        public void Multi_label_xet_tung_lop_doc_lap()
        {
            SampleResult r = ResultAggregator.Chot(new[] { Nhan("cho", "meo"), Nhan("cho"), Nhan("cho", "ga") }).Single();

            Assert.Equal(ChiCho, r.FinalLabels.ToArray());
        }
    }
}
