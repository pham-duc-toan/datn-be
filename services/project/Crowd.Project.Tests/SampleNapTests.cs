using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Xunit;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Vong doi nap mau (khong con bang "lo nap"): queued → processing → ready | failed. File upload xong moi chi la mot
    /// dong "cho xu ly"; chi khi worker doc xong moi la mau that.
    /// </summary>
    public sealed class SampleNapTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

        private static Sample ChoNap(string modality, Guid duAn)
        {
            return Sample.TaoChoNap(duAn, modality, duAn + "/raw/a.bin", "a.bin", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);
        }

        [Fact]
        public void Mau_vua_upload_o_hang_doi_chua_co_dau_van_tay()
        {
            Sample s = ChoNap(Modalities.Image, Guid.NewGuid());

            Assert.Equal(TrangThaiNap.ChoXuLy, s.IngestState);
            Assert.True(s.DangCho());
            Assert.Null(s.Sha256);
            Assert.Null(s.SizeBytes);
            Assert.Null(s.ContentType);
            Assert.Null(s.IngestError);
        }

        [Fact]
        public void Mau_tao_san_cac_cach_khac_deu_ready()
        {
            Guid duAn = Guid.NewGuid();

            Assert.Equal(TrangThaiNap.SanSang, Sample.TaoTuNoiDung(duAn, Modalities.Text, RawJson.Tu("{\"text\":\"x\"}"), "d", "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu).IngestState);
            Assert.Equal(TrangThaiNap.SanSang, Sample.TaoAnhTrongZip(duAn, "a.png", "image/png", ".png", 5, "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu).IngestState);
            Assert.Equal(TrangThaiNap.SanSang, Sample.TaoTuFile(duAn, Modalities.Audio, duAn + "/a.wav", "a", "audio/wav", 5, "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu).IngestState);
        }

        [Fact]
        public void Nhan_viec_roi_hoan_tat_thanh_ready_va_ghi_thong_tin_that()
        {
            Sample s = ChoNap(Modalities.Image, Guid.NewGuid());

            s.BatDauNap();
            Assert.Equal(TrangThaiNap.DangXuLy, s.IngestState);

            s.HoanTatNap("hash", 1234, "image/png", new SampleMetadata { Width = 320, Height = 200 });

            Assert.Equal(TrangThaiNap.SanSang, s.IngestState);
            Assert.Equal("hash", s.Sha256);
            Assert.Equal(1234, s.SizeBytes);
            Assert.Equal("image/png", s.ContentType);
            Assert.Equal(320, SampleMetadata.Tu(s.Metadata).Width);
            Assert.Null(s.MediaState);
        }

        [Fact]
        public void Video_hoan_tat_nap_con_phai_cho_media_chuan_bi()
        {
            Sample s = ChoNap(Modalities.Video, Guid.NewGuid());
            s.BatDauNap();
            s.HoanTatNap("hash", 10, "video/mp4", new SampleMetadata { DurationSec = 5 });

            Assert.Equal(TrangThaiNap.SanSang, s.IngestState);
            Assert.Equal(TrangThaiMedia.DangChuanBi, s.MediaState);
        }

        [Fact]
        public void Chi_nhan_viec_khi_dang_cho_va_chi_hoan_tat_khi_dang_xu_ly()
        {
            Sample s = ChoNap(Modalities.Image, Guid.NewGuid());

            Assert.Throws<RuleViolationException>(() => s.HoanTatNap("h", 1, "image/png", SampleMetadata.Rong));

            s.BatDauNap();
            Assert.Throws<RuleViolationException>(() => s.BatDauNap());
        }

        [Fact]
        public void File_loi_o_lai_kem_ly_do_va_bo_dau_van_tay()
        {
            Sample s = ChoNap(Modalities.Image, Guid.NewGuid());
            s.BatDauNap();
            s.NapLoi(new string('x', 5000), 4000);

            Assert.Equal(TrangThaiNap.Loi, s.IngestState);
            Assert.Equal(4000, s.IngestError!.Length);
            Assert.Null(s.Sha256);
        }

        [Fact]
        public void Khoi_dong_lai_dua_mau_dang_xu_ly_ve_hang_doi_con_mau_khac_giu_nguyen()
        {
            Sample dang = ChoNap(Modalities.Image, Guid.NewGuid());
            dang.BatDauNap();
            dang.VeHangDoi();
            Assert.Equal(TrangThaiNap.ChoXuLy, dang.IngestState);

            Sample xong = ChoNap(Modalities.Image, Guid.NewGuid());
            xong.BatDauNap();
            xong.HoanTatNap("h", 1, "image/png", SampleMetadata.Rong);
            xong.VeHangDoi();
            Assert.Equal(TrangThaiNap.SanSang, xong.IngestState);
        }

        [Fact]
        public void Khong_nhan_file_cua_du_an_khac_va_du_lieu_khong_phai_file()
        {
            Guid duAn = Guid.NewGuid();

            Assert.Throws<InvalidValueException>(() =>
                Sample.TaoChoNap(duAn, Modalities.Image, Guid.NewGuid() + "/raw/a.png", "a", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu));
            Assert.Throws<InvalidValueException>(() =>
                Sample.TaoChoNap(duAn, Modalities.Text, duAn + "/raw/a.txt", "a", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu));
        }

        [Fact]
        public void Nguon_ngoai_tu_dat_khoa_trong_du_an_va_giu_link_tai_toi_khi_xong()
        {
            Guid duAn = Guid.NewGuid();
            Sample s = Sample.TaoChoTai(duAn, Modalities.Audio, "cuoc-goi.wav", ".wav", "https://example.com/cuoc-goi.wav", Luc, QuyDinhMau.DuLieu);

            Assert.StartsWith(duAn + "/import/" + s.Id, s.StorageKey);
            Assert.EndsWith(".wav", s.StorageKey);
            Assert.Equal("https://example.com/cuoc-goi.wav", s.IngestSource);
            Assert.True(s.DangCho());

            s.BatDauNap();
            s.HoanTatNap("h", 9, "audio/wav", new SampleMetadata { DurationSec = 3 });
            Assert.Null(s.IngestSource);
        }

        [Fact]
        public void Nguon_ngoai_loi_cung_xoa_link_tai()
        {
            Sample s = Sample.TaoChoTai(Guid.NewGuid(), Modalities.Image, "a.png", ".png", "https://example.com/a.png?sig=1", Luc, QuyDinhMau.DuLieu);
            s.BatDauNap();
            s.NapLoi("khong tai duoc", 4000);

            Assert.Null(s.IngestSource);
        }

        [Fact]
        public void Doan_cat_dau_tien_doi_ten_theo_vi_tri()
        {
            Sample s = ChoNap(Modalities.Audio, Guid.NewGuid());
            s.DatTen("a.bin [0-10s]", QuyDinhMau.DuLieu);

            Assert.Equal("a.bin [0-10s]", s.OriginalName);
        }
    }
}
