using System;

namespace Crowd.Labeling.Tests
{
    /// <summary>
    /// Cac cong cu them sau: freeText, number, keypoint, polyline, mask, track, relation, ranking
    /// va THUOC TINH cua tung doi tuong — kiem dinh dang, chuan hoa, cham dap an.
    /// </summary>
    public sealed class CongCuMoiTests
    {
        private static readonly SampleMetadata Anh100x50 = new SampleMetadata { Width = 100, Height = 50 };

        private static string Code(Action a)
        {
            return Assert.Throws<LabelFormatException>(a).Code;
        }

        private static LabelSchema Mot(string modality, string congCu)
        {
            return LabelSchema.Doc("{\"modality\":\"" + modality + "\",\"tools\":[" + congCu + "]}");
        }

        // ---------------------------------------------------------------- tap nhan

        [Theory]
        [InlineData("{\"modality\":\"text\",\"tools\":[{\"name\":\"k\",\"kind\":\"keypoint\",\"classes\":[\"a\"]}]}", "tap_nhan_khong_hop_le")]      // keypoint chi cho anh
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"ranking\"}]}", "tap_nhan_khong_hop_le")]                             // ranking chi cho list
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"track\",\"classes\":[\"a\"]}]}", "tap_nhan_khong_hop_le")]          // track chi cho video
        [InlineData("{\"modality\":\"text\",\"tools\":[{\"name\":\"q\",\"kind\":\"relation\",\"classes\":[\"a\"],\"spanTool\":\"khong_co\"}]}", "tap_nhan_khong_hop_le")]   // spanTool khong ton tai
        [InlineData("{\"modality\":\"text\",\"tools\":[{\"name\":\"l\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]},{\"name\":\"q\",\"kind\":\"relation\",\"classes\":[\"a\"],\"spanTool\":\"l\"}]}", "tap_nhan_khong_hop_le")] // spanTool khong phai span
        [InlineData("{\"modality\":\"text\",\"tools\":[{\"name\":\"q\",\"kind\":\"relation\",\"classes\":[\"a\"]}]}", "tap_nhan_sai_dinh_dang")]         // relation thieu spanTool
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"n\",\"kind\":\"number\",\"min\":5,\"max\":1}]}", "tap_nhan_khong_hop_le")]           // min > max
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"n\",\"kind\":\"number\",\"integer\":true,\"min\":0.5}]}", "tap_nhan_khong_hop_le")]  // so nguyen ma min le
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"n\",\"kind\":\"number\",\"classes\":[\"a\"]}]}", "tap_nhan_sai_dinh_dang")]          // number khong co lop
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"t\",\"kind\":\"freeText\",\"matchThreshold\":0.5}]}", "tap_nhan_sai_dinh_dang")]     // freeText khong cham
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"l\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"],\"attributes\":[{\"name\":\"x\",\"type\":\"boolean\"}]}]}", "tap_nhan_sai_dinh_dang")] // thuoc tinh chi cho cong cu danh sach
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\",\"classes\":[\"a\"],\"attributes\":[{\"name\":\"x\",\"type\":\"choice\"}]}]}", "tap_nhan_sai_dinh_dang")]                 // choice thieu options
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\",\"classes\":[\"a\"],\"attributes\":[{\"name\":\"x\",\"type\":\"boolean\",\"options\":[\"a\",\"b\"]}]}]}", "tap_nhan_sai_dinh_dang")] // options chi cho choice
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"v\",\"kind\":\"bbox\",\"classes\":[\"a\"],\"attributes\":[{\"name\":\"x\",\"type\":\"boolean\"},{\"name\":\"x\",\"type\":\"text\"}]}]}", "tap_nhan_khong_hop_le")] // trung ten thuoc tinh
        public void Tap_nhan_sai_bi_tu_choi(string json, string ma)
        {
            Assert.Equal(ma, Code(() => LabelSchema.Doc(json)));
        }

        [Fact]
        public void Tap_nhan_moi_duoc_chuan_hoa_va_doc_lai_ra_dung_ban_chuan()
        {
            LabelSchema s = LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[" +
                "{\"name\":\"chu\",\"kind\":\"bbox\",\"classes\":[\"dong\"],\"attributes\":[{\"name\":\"noi_dung\",\"type\":\"text\"},{\"name\":\"mau\",\"type\":\"choice\",\"options\":[\"do\",\"xanh\"],\"required\":true}]}," +
                "{\"name\":\"diem\",\"kind\":\"number\",\"integer\":true,\"min\":1,\"max\":5}," +
                "{\"name\":\"mo_ta\",\"kind\":\"freeText\",\"required\":false}]}");

            Assert.Equal(
                "{\"modality\":\"image\",\"tools\":[" +
                "{\"attributes\":[{\"maxLength\":500,\"name\":\"noi_dung\",\"required\":false,\"type\":\"text\"},{\"name\":\"mau\",\"options\":[\"do\",\"xanh\"],\"required\":true,\"type\":\"choice\"}]," +
                "\"classes\":[\"dong\"],\"kind\":\"bbox\",\"maxItems\":1000,\"minItems\":0,\"name\":\"chu\",\"required\":true}," +
                "{\"integer\":true,\"kind\":\"number\",\"max\":5,\"min\":1,\"name\":\"diem\",\"required\":true,\"tolerance\":0}," +
                "{\"kind\":\"freeText\",\"maxLength\":5000,\"name\":\"mo_ta\",\"required\":false}]}",
                s.ToRawJson().Json);
            Assert.Equal(s.ToRawJson(), LabelSchema.Doc(s.ToRawJson()).ToRawJson());
        }

        [Fact]
        public void Tap_nhan_cu_khong_doi_ban_chuan()
        {
            // Them truong moi vao ToolDefinition khong duoc lam doi ban chuan cua tap nhan da luu.
            LabelSchema s = LabelSchema.Doc("{\"modality\":\"audio\",\"tools\":[{\"name\":\"loi\",\"kind\":\"transcription\"},{\"name\":\"d\",\"kind\":\"temporalSegment\",\"classes\":[\"noi\"]}]}");
            Assert.Equal(
                "{\"modality\":\"audio\",\"tools\":[{\"kind\":\"transcription\",\"maxLength\":5000,\"name\":\"loi\",\"required\":true}," +
                "{\"classes\":[\"noi\"],\"kind\":\"temporalSegment\",\"maxItems\":1000,\"minItems\":0,\"name\":\"d\",\"required\":true}]}",
                s.ToRawJson().Json);
        }

        // ---------------------------------------------------------------- thuoc tinh doi tuong

        private static readonly LabelSchema Ocr = Mot("image",
            "{\"name\":\"chu\",\"kind\":\"bbox\",\"classes\":[\"dong\"],\"attributes\":[" +
            "{\"name\":\"noi_dung\",\"type\":\"text\",\"required\":true,\"maxLength\":20}," +
            "{\"name\":\"mau\",\"type\":\"choice\",\"options\":[\"do\",\"xanh\"]}," +
            "{\"name\":\"mo\",\"type\":\"boolean\"},{\"name\":\"so_ky_tu\",\"type\":\"number\"}]}");

        private static string Dong(string thuocTinh)
        {
            return "{\"chu\":[{\"labelId\":\"dong\",\"x\":0,\"y\":0,\"w\":50,\"h\":10" + (thuocTinh.Length == 0 ? string.Empty : ",\"attributes\":" + thuocTinh) + "}]}";
        }

        [Fact]
        public void Thuoc_tinh_hop_le_duoc_giu_va_sai_bi_tu_choi()
        {
            LabelPayload p = LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Xin chao\",\"mau\":\"do\",\"mo\":false,\"so_ky_tu\":8}"), Anh100x50);
            Assert.Contains("\"attributes\":{\"mau\":\"do\",\"mo\":false,\"noi_dung\":\"Xin chao\",\"so_ky_tu\":8}", p.DataJson, StringComparison.Ordinal);

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong(string.Empty), Anh100x50)));                                  // thieu thuoc tinh bat buoc
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"  \"}"), Anh100x50)));                       // bat buoc ma rong
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"a\",\"mau\":\"tim\"}"), Anh100x50)));        // ngoai lua chon
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"a\",\"mo\":\"co\"}"), Anh100x50)));          // sai kieu
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"a\",\"la\":1}"), Anh100x50)));               // thuoc tinh khong khai
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"" + new string('a', 21) + "\"}"), Anh100x50))); // qua dai
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":[1]}"), Anh100x50)));                           // JSON Schema chan mang
        }

        [Fact]
        public void Cong_cu_khong_khai_thuoc_tinh_thi_doi_tuong_khong_duoc_mang_thuoc_tinh()
        {
            LabelSchema khung = Mot("image", "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\"]}");
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(khung,
                "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":5,\"h\":5,\"attributes\":{\"mo\":true}}]}", Anh100x50)));

            // Object rong bi bo khi chuan hoa — hai nhan giong nhau ra cung chuoi.
            LabelPayload a = LabelPayload.Tao(khung, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":5,\"h\":5,\"attributes\":{}}]}", Anh100x50);
            LabelPayload b = LabelPayload.Tao(khung, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":5,\"h\":5}]}", Anh100x50);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Cham_dap_an_so_ca_thuoc_tinh_ma_dap_an_co_ghi()
        {
            LabelPayload dapAn = LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Xin chao cac ban\",\"mau\":\"do\"}"), Anh100x50);

            // Chu sai 1 / 16 ky tu (CER 0.0625 <= 0.1), hoa thuong khac nhau: van khop.
            Assert.True(LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"xin chao cac bon\",\"mau\":\"do\"}"), Anh100x50).KhopDapAn(Ocr, dapAn));
            // Sai mau.
            Assert.False(LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Xin chao cac ban\",\"mau\":\"xanh\"}"), Anh100x50).KhopDapAn(Ocr, dapAn));
            // Dap an co ghi mau ma bai nop bo trong.
            Assert.False(LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Xin chao cac ban\"}"), Anh100x50).KhopDapAn(Ocr, dapAn));
            // Chu khac han.
            Assert.False(LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Tam biet\",\"mau\":\"do\"}"), Anh100x50).KhopDapAn(Ocr, dapAn));
            // Thuoc tinh dap an KHONG ghi (mo) thi khong cham.
            Assert.True(LabelPayload.Tao(Ocr, Dong("{\"noi_dung\":\"Xin chao cac ban\",\"mau\":\"do\",\"mo\":true}"), Anh100x50).KhopDapAn(Ocr, dapAn));
        }

        // ---------------------------------------------------------------- freeText, number

        [Fact]
        public void Van_ban_tu_do_dung_cho_moi_loai_du_lieu_va_khong_cham()
        {
            foreach (string modality in new string[] { "image", "text", "audio", "video", "pair", "list" })
            {
                LabelSchema s = Mot(modality, "{\"name\":\"mo_ta\",\"kind\":\"freeText\",\"maxLength\":10}");
                LabelPayload a = LabelPayload.Tao(s, "{\"mo_ta\":{\"text\":\"mot con\"}}", null);
                LabelPayload b = LabelPayload.Tao(s, "{\"mo_ta\":{\"text\":\"khac han\"}}", null);

                Assert.True(a.KhopDapAn(s, b));
                Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(s, "{\"mo_ta\":{\"text\":\"\"}}", null)));
                Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(s, "{\"mo_ta\":{\"text\":\"dai qua muoi ky tu\"}}", null)));
            }
        }

        [Fact]
        public void So_kiem_gioi_han_so_nguyen_va_cham_theo_sai_so()
        {
            LabelSchema diem = Mot("pair", "{\"name\":\"diem\",\"kind\":\"number\",\"integer\":true,\"min\":1,\"max\":5,\"tolerance\":1}");

            Assert.Equal("{\"diem\":{\"value\":4}}", LabelPayload.Tao(diem, "{\"diem\":{\"value\":4.0}}", null).DataJson);
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(diem, "{\"diem\":{\"value\":4.5}}", null)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(diem, "{\"diem\":{\"value\":0}}", null)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(diem, "{\"diem\":{\"value\":6}}", null)));
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(diem, "{\"diem\":{\"value\":\"4\"}}", null)));

            LabelPayload dapAn = LabelPayload.Tao(diem, "{\"diem\":{\"value\":4}}", null);
            Assert.True(LabelPayload.Tao(diem, "{\"diem\":{\"value\":5}}", null).KhopDapAn(diem, dapAn));
            Assert.True(LabelPayload.Tao(diem, "{\"diem\":{\"value\":3}}", null).KhopDapAn(diem, dapAn));
            Assert.False(LabelPayload.Tao(diem, "{\"diem\":{\"value\":2}}", null).KhopDapAn(diem, dapAn));
        }

        // ---------------------------------------------------------------- keypoint, polyline

        [Fact]
        public void Diem_moc_cham_theo_do_lech_so_voi_duong_cheo_anh()
        {
            LabelSchema mat = Mot("image", "{\"name\":\"mat\",\"kind\":\"keypoint\",\"classes\":[\"mui\",\"mat_trai\"]}");
            SampleMetadata anh = new SampleMetadata { Width = 300, Height = 400 };   // duong cheo 500 → 2% = 10 px

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mui\",\"x\":301,\"y\":10}]}", anh)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"tai\",\"x\":1,\"y\":10}]}", anh)));

            LabelPayload dapAn = LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mui\",\"x\":100,\"y\":100},{\"labelId\":\"mat_trai\",\"x\":80,\"y\":60}]}", anh);
            NguongKhop n = NguongKhop.CuaThuVien;

            // Lech 6 px va 8 px (<= 10): khop. Thu tu diem khong quan trong.
            Assert.True(LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mat_trai\",\"x\":80,\"y\":68},{\"labelId\":\"mui\",\"x\":106,\"y\":100}]}", anh).KhopDapAn(mat, dapAn, n, anh));
            // Mui lech 12 px (> 10): truot.
            Assert.False(LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mui\",\"x\":112,\"y\":100},{\"labelId\":\"mat_trai\",\"x\":80,\"y\":60}]}", anh).KhopDapAn(mat, dapAn, n, anh));
            // Thieu mot diem.
            Assert.False(LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mui\",\"x\":100,\"y\":100}]}", anh).KhopDapAn(mat, dapAn, n, anh));
            // Nham lop.
            Assert.False(LabelPayload.Tao(mat, "{\"mat\":[{\"labelId\":\"mat_trai\",\"x\":100,\"y\":100},{\"labelId\":\"mui\",\"x\":80,\"y\":60}]}", anh).KhopDapAn(mat, dapAn, n, anh));
        }

        [Fact]
        public void Duong_gap_khuc_khop_bat_ke_chieu_ve_va_so_dinh()
        {
            LabelSchema lan = Mot("image", "{\"name\":\"lan\",\"kind\":\"polyline\",\"classes\":[\"vach\"]}");
            SampleMetadata anh = new SampleMetadata { Width = 300, Height = 400 };   // 3% duong cheo = 15 px

            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[0,0]]}]}", anh)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[5,5],[5,5]]}]}", anh)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[0,0],[301,0]]}]}", anh)));

            LabelPayload dapAn = LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[0,100],[300,100]]}]}", anh);
            NguongKhop n = NguongKhop.CuaThuVien;

            // Ve nguoc chieu, them dinh giua, lech 5 px.
            Assert.True(LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[300,105],[150,105],[0,105]]}]}", anh).KhopDapAn(lan, dapAn, n, anh));
            // Lech 40 px.
            Assert.False(LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[0,140],[300,140]]}]}", anh).KhopDapAn(lan, dapAn, n, anh));
            // Chi ve mot nua duong: dau mut lech xa.
            Assert.False(LabelPayload.Tao(lan, "{\"lan\":[{\"labelId\":\"vach\",\"points\":[[0,100],[150,100]]}]}", anh).KhopDapAn(lan, dapAn, n, anh));
        }

        // ---------------------------------------------------------------- mask

        // Anh 4 x 4 (16 pixel), duyet theo cot.
        private static readonly SampleMetadata Anh4x4 = new SampleMetadata { Width = 4, Height = 4 };

        private static readonly LabelSchema Vung = Mot("image", "{\"name\":\"vung\",\"kind\":\"mask\",\"classes\":[\"troi\",\"dat\"]}");

        private static string MatNa(string lop, string counts)
        {
            return "{\"labelId\":\"" + lop + "\",\"rle\":{\"size\":[4,4],\"counts\":[" + counts + "]}}";
        }

        [Fact]
        public void Mat_na_phai_dung_kich_thuoc_dang_chuan_va_khong_rong()
        {
            Assert.Equal("image", LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,8,8") + "]}", Anh4x4).TaskType);

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,8,7") + "]}", Anh4x4)));      // tong != 16
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "4,0,12") + "]}", Anh4x4)));     // doan giua dai 0
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("may", "0,8,8") + "]}", Anh4x4)));        // lop la
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "16") + "]}", Anh4x4)));         // mat na rong (1 doan)
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Vung,
                "{\"vung\":[{\"labelId\":\"troi\",\"rle\":{\"size\":[2,8],\"counts\":[0,8,8]}}]}", Anh4x4)));                                   // sai kich thuoc anh
        }

        [Fact]
        public void Mat_na_cham_theo_IoU()
        {
            LabelPayload dapAn = LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,8,8") + "]}", Anh4x4);   // 2 cot trai

            // 2 cot trai + 2 pixel: IoU 8 / 10.
            Assert.True(LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,10,6") + "]}", Anh4x4).KhopDapAn(Vung, dapAn));
            // Chi cot 1..2 (pixel 4..11): giao 4, hop 12 → 0.33.
            Assert.False(LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "4,8,4") + "]}", Anh4x4).KhopDapAn(Vung, dapAn));
            // Dung vung nhung sai lop.
            Assert.False(LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("dat", "0,8,8") + "]}", Anh4x4).KhopDapAn(Vung, dapAn));
        }

        // ---------------------------------------------------------------- track

        private static readonly SampleMetadata Video = new SampleMetadata { Width = 200, Height = 100, DurationSec = 10 };

        private static readonly LabelSchema TheoDoi = Mot("video", "{\"name\":\"vat\",\"kind\":\"track\",\"classes\":[\"xe\"]}");

        private static string Chuoi(string moc)
        {
            return "{\"vat\":[{\"labelId\":\"xe\",\"keyframes\":[" + moc + "]}]}";
        }

        private static string Moc(double t, double x)
        {
            return "{\"t\":" + t.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"x\":" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":10,\"w\":40,\"h\":40}";
        }

        [Fact]
        public void Chuoi_khung_kiem_moc_tang_dan_trong_video()
        {
            Assert.Equal("video", LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0) + "," + Moc(4, 100)), Video).TaskType);

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0)), Video)));                       // mot moc
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(TheoDoi, Chuoi(Moc(4, 0) + "," + Moc(2, 10)), Video)));    // lui thoi gian
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0) + "," + Moc(11, 10)), Video)));   // qua do dai
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0) + "," + Moc(4, 170)), Video)));   // khung tran ra ngoai
        }

        [Fact]
        public void Chuoi_khung_cham_ca_vi_tri_lan_thoi_diem()
        {
            LabelPayload dapAn = LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0) + "," + Moc(4, 100)), Video);

            // Cung duong di, them moc giua, lech 4 px.
            Assert.True(LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 4) + "," + Moc(2, 54) + "," + Moc(4, 104)), Video).KhopDapAn(TheoDoi, dapAn));
            // Cung vi tri nhung chi theo doi 1 / 4 thoi gian dau.
            Assert.False(LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 0) + "," + Moc(1, 25)), Video).KhopDapAn(TheoDoi, dapAn));
            // Dung thoi gian nhung di nguoc chieu.
            Assert.False(LabelPayload.Tao(TheoDoi, Chuoi(Moc(0, 100) + "," + Moc(4, 0)), Video).KhopDapAn(TheoDoi, dapAn));
        }

        // ---------------------------------------------------------------- relation

        private static readonly LabelSchema QuanHe = LabelSchema.Doc(
            "{\"modality\":\"text\",\"tools\":[" +
            "{\"name\":\"ner\",\"kind\":\"span\",\"classes\":[\"PER\",\"ORG\"]}," +
            "{\"name\":\"qh\",\"kind\":\"relation\",\"classes\":[\"lam_tai\"],\"spanTool\":\"ner\",\"required\":false}]}");

        private static readonly SampleMetadata Van30 = new SampleMetadata { Length = 30 };

        private const string HaiDoan = "\"ner\":[{\"labelId\":\"PER\",\"start\":0,\"end\":5},{\"labelId\":\"ORG\",\"start\":18,\"end\":24}]";

        [Fact]
        public void Quan_he_chi_noi_cac_doan_da_danh_dau()
        {
            LabelPayload p = LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":18,\"end\":24}}]}", Van30);
            Assert.Contains("\"qh\":[{\"from\":{\"end\":5,\"start\":0},\"labelId\":\"lam_tai\",\"to\":{\"end\":24,\"start\":18}}]", p.DataJson, StringComparison.Ordinal);

            // Dau tro toi vi tri khong phai mot doan.
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":18,\"end\":25}}]}", Van30)));
            // Noi mot doan voi chinh no.
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":0,\"end\":5}}]}", Van30)));
            // Ghi trung.
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":18,\"end\":24}}," +
                "{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":18,\"end\":24}}]}", Van30)));
        }

        [Fact]
        public void Quan_he_co_huong_va_cham_bang_F1()
        {
            LabelPayload dapAn = LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":0,\"end\":5},\"to\":{\"start\":18,\"end\":24}}]}", Van30);
            LabelPayload nguoc = LabelPayload.Tao(QuanHe,
                "{" + HaiDoan + ",\"qh\":[{\"labelId\":\"lam_tai\",\"from\":{\"start\":18,\"end\":24},\"to\":{\"start\":0,\"end\":5}}]}", Van30);
            LabelPayload thieu = LabelPayload.Tao(QuanHe, "{" + HaiDoan + ",\"qh\":[]}", Van30);

            Assert.True(dapAn.KhopDapAn(QuanHe, dapAn));
            Assert.False(nguoc.KhopDapAn(QuanHe, dapAn));
            Assert.False(thieu.KhopDapAn(QuanHe, dapAn));
        }

        // ---------------------------------------------------------------- ranking

        [Fact]
        public void Xep_hang_phai_la_hoan_vi_cac_muc_cua_mau_va_cham_bang_Kendall()
        {
            LabelSchema xep = Mot("list", "{\"name\":\"xep\",\"kind\":\"ranking\"}");
            SampleMetadata bonMuc = new SampleMetadata { ItemCount = 4 };

            Assert.Equal("{\"xep\":{\"order\":[2,0,3,1]}}", LabelPayload.Tao(xep, "{\"xep\":{\"order\":[2,0,3,1]}}", bonMuc).DataJson);
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(xep, "{\"xep\":{\"order\":[2,0,3]}}", bonMuc)));      // thieu muc
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(xep, "{\"xep\":{\"order\":[2,0,3,4]}}", bonMuc)));    // muc khong ton tai
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(xep, "{\"xep\":{\"order\":[2,0,2,1]}}", bonMuc)));   // lap muc

            LabelPayload dapAn = LabelPayload.Tao(xep, "{\"xep\":{\"order\":[0,1,2,3]}}", bonMuc);
            // Doi cho hai muc ke nhau: 5 / 6 cap cung chieu = 0.83 >= 0.8.
            Assert.True(LabelPayload.Tao(xep, "{\"xep\":{\"order\":[1,0,2,3]}}", bonMuc).KhopDapAn(xep, dapAn));
            // Doi cho hai cap: 4 / 6 = 0.67.
            Assert.False(LabelPayload.Tao(xep, "{\"xep\":{\"order\":[1,0,3,2]}}", bonMuc).KhopDapAn(xep, dapAn));
        }

        // ---------------------------------------------------------------- so nguyen viet kieu khac

        [Fact]
        public void So_nguyen_viet_dang_thap_phan_duoc_chuan_hoa_va_so_qua_lon_bi_tu_choi_chu_khong_no()
        {
            // JSON Schema coi 3.0 la "integer". Truoc day doc bang GetValue<int>() → nem loi → 500.
            LabelSchema ner = Mot("text", "{\"name\":\"ner\",\"kind\":\"span\",\"classes\":[\"A\"],\"minItems\":0.0}");
            SampleMetadata van = new SampleMetadata { Length = 30 };

            LabelPayload a = LabelPayload.Tao(ner, "{\"ner\":[{\"labelId\":\"A\",\"start\":0.0,\"end\":3.0}]}", van);
            LabelPayload b = LabelPayload.Tao(ner, "{\"ner\":[{\"labelId\":\"A\",\"start\":0,\"end\":3}]}", van);
            Assert.Equal(a, b);
            Assert.Equal("{\"ner\":[{\"end\":3,\"labelId\":\"A\",\"start\":0}]}", a.DataJson);

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(ner, "{\"ner\":[{\"labelId\":\"A\",\"start\":99999999999,\"end\":999999999999}]}", van)));

            // Mat na: do dai doan 8.0 = 8; do dai vuot moi gioi han → 400.
            LabelPayload m1 = LabelPayload.Tao(Vung, "{\"vung\":[{\"labelId\":\"troi\",\"rle\":{\"size\":[4.0,4],\"counts\":[0,8.0,8]}}]}", Anh4x4);
            LabelPayload m2 = LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,8,8") + "]}", Anh4x4);
            Assert.Equal(m1, m2);
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Vung, "{\"vung\":[" + MatNa("troi", "0,1e30,8") + "]}", Anh4x4)));

            // Xep hang.
            LabelSchema xep = Mot("list", "{\"name\":\"xep\",\"kind\":\"ranking\"}");
            Assert.Equal("{\"xep\":{\"order\":[1,0,2]}}", LabelPayload.Tao(xep, "{\"xep\":{\"order\":[1.0,0,2]}}", new SampleMetadata { ItemCount = 3 }).DataJson);
        }

        // ---------------------------------------------------------------- transcription cho anh

        [Fact]
        public void Chep_chu_trong_anh_dung_transcription()
        {
            LabelSchema ocr = Mot("image", "{\"name\":\"chu\",\"kind\":\"transcription\"}");
            LabelPayload dapAn = LabelPayload.Tao(ocr, "{\"chu\":{\"text\":\"CAM DO XE\"}}", Anh100x50);

            Assert.True(LabelPayload.Tao(ocr, "{\"chu\":{\"text\":\"cam  do xe\"}}", Anh100x50).KhopDapAn(ocr, dapAn));
            Assert.False(LabelPayload.Tao(ocr, "{\"chu\":{\"text\":\"cam dung\"}}", Anh100x50).KhopDapAn(ocr, dapAn));
        }
    }
}
