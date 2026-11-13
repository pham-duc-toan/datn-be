using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Xunit;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Cau hoi cua mau: cau RIENG (samples.question) neu chu du an dat, khong thi labeler thay cau hoi CHUNG cua du an
    /// (label_schema.question). Huong dan Markdown cua du an da bo.
    /// </summary>
    public sealed class CauHoiMauTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

        private static readonly LabelSchema TapNhan = LabelSchema.Doc(
            "{\"modality\":\"image\",\"question\":\"Con vat trong anh la gi?\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\",\"classes\":[\"cho\",\"meo\"]}]}");

        private static Sample Anh()
        {
            Guid duAn = Guid.NewGuid();
            return Sample.TaoChoNap(duAn, Modalities.Image, duAn + "/raw/a.png", "a.png", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);
        }

        [Fact]
        public void Khong_co_cau_rieng_thi_hien_cau_hoi_chung()
        {
            Sample s = Anh();

            Assert.Null(s.Question);
            Assert.Equal("Con vat trong anh la gi?", s.CauHoiHienThi(TapNhan));
        }

        [Fact]
        public void Cau_rieng_thay_cau_hoi_chung()
        {
            Sample s = Anh();
            s.DatCauHoi("  Co bao nhieu con meo?  ");

            Assert.Equal("Co bao nhieu con meo?", s.Question);
            Assert.Equal("Co bao nhieu con meo?", s.CauHoiHienThi(TapNhan));
        }

        [Fact]
        public void Chuoi_rong_xoa_cau_rieng_ve_lai_cau_chung()
        {
            Sample s = Anh();
            s.DatCauHoi("Co bao nhieu con meo?");
            s.DatCauHoi("   ");

            Assert.Null(s.Question);
            Assert.Equal("Con vat trong anh la gi?", s.CauHoiHienThi(TapNhan));
        }

        [Fact]
        public void Cau_rieng_qua_dai_bi_tu_choi()
        {
            Sample s = Anh();

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => s.DatCauHoi(new string('a', CotDb.CauHoiMau + 1)));
            Assert.Equal("cau_hoi_qua_dai", ex.Code);
        }

        [Fact]
        public void Du_an_chua_co_tap_nhan_va_mau_khong_co_cau_rieng_thi_khong_co_cau_hoi()
        {
            Assert.Null(Anh().CauHoiHienThi(null));
        }
    }
}
