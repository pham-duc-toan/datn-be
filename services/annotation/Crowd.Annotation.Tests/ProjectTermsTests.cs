using System;
using Crowd.Annotation.Domain.Projects;

namespace Crowd.Annotation.Tests
{
    /// <summary>Dieu khoan du an (docs 3.18): chi con tap nhan + moc ket thuc (khong con dong / mo so tung nhan).</summary>
    public sealed class ProjectTermsTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

        private static ProjectTerms DieuKhoan()
        {
            return ProjectTerms.Tao(Guid.NewGuid(), Guid.NewGuid(), ResultAggregatorTests.TapNhan);
        }

        [Fact]
        public void Moi_tao_thi_chua_ket_thuc()
        {
            ProjectTerms t = DieuKhoan();

            Assert.False(t.DaDong);
            Assert.False(t.IsFinal);
            Assert.Equal("image", t.Modality);
        }

        [Fact]
        public void Ket_thuc_giu_moc_dau_tien()
        {
            ProjectTerms t = DieuKhoan();
            t.KetThuc(T);
            t.KetThuc(T.AddMinutes(5));

            Assert.True(t.DaDong);
            Assert.True(t.IsFinal);
            Assert.Equal(T, t.ClosedAt);
        }
    }
}
