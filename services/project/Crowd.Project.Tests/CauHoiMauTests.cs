using System;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Labeling;
using Xunit;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Ngu canh / cau hoi RIENG cua mau (samples.question): tuy chon, hien o dau cau. Cau hoi bat buoc nam o tung
    /// cong cu (label_schema.tools[].question — kien truc 3.35), khong con cau hoi chung de mau ke thua.
    /// </summary>
    public sealed class CauHoiMauTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

        private static Sample Anh()
        {
            Guid duAn = Guid.NewGuid();
            return Sample.TaoChoNap(duAn, Modalities.Image, duAn + "/raw/a.png", "a.png", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);
        }

        [Fact]
        public void Mac_dinh_mau_khong_co_cau_rieng()
        {
            Assert.Null(Anh().Question);
        }

        [Fact]
        public void Dat_cau_rieng_duoc_cat_khoang_trang()
        {
            Sample s = Anh();
            s.DatCauHoi("  Co bao nhieu con meo?  ");

            Assert.Equal("Co bao nhieu con meo?", s.Question);
        }

        [Fact]
        public void Chuoi_rong_xoa_cau_rieng()
        {
            Sample s = Anh();
            s.DatCauHoi("Co bao nhieu con meo?");
            s.DatCauHoi("   ");

            Assert.Null(s.Question);
        }

        [Fact]
        public void Cau_rieng_qua_dai_bi_tu_choi()
        {
            Sample s = Anh();

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => s.DatCauHoi(new string('a', CotDb.CauHoiMau + 1)));
            Assert.Equal("cau_hoi_qua_dai", ex.Code);
        }
    }
}
