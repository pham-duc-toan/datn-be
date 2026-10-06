using System;
using System.Text.Json;

namespace Crowd.Labeling.Tests
{
    public sealed class LabelPayloadTests
    {
        private static readonly string[] ChoMeo = new string[] { "cho", "meo" };

        private static LabelPayload PhanLoaiTuJson(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                return LabelPayload.Tao(LabelTaskTypes.ImageClassification, 1, doc.RootElement);
            }
        }

        [Fact]
        public void Chuan_hoa_khong_phu_thuoc_thu_tu_va_khoang_trang()
        {
            LabelPayload a = PhanLoaiTuJson("{ \"labelIds\" : [ \"meo\", \"cho\" ] }");
            LabelPayload b = LabelPayload.PhanLoai("cho", "meo");

            Assert.Equal("{\"labelIds\":[\"cho\",\"meo\"]}", a.DataJson);
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Doc_lai_tu_chuoi_database_ra_cung_gia_tri()
        {
            // Postgres tra jsonb ve co them khoang trang: {"labelIds": ["do"]}.
            LabelPayload goc = LabelPayload.PhanLoai("do");
            LabelPayload tuDb = LabelPayload.Tao(goc.TaskType, goc.SchemaVersion, "{\"labelIds\": [\"do\"]}");

            Assert.Equal(goc, tuDb);
        }

        [Theory]
        [InlineData("[\"do\"]")]                              // khong phai object
        [InlineData("{}")]                                    // thieu labelIds
        [InlineData("{\"labels\":[\"do\"]}")]                 // sai ten truong
        [InlineData("{\"labelIds\":[\"do\"],\"x\":1}")]       // truong la
        [InlineData("{\"labelIds\":[]}")]                     // rong
        [InlineData("{\"labelIds\":[\"do\",\"do\"]}")]        // lap
        [InlineData("{\"labelIds\":[1]}")]                    // khong phai chuoi
        [InlineData("{\"labelIds\":[\"  \"]}")]               // chuoi trang
        [InlineData("{\"labelIds\":\"do\"}")]                 // khong phai mang
        public void Sai_dinh_dang_thi_nem_loi(string json)
        {
            Assert.Throws<LabelFormatException>(() => PhanLoaiTuJson(json));
        }

        [Fact]
        public void Json_hong_thi_nem_LabelFormatException()
        {
            Assert.Throws<LabelFormatException>(() =>
                LabelPayload.Tao(LabelTaskTypes.ImageClassification, 1, "{khong phai json"));
        }

        [Fact]
        public void Loai_nhan_chua_ho_tro_co_ma_loi_rieng()
        {
            using (JsonDocument doc = JsonDocument.Parse("{}"))
            {
                LabelFormatException ex = Assert.Throws<LabelFormatException>(() =>
                    LabelPayload.Tao("boundingBox", 1, doc.RootElement));
                Assert.Equal("loai_nhan_chua_ho_tro", ex.Code);
            }

            Assert.Throws<LabelFormatException>(() => LabelFormats.Lay(LabelTaskTypes.ImageClassification, 99));
        }

        [Fact]
        public void Cac_lop_va_khop_dap_an()
        {
            LabelPayload nop = LabelPayload.PhanLoai("meo", "cho");

            Assert.Equal(ChoMeo, nop.CacLop());
            Assert.True(nop.KhopDapAn(LabelPayload.PhanLoai("cho", "meo")));
            Assert.False(nop.KhopDapAn(LabelPayload.PhanLoai("cho")));
            Assert.False(LabelPayload.PhanLoai("cho").KhopDapAn(LabelPayload.PhanLoai("cho", "ga")));
        }

        [Fact]
        public void Json_tren_day_la_object_long_nhau_va_doc_lai_bang_nhau()
        {
            LabelPayload goc = LabelPayload.PhanLoai("do");

            string json = JsonSerializer.Serialize(goc);
            Assert.Equal("{\"taskType\":\"imageClassification\",\"schemaVersion\":1,\"data\":{\"labelIds\":[\"do\"]}}", json);

            LabelPayload? doc = JsonSerializer.Deserialize<LabelPayload>(json);
            Assert.Equal(goc, doc);
        }

        [Theory]
        [InlineData("{\"taskType\":\"imageClassification\",\"schemaVersion\":1}")]                                     // thieu data
        [InlineData("{\"taskType\":\"imageClassification\",\"schemaVersion\":1,\"data\":{\"labelIds\":[\"do\"]},\"x\":1}")] // truong la
        [InlineData("{\"taskType\":\"imageClassification\",\"schemaVersion\":1,\"data\":{\"labelIds\":[]}}")]          // data sai dinh dang
        public void Json_tren_day_sai_thi_nem_JsonException(string json)
        {
            Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<LabelPayload>(json));
        }

        [Fact]
        public void PhanLoai_khong_nhan_null()
        {
            Assert.Throws<ArgumentNullException>(() => LabelPayload.PhanLoai(null!));
        }
    }
}
