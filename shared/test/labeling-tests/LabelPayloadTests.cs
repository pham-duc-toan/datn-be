using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tests
{
    public sealed class LabelSchemaTests
    {
        [Fact]
        public void Tap_nhan_hop_le_duoc_chuan_hoa_dien_mac_dinh()
        {
            LabelSchema s = LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[" +
                "{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"ngay\",\"dem\"]}," +
                "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\"]}]}");

            Assert.Equal("image", s.Modality);
            Assert.Equal(2, s.Tools.Count);
            Assert.Equal(
                "{\"modality\":\"image\",\"tools\":[" +
                "{\"allowMultiple\":false,\"classes\":[\"ngay\",\"dem\"],\"kind\":\"classification\",\"name\":\"loai\",\"required\":true}," +
                "{\"classes\":[\"xe\"],\"kind\":\"bbox\",\"maxItems\":1000,\"minItems\":0,\"name\":\"vat\",\"required\":true}]}",
                s.ToRawJson().Json);

            // Doc lai ban chuan ra dung ban chuan.
            Assert.Equal(s.ToRawJson(), LabelSchema.Doc(s.ToRawJson()).ToRawJson());
        }

        [Theory]
        [InlineData("{\"modality\":\"text\",\"tools\":[{\"name\":\"k\",\"kind\":\"bbox\",\"classes\":[\"a\"]}]}", "tap_nhan_khong_hop_le")]       // bbox khong dung cho text
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]},{\"name\":\"k\",\"kind\":\"bbox\",\"classes\":[\"a\"]}]}", "tap_nhan_khong_hop_le")] // trung ten
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"classification\",\"classes\":[\"Meo\",\"meo\"]}]}", "tap_nhan_khong_hop_le")] // trung lop
        [InlineData("{\"modality\":\"image\",\"segmentSeconds\":30,\"tools\":[{\"name\":\"k\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}", "tap_nhan_khong_hop_le")] // segment cho anh
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"classification\",\"classes\":[\"a\"]}]}", "tap_nhan_sai_dinh_dang")]  // phan loai can >= 2 lop
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"K hoa\",\"kind\":\"bbox\",\"classes\":[\"a\"]}]}", "tap_nhan_sai_dinh_dang")]         // ten sai mau
        [InlineData("{\"modality\":\"image\",\"tools\":[{\"name\":\"k\",\"kind\":\"bbox\",\"classes\":[\"a\"],\"allowTie\":true}]}", "tap_nhan_sai_dinh_dang")] // truong khong thuoc bbox
        [InlineData("{\"modality\":\"sound\",\"tools\":[]}", "tap_nhan_sai_dinh_dang")]                                                              // loai du lieu la + rong
        public void Tap_nhan_sai_bi_tu_choi(string json, string ma)
        {
            LabelFormatException ex = Assert.Throws<LabelFormatException>(() => LabelSchema.Doc(json));
            Assert.Equal(ma, ex.Code);
        }
    }

    public sealed class LabelPayloadTests
    {
        private static readonly LabelSchema Anh = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":[" +
            "{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"ngay\",\"dem\"]}," +
            "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\",\"nguoi\"],\"maxItems\":3}," +
            "{\"name\":\"vung\",\"kind\":\"polygon\",\"classes\":[\"duong\"],\"required\":false}]}");

        private static readonly SampleMetadata Anh100x50 = new SampleMetadata { Width = 100, Height = 50 };

        private static readonly LabelSchema VanBan = LabelSchema.Doc(
            "{\"modality\":\"text\",\"tools\":[{\"name\":\"ner\",\"kind\":\"span\",\"classes\":[\"PER\",\"LOC\"]}]}");

        private static readonly LabelSchema AmThanh = LabelSchema.Doc(
            "{\"modality\":\"audio\",\"segmentSeconds\":30,\"tools\":[" +
            "{\"name\":\"loi\",\"kind\":\"transcription\"}," +
            "{\"name\":\"doan\",\"kind\":\"temporalSegment\",\"classes\":[\"noi\",\"nhac\"]}]}");

        private static readonly LabelSchema Cap = LabelSchema.Doc(
            "{\"modality\":\"pair\",\"tools\":[{\"name\":\"tot_hon\",\"kind\":\"pairwise\",\"allowTie\":false}]}");

        private static string Code(Action a)
        {
            return Assert.Throws<LabelFormatException>(a).Code;
        }

        // ---------------------------------------------------------------- anh

        [Fact]
        public void Nhan_anh_hop_le_va_chuan_hoa_khong_phu_thuoc_thu_tu()
        {
            LabelPayload a = LabelPayload.Tao(Anh,
                "{\"vat\":[{\"labelId\":\"xe\",\"x\":50,\"y\":0,\"w\":10,\"h\":10},{\"labelId\":\"nguoi\",\"x\":0,\"y\":0,\"w\":5,\"h\":5}],\"loai\":{\"labelIds\":[\"ngay\"]}}",
                Anh100x50);
            LabelPayload b = LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"nguoi\",\"x\":0,\"y\":0,\"w\":5,\"h\":5},{\"labelId\":\"xe\",\"x\":50,\"y\":0,\"w\":10,\"h\":10}]}",
                Anh100x50);

            Assert.Equal("image", a.TaskType);
            Assert.Equal(a, b);
            Assert.StartsWith("{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"h\":5,\"labelId\":\"nguoi\"", a.DataJson);
        }

        [Fact]
        public void Khung_vuot_anh_hoac_lop_la_hoac_qua_so_luong_bi_tu_choi()
        {
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":95,\"y\":0,\"w\":10,\"h\":10}]}", Anh100x50)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"meo\",\"x\":0,\"y\":0,\"w\":1,\"h\":1}]}", Anh100x50)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[" + string.Join(",", new string[] { Hop(), Hop(), Hop(), Hop() }) + "]}", Anh100x50)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\",\"dem\"]},\"vat\":[]}", Anh100x50)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Anh,
                "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[],\"vung\":[{\"labelId\":\"duong\",\"points\":[[0,0],[200,0],[0,10]]}]}", Anh100x50)));
        }

        private static string Hop()
        {
            return "{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":1,\"h\":1}";
        }

        [Fact]
        public void Sai_hinh_dang_bi_JSON_Schema_chan()
        {
            // thieu cong cu bat buoc "vat"
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]}}", Anh100x50)));
            // cong cu khong co trong tap nhan
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[],\"la\":1}", Anh100x50)));
            // khung thieu h, w am
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":1}]}", Anh100x50)));
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":-1,\"h\":1}]}", Anh100x50)));
        }

        [Fact]
        public void Cham_bbox_bang_IoU()
        {
            LabelPayload dapAn = LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":10,\"y\":10,\"w\":20,\"h\":20}]}", Anh100x50);
            LabelPayload gan = LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":12,\"y\":12,\"w\":20,\"h\":20}]}", Anh100x50);
            LabelPayload lech = LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":25,\"y\":25,\"w\":20,\"h\":20}]}", Anh100x50);
            LabelPayload saiLop = LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[{\"labelId\":\"nguoi\",\"x\":10,\"y\":10,\"w\":20,\"h\":20}]}", Anh100x50);

            Assert.True(gan.KhopDapAn(Anh, dapAn));      // IoU ~0.68
            Assert.False(lech.KhopDapAn(Anh, dapAn));    // IoU ~0.06
            Assert.False(saiLop.KhopDapAn(Anh, dapAn));
        }

        [Fact]
        public void Cham_polygon_bang_IoU_uoc_luong()
        {
            LabelPayload dapAn = LabelPayload.Tao(Anh, HinhChuNhat("0", "40"), Anh100x50);
            LabelPayload gan = LabelPayload.Tao(Anh, HinhChuNhat("5", "45"), Anh100x50);
            LabelPayload xa = LabelPayload.Tao(Anh, HinhChuNhat("50", "90"), Anh100x50);

            Assert.True(gan.KhopDapAn(Anh, dapAn));      // IoU 35/45 ~0.78
            Assert.False(xa.KhopDapAn(Anh, dapAn));      // khong giao
        }

        /// <summary>Da giac hinh chu nhat tu x = trai den x = phai, cao 40.</summary>
        private static string HinhChuNhat(string trai, string phai)
        {
            return "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[],\"vung\":[{\"labelId\":\"duong\",\"points\":[[T,0],[P,0],[P,40],[T,40]]}]}"
                .Replace("T", trai, StringComparison.Ordinal)
                .Replace("P", phai, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- van ban

        [Fact]
        public void Span_kiem_nam_trong_van_ban_va_khong_chong()
        {
            SampleMetadata dai20 = new SampleMetadata { Length = 20 };

            LabelPayload ok = LabelPayload.Tao(VanBan, "{\"ner\":[{\"labelId\":\"LOC\",\"start\":10,\"end\":15},{\"labelId\":\"PER\",\"start\":0,\"end\":5}]}", dai20);
            Assert.StartsWith("{\"ner\":[{\"end\":5,\"labelId\":\"PER\"", ok.DataJson);

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(VanBan, "{\"ner\":[{\"labelId\":\"PER\",\"start\":15,\"end\":25}]}", dai20)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(VanBan, "{\"ner\":[{\"labelId\":\"PER\",\"start\":0,\"end\":5},{\"labelId\":\"LOC\",\"start\":4,\"end\":8}]}", dai20)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(VanBan, "{\"ner\":[{\"labelId\":\"PER\",\"start\":5,\"end\":5}]}", dai20)));

            LabelPayload thieuMot = LabelPayload.Tao(VanBan, "{\"ner\":[{\"labelId\":\"PER\",\"start\":0,\"end\":5}]}", dai20);
            Assert.True(ok.KhopDapAn(VanBan, ok));
            Assert.False(thieuMot.KhopDapAn(VanBan, ok));  // F1 = 2/3 < 1.0
        }

        // ---------------------------------------------------------------- am thanh

        [Fact]
        public void Transcription_va_doan_thoi_gian()
        {
            SampleMetadata dai30 = new SampleMetadata { DurationSec = 30 };

            LabelPayload dapAn = LabelPayload.Tao(AmThanh, "{\"loi\":{\"text\":\"xin chao cac ban\"},\"doan\":[{\"labelId\":\"noi\",\"start\":1,\"end\":5}]}", dai30);
            LabelPayload gan = LabelPayload.Tao(AmThanh, "{\"loi\":{\"text\":\"Xin  chao cac ban.\"},\"doan\":[{\"labelId\":\"noi\",\"start\":1.5,\"end\":5}]}", dai30);
            LabelPayload xa = LabelPayload.Tao(AmThanh, "{\"loi\":{\"text\":\"tam biet\"},\"doan\":[{\"labelId\":\"noi\",\"start\":1,\"end\":5}]}", dai30);

            Assert.True(gan.KhopDapAn(AmThanh, dapAn));   // CER = 1/16, IoU = 0.875
            Assert.False(xa.KhopDapAn(AmThanh, dapAn));   // CER cao

            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(AmThanh, "{\"loi\":{\"text\":\"  \"},\"doan\":[]}", dai30)));
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(AmThanh, "{\"loi\":{\"text\":\"a\"},\"doan\":[{\"labelId\":\"noi\",\"start\":20,\"end\":31}]}", dai30)));
        }

        // ---------------------------------------------------------------- cap

        [Fact]
        public void Pairwise_khong_cho_hoa_khi_tap_nhan_cam()
        {
            Assert.Equal("nhan_khong_hop_le", Code(() => LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"tie\"}}", null)));
            Assert.Equal("nhan_sai_dinh_dang", Code(() => LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"c\"}}", null)));
            Assert.True(LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"a\"}}", null)
                .KhopDapAn(Cap, LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"a\"}}", null)));
        }

        // ---------------------------------------------------------------- gop

        [Fact]
        public void Gop_theo_tung_cong_cu()
        {
            LabelPayload[] nhan = new LabelPayload[]
            {
                LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[]}", Anh100x50),
                LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"ngay\"]},\"vat\":[]}", Anh100x50),
                LabelPayload.Tao(Anh, "{\"loai\":{\"labelIds\":[\"dem\"]},\"vat\":[]}", Anh100x50),
            };

            JsonObject g = LabelAggregator.Gop(Anh, nhan);

            Assert.Equal("majority", g["loai"]!["method"]!.GetValue<string>());
            Assert.Equal("ngay", g["loai"]!["final"]!["labelIds"]![0]!.GetValue<string>());
            Assert.Equal(2, g["loai"]!["votes"]!["ngay"]!.GetValue<int>());
            Assert.False(g["loai"]!["disputed"]!.GetValue<bool>());
            Assert.Equal("none", g["vat"]!["method"]!.GetValue<string>());
            Assert.Equal("none", g["vung"]!["method"]!.GetValue<string>());   // khong ai gan cong cu tuy chon nay

            JsonObject hoa = LabelAggregator.Gop(Cap, new LabelPayload[]
            {
                LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"a\"}}", null),
                LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"b\"}}", null),
            });
            Assert.True(hoa["tot_hon"]!["disputed"]!.GetValue<bool>());
        }

        // ---------------------------------------------------------------- tren day

        [Fact]
        public void Json_tren_day_long_nhau_va_doc_lai_bang_nhau()
        {
            LabelPayload goc = LabelPayload.Tao(Cap, "{\"tot_hon\":{\"choice\":\"b\"}}", null);

            string json = JsonSerializer.Serialize(goc);
            Assert.Equal("{\"taskType\":\"pair\",\"schemaVersion\":1,\"data\":{\"tot_hon\":{\"choice\":\"b\"}}}", json);
            Assert.Equal(goc, JsonSerializer.Deserialize<LabelPayload>(json));

            // Doc tu database: jsonb tra ve co khoang trang.
            Assert.Equal(goc, LabelPayload.TuLuuTru("pair", 1, "{\"tot_hon\": {\"choice\": \"b\"}}"));
        }

        [Theory]
        [InlineData("{\"taskType\":\"pair\",\"schemaVersion\":1}")]
        [InlineData("{\"taskType\":\"pair\",\"schemaVersion\":1,\"data\":{},\"x\":1}")]
        [InlineData("{\"taskType\":\"sound\",\"schemaVersion\":1,\"data\":{}}")]
        [InlineData("{\"taskType\":\"pair\",\"schemaVersion\":9,\"data\":{}}")]
        [InlineData("{\"taskType\":\"pair\",\"schemaVersion\":1,\"data\":[]}")]
        public void Json_tren_day_sai_thi_nem_JsonException(string json)
        {
            Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<LabelPayload>(json));
        }

        [Fact]
        public void RawJson_so_sanh_theo_gia_tri_bo_qua_thu_tu_khoa()
        {
            Assert.Equal(RawJson.Tu("{\"b\": 1, \"a\": [2, 1]}"), RawJson.Tu("{\"a\":[2,1],\"b\":1}"));
            Assert.NotEqual(RawJson.Tu("{\"a\":[1,2]}"), RawJson.Tu("{\"a\":[2,1]}"));
            Assert.Equal("{\"durationSec\":30,\"segmentStart\":60}", new SampleMetadata { DurationSec = 30, SegmentStart = 60 }.ToRawJson().Json);
        }
    }
}
