using Crowd.BuildingBlocks.Tags;
using Xunit;

namespace Crowd.BuildingBlocks.Tests.Tags
{
    public sealed class MaTheTests
    {
        [Theory]
        [InlineData("Y tế", "y-te")]
        [InlineData("  Tài chính – ngân hàng ", "tai-chinh-ngan-hang")]
        [InlineData("Đường bộ", "duong-bo")]
        [InlineData("y-te", "y-te")]
        [InlineData("AI/ML 2026", "ai-ml-2026")]
        [InlineData("---", "")]
        [InlineData(null, "")]
        public void Doi_chu_ve_ma(string? chu, string ma)
        {
            Assert.Equal(ma, MaThe.TuChu(chu));
        }

        [Fact]
        public void Ma_toi_da_50_ky_tu_khong_duoi_gach()
        {
            string ma = MaThe.TuChu(new string('a', 49) + " bcd");
            Assert.True(ma.Length <= MaThe.DoDaiToiDa);
            Assert.False(ma.EndsWith('-'));
        }
    }
}
