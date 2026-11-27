using System;
using Crowd.Labeling;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Exports;
using Xunit;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Viec chay nen cua doanh nghiep (kien truc 3.41): phieu giai nen ZIP anh va lan tao file ket qua. Ca hai khong con
    /// lam trong request — vong doi nam tren dong cua chinh no.
    /// </summary>
    public sealed class ZipImportTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

        private static ZipImport Phieu(Guid duAn)
        {
            return ZipImport.Tao(duAn, ZipImport.TienToKhoa(duAn) + Guid.NewGuid() + ".zip", "anh.zip", 1024, Luc, QuyDinhMau.DuLieu);
        }

        [Fact]
        public void Phieu_moi_o_hang_doi()
        {
            ZipImport z = Phieu(Guid.NewGuid());

            Assert.Equal(TrangThaiZip.ChoXuLy, z.State);
            Assert.True(z.DangCho());
            Assert.Equal("anh.zip", z.OriginalName);
            Assert.Equal(0, z.ImageCount);
        }

        [Fact]
        public void Khoa_cua_du_an_khac_hoac_khong_phai_zip_bi_tu_choi()
        {
            Guid duAn = Guid.NewGuid();
            Guid khac = Guid.NewGuid();

            Assert.Equal("khoa_zip_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => ZipImport.Tao(duAn, ZipImport.TienToKhoa(khac) + "a.zip", "a.zip", 10, Luc, QuyDinhMau.DuLieu)).Code);
            Assert.Equal("khoa_zip_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => ZipImport.Tao(duAn, duAn + "/raw/a.png", "a.png", 10, Luc, QuyDinhMau.DuLieu)).Code);
            Assert.Equal("khoa_zip_khong_hop_le", Assert.Throws<InvalidValueException>(
                () => ZipImport.Tao(duAn, ZipImport.TienToKhoa(duAn) + "../x.zip", "x.zip", 10, Luc, QuyDinhMau.DuLieu)).Code);
            Assert.Equal("zip_rong", Assert.Throws<InvalidValueException>(
                () => ZipImport.Tao(duAn, ZipImport.TienToKhoa(duAn) + "a.zip", "a.zip", 0, Luc, QuyDinhMau.DuLieu)).Code);
        }

        [Fact]
        public void Giai_nen_xong_ghi_so_anh_va_so_bo_qua()
        {
            ZipImport z = Phieu(Guid.NewGuid());

            z.BatDau(Luc);
            Assert.Equal(TrangThaiZip.DangXuLy, z.State);

            z.HoanTat(12, 3, Luc.AddMinutes(1));

            Assert.Equal(TrangThaiZip.Xong, z.State);
            Assert.False(z.DangCho());
            Assert.Equal(12, z.ImageCount);
            Assert.Equal(3, z.SkippedCount);
            Assert.Equal(15, z.ProcessedCount);
            Assert.Equal(Luc.AddMinutes(1), z.FinishedAt);
        }

        [Fact]
        public void Khong_nhan_viec_hai_lan_va_khong_chot_khi_chua_nhan()
        {
            ZipImport z = Phieu(Guid.NewGuid());

            Assert.Throws<RuleViolationException>(() => z.HoanTat(1, 0, Luc));
            z.BatDau(Luc);
            Assert.Throws<RuleViolationException>(() => z.BatDau(Luc));
        }

        [Fact]
        public void Service_tat_giua_chung_thi_phieu_ve_hang_doi_lam_lai_tu_dau()
        {
            ZipImport z = Phieu(Guid.NewGuid());
            z.BatDau(Luc);

            z.VeHangDoi();
            Assert.Equal(TrangThaiZip.ChoXuLy, z.State);

            z.BatDau(Luc.AddSeconds(5));
            Assert.Equal(0, z.ProcessedCount);
        }

        [Fact]
        public void Loi_ghi_ly_do_cat_theo_do_dai()
        {
            ZipImport z = Phieu(Guid.NewGuid());
            z.BatDau(Luc);

            z.Loi(new string('x', 5000), 100, Luc);

            Assert.Equal(TrangThaiZip.Loi, z.State);
            Assert.Equal(100, z.Error!.Length);
        }

        [Fact]
        public void Ma_mau_cua_anh_co_dinh_theo_phieu_va_vi_tri()
        {
            ZipImport z = Phieu(Guid.NewGuid());
            ZipImport khac = Phieu(Guid.NewGuid());

            // Lam lai phieu (khoi dong lai) cho dung ma cu → anh ghi de dung khoa cu, khong de file mo coi.
            Assert.Equal(z.MaMauCuaAnh(0), z.MaMauCuaAnh(0));
            Assert.NotEqual(z.MaMauCuaAnh(0), z.MaMauCuaAnh(1));
            Assert.NotEqual(z.MaMauCuaAnh(0), khac.MaMauCuaAnh(0));
            Assert.Equal(8, z.MaMauCuaAnh(7).Version);
        }

        [Fact]
        public void Anh_trong_zip_dung_ma_cho_truoc_lam_khoa_file()
        {
            Guid duAn = Guid.NewGuid();
            ZipImport z = Phieu(duAn);
            Guid ma = z.MaMauCuaAnh(3);

            Sample s = Sample.TaoAnhTrongZip(ma, duAn, "a.png", "image/png", ".png", 5, "abc", SampleMetadata.Rong, Luc, QuyDinhMau.DuLieu);

            Assert.Equal(ma, s.Id);
            Assert.Equal(duAn + "/" + ma + ".png", s.StorageKey);
            Assert.Equal(TrangThaiNap.SanSang, s.IngestState);
        }
    }

    public sealed class ExportJobTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Lan_tao_moi_o_hang_doi_va_chua_co_file()
        {
            ExportJob j = ExportJob.Tao(Guid.NewGuid(), "yolo", Guid.NewGuid(), Luc);

            Assert.Equal(TrangThaiXuat.ChoXuLy, j.State);
            Assert.True(j.DangCho());
            Assert.Null(j.StorageKey);
        }

        [Fact]
        public void Tao_xong_ghi_file_va_dau_van_tay()
        {
            Guid duAn = Guid.NewGuid();
            ExportJob j = ExportJob.Tao(duAn, "yolo", null, Luc);
            string khoa = j.KhoaFileMoi(".yolo.zip");

            j.BatDau(Luc);
            j.HoanTat(khoa, "ket-qua.yolo.zip", "application/zip", 2048, "abc", 7, Luc.AddSeconds(3));

            Assert.Equal(TrangThaiXuat.SanSang, j.State);
            Assert.Equal(duAn + "/exports/" + j.Id + ".yolo.zip", j.StorageKey);
            Assert.Equal(2048, j.SizeBytes);
            Assert.Equal(7, j.SampleCount);
            Assert.Equal("abc", j.ResultFingerprint);
            Assert.True(j.DaXong());
        }

        [Fact]
        public void File_phai_nam_trong_thu_muc_du_an()
        {
            ExportJob j = ExportJob.Tao(Guid.NewGuid(), "csv", null, Luc);
            j.BatDau(Luc);

            Assert.Throws<ArgumentException>(() => j.HoanTat(Guid.NewGuid() + "/exports/x.csv", "x.csv", "text/csv", 1, "a", 0, Luc));
        }

        [Fact]
        public void Giao_lai_khi_dang_tao_thi_lam_tiep_da_xong_thi_tu_choi()
        {
            ExportJob j = ExportJob.Tao(Guid.NewGuid(), "csv", null, Luc);
            j.BatDau(Luc);
            j.BatDau(Luc.AddSeconds(10));
            Assert.Equal(Luc, j.StartedAt);

            j.Loi("Dự án chưa có tập nhãn.", Luc.AddSeconds(11));
            Assert.Equal(TrangThaiXuat.Loi, j.State);
            Assert.Throws<RuleViolationException>(() => j.BatDau(Luc.AddSeconds(12)));

            // Loi lan nua (giao lai) khong ghi de ly do cu.
            j.Loi("khac", Luc.AddSeconds(13));
            Assert.Equal("Dự án chưa có tập nhãn.", j.Error);
        }

        [Fact]
        public void Dang_cho_qua_han_thi_bao_failed_khong_doi_dong()
        {
            ExportJob j = ExportJob.Tao(Guid.NewGuid(), "json", null, Luc);
            TimeSpan han = TimeSpan.FromMinutes(30);

            Assert.Equal(TrangThaiXuat.ChoXuLy, j.TrangThaiHienThi(Luc.AddMinutes(29), han));
            Assert.Equal(TrangThaiXuat.Loi, j.TrangThaiHienThi(Luc.AddMinutes(31), han));
            Assert.Equal(TrangThaiXuat.ChoXuLy, j.State);
        }
    }

    public sealed class XuatKetQuaTests
    {
        private static readonly LabelSchema VanBan = LabelSchema.Doc(
            "{\"modality\":\"text\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}");

        private static readonly LabelSchema AnhKhung = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":[{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\",\"nguoi\"]}]}");

        [Fact]
        public void Dinh_dang_chuan_hoa_chu_thuong_bo_trong_la_json()
        {
            Assert.Equal("json", XuatKetQua.ChuanHoa(null));
            Assert.Equal("json", XuatKetQua.ChuanHoa("  "));
            Assert.Equal("yolo-seg", XuatKetQua.ChuanHoa(" YOLO-Seg "));
            Assert.Equal("dinh_dang_chua_ho_tro", Assert.Throws<InvalidValueException>(() => XuatKetQua.ChuanHoa("xml")).Code);
        }

        [Fact]
        public void Dinh_dang_khong_hop_du_an_bi_tu_choi_ngay_luc_yeu_cau()
        {
            Assert.Equal("coco_chi_cho_khung_anh", Assert.Throws<InvalidValueException>(() => XuatKetQua.KiemHopDuAn(VanBan, "coco")).Code);
            Assert.Equal("khung_chi_cho_anh", Assert.Throws<InvalidValueException>(() => XuatKetQua.KiemHopDuAn(VanBan, "yolo")).Code);
            Assert.Equal("dinh_dang_khong_hop_du_an", Assert.Throws<InvalidValueException>(() => XuatKetQua.KiemHopDuAn(AnhKhung, "conll")).Code);
        }

        [Fact]
        public void Dinh_dang_hop_du_an_qua_kiem()
        {
            XuatKetQua.KiemHopDuAn(VanBan, "json");
            XuatKetQua.KiemHopDuAn(VanBan, "csv");
            XuatKetQua.KiemHopDuAn(AnhKhung, "coco");
            XuatKetQua.KiemHopDuAn(AnhKhung, "yolo");
            XuatKetQua.KiemHopDuAn(AnhKhung, "voc");
        }

        [Fact]
        public void Duoi_file_lay_tu_ten_goi_y()
        {
            Guid duAn = Guid.NewGuid();

            Assert.Equal(".yolo.zip", XuatKetQua.Duoi("ket-qua-" + duAn + ".yolo.zip", duAn));
            Assert.Equal(".csv", XuatKetQua.Duoi("ket-qua-" + duAn + ".csv", duAn));
        }
    }
}
