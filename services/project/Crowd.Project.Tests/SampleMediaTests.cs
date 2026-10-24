using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Datasets;
using Xunit;

namespace Crowd.Project.Tests
{
    public sealed class SampleMediaTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

        private static Sample File(string modality, Guid duAn)
        {
            return Sample.TaoTuFile(
                duAn, Guid.NewGuid(), modality, duAn + "/a.bin", "a", "application/octet-stream", 10, "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);
        }

        [Fact]
        public void Video_moi_nap_dang_chuan_bi_cac_loai_khac_khong_can()
        {
            Guid duAn = Guid.NewGuid();
            Assert.Equal(TrangThaiMedia.DangChuanBi, File(Modalities.Video, duAn).MediaState);
            Assert.Null(File(Modalities.Audio, duAn).MediaState);
            Assert.Null(File(Modalities.Image, duAn).MediaState);
        }

        [Fact]
        public void Ghi_ket_qua_media_thanh_cong_xoa_loi_that_bai_giu_ly_do()
        {
            Sample s = File(Modalities.Video, Guid.NewGuid());

            s.GhiKetQuaMedia(false, new string('x', 900));
            Assert.Equal(TrangThaiMedia.Loi, s.MediaState);
            Assert.Equal(500, s.MediaError!.Length);

            s.GhiKetQuaMedia(true, null);
            Assert.Equal(TrangThaiMedia.SanSang, s.MediaState);
            Assert.Null(s.MediaError);
        }
    }
}
