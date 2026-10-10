using System;
using Crowd.Annotation.Domain.Common;
using Crowd.Annotation.Domain.Projects;

namespace Crowd.Annotation.Tests
{
    /// <summary>Dong so du an (phuong an A): dong / mo lai / ket thuc, va event den sai thu tu.</summary>
    public sealed class ProjectTermsTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

        private static ProjectTerms DieuKhoan()
        {
            return ProjectTerms.Tao(Guid.NewGuid(), Guid.NewGuid(), 1000, 300, ResultAggregatorTests.TapNhan);
        }

        [Fact]
        public void Moi_tao_thi_dang_mo()
        {
            ProjectTerms t = DieuKhoan();

            Assert.False(t.DaDong);
            t.KiemConMo();
        }

        [Fact]
        public void Dong_so_thi_moi_thao_tac_lam_doi_tien_bi_409()
        {
            ProjectTerms t = DieuKhoan();
            t.DongSo(T);

            Assert.True(t.DaDong);
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => t.KiemConMo());
            Assert.Equal("du_an_da_ket_thuc", ex.Code);
        }

        [Fact]
        public void Dong_so_lai_giu_moc_cu()
        {
            ProjectTerms t = DieuKhoan();
            t.DongSo(T);
            t.DongSo(T.AddMinutes(5));

            Assert.Equal(T, t.ClosedAt);
        }

        [Fact]
        public void Project_resumed_moi_hon_luc_dong_thi_mo_lai()
        {
            ProjectTerms t = DieuKhoan();
            t.DongSo(T);

            Assert.True(t.MoLai(T.AddSeconds(30)));
            Assert.False(t.DaDong);
        }

        [Fact]
        public void Project_resumed_cu_giao_tre_khong_mo_lai()
        {
            ProjectTerms t = DieuKhoan();
            t.DongSo(T);

            Assert.False(t.MoLai(T.AddSeconds(-30)));
            Assert.False(t.MoLai(T));
            Assert.True(t.DaDong);
        }

        [Fact]
        public void Da_ket_thuc_thi_khong_event_nao_mo_lai()
        {
            ProjectTerms t = DieuKhoan();
            t.DongSo(T);
            t.KetThuc(T.AddSeconds(1));

            Assert.True(t.IsFinal);
            Assert.False(t.MoLai(T.AddDays(1)));
            Assert.True(t.DaDong);
        }

        [Fact]
        public void Ket_thuc_khi_chua_dong_so_cung_dong_luon()
        {
            ProjectTerms t = DieuKhoan();
            t.KetThuc(T);

            Assert.True(t.DaDong);
            Assert.True(t.IsFinal);
        }
    }
}
