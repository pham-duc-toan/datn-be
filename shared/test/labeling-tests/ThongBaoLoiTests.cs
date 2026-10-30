using System;

namespace Crowd.Labeling.Tests
{
    /// <summary>
    /// Thong bao loi tra cho nguoi dung: tieng Viet co dau, chi dung vi tri cong cu (khong lo khoa ky thuat
    /// hay cau tieng Anh cua thu vien JSON Schema kieu "Some properties did not match").
    /// </summary>
    public sealed class ThongBaoLoiTests
    {
        private static string Loi(string json)
        {
            return Assert.Throws<LabelFormatException>(() => LabelSchema.Doc(json)).Message;
        }

        private static void KhongCoTiengAnh(string thongBao)
        {
            Assert.DoesNotContain("Some ", thongBao, StringComparison.Ordinal);
            Assert.DoesNotContain("schema", thongBao, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("(goc)", thongBao, StringComparison.Ordinal);
        }

        [Fact]
        public void Phan_loai_mot_lop_chi_ro_cong_cu_va_so_lop()
        {
            string tb = Loi("{\"modality\":\"image\",\"tools\":[{\"name\":\"a\",\"kind\":\"bbox\",\"classes\":[\"x\"]},{\"name\":\"b\",\"kind\":\"classification\",\"classes\":[\"x\"]}]}");
            KhongCoTiengAnh(tb);
            Assert.Equal("Tập nhãn chưa hợp lệ: Công cụ thứ 2 (Phân loại) — danh sách lớp: cần ít nhất 2 lớp.", tb);
        }

        [Fact]
        public void Truong_khong_dung_duoc_cho_loai_cong_cu()
        {
            string tb = Loi("{\"modality\":\"image\",\"tools\":[{\"name\":\"t\",\"kind\":\"freeText\",\"classes\":[\"a\"]}]}");
            KhongCoTiengAnh(tb);
            Assert.Contains("Công cụ thứ 1 (Câu trả lời mở) — danh sách lớp: không dùng được ở đây.", tb, StringComparison.Ordinal);
        }

        [Fact]
        public void Thieu_lop_va_lop_rong()
        {
            string thieu = Loi("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\"}]}");
            KhongCoTiengAnh(thieu);
            Assert.Contains("Công cụ thứ 1 (Khung chữ nhật): thiếu danh sách lớp.", thieu, StringComparison.Ordinal);

            string rong = Loi("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\",\"classes\":[\"a\",\"\"]}]}");
            KhongCoTiengAnh(rong);
            Assert.Contains("Công cụ thứ 1 (Khung chữ nhật) — lớp thứ 2: không được để trống.", rong, StringComparison.Ordinal);
        }

        [Fact]
        public void Loi_ngu_nghia_theo_vi_tri_cong_cu()
        {
            string trung = Loi("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\",\"classes\":[\"Xe\",\"xe\"]}]}");
            Assert.StartsWith("Công cụ thứ 1 (Khung chữ nhật): lớp “xe” bị trùng", trung, StringComparison.Ordinal);

            string sai = Loi("{\"modality\":\"text\",\"tools\":[{\"name\":\"k\",\"kind\":\"keypoint\",\"classes\":[\"a\"]}]}");
            Assert.Equal("Công cụ thứ 1 (Điểm mốc): không dùng được cho dữ liệu văn bản.", sai);
        }

        [Fact]
        public void Loi_nhan_dung_ten_loai_cong_cu()
        {
            LabelSchema s = LabelSchema.Doc("{\"modality\":\"image\",\"tools\":[{\"name\":\"lop_xe\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}");
            string tb = Assert.Throws<LabelFormatException>(() => LabelPayload.Tao(s, "{\"lop_xe\":{\"labelIds\":[\"c\"]}}", null)).Message;
            Assert.DoesNotContain("lop_xe", tb, StringComparison.Ordinal);
            Assert.Equal("Phân loại: lớp “c” không có trong tập nhãn.", tb);
        }
    }
}
