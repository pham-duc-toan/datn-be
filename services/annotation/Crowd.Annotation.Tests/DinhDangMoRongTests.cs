// Mang hang so trong test chi tao mot lan moi test — canh bao hieu nang CA1861 / CA1845 khong co y nghia o day.
#pragma warning disable CA1861, CA1845
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Crowd.Annotation.Api.Services;

namespace Crowd.Annotation.Tests
{
    public sealed class DinhDangMoRongTests
    {
        [Fact]
        public void Gan_BIO_theo_doan_ky_tu()
        {
            string van = "Ong Nguyen Van A song o Ha Noi";
            // "Nguyen Van A" = [4, 16), "Ha Noi" = [24, 30)
            List<KeyValuePair<int[], string>> tu = DinhDangMoRongExporter.GanBio(
                van, new List<int[]> { new int[] { 4, 16 }, new int[] { 24, 30 } }, new List<string> { "PER", "LOC" });

            List<string> nhan = new List<string>();
            foreach (KeyValuePair<int[], string> t in tu)
            {
                nhan.Add(van.Substring(t.Key[0], t.Key[1] - t.Key[0]) + "/" + t.Value);
            }

            Assert.Equal(
                new List<string> { "Ong/O", "Nguyen/B-PER", "Van/I-PER", "A/I-PER", "song/O", "o/O", "Ha/B-LOC", "Noi/I-LOC" },
                nhan);
        }

        [Fact]
        public void Hai_doan_lien_nhau_cung_lop_van_tach_B()
        {
            string van = "Ha Noi Hue";
            List<KeyValuePair<int[], string>> tu = DinhDangMoRongExporter.GanBio(
                van, new List<int[]> { new int[] { 0, 6 }, new int[] { 7, 10 } }, new List<string> { "LOC", "LOC" });
            Assert.Equal("B-LOC", tu[2].Value);
        }

        [Fact]
        public void Khung_tai_bo_qua_doan_an()
        {
            JsonArray moc = (JsonArray)JsonNode.Parse(
                "[{\"t\":0,\"x\":0,\"y\":0,\"w\":10,\"h\":10},{\"t\":2,\"x\":20,\"y\":0,\"w\":10,\"h\":10,\"visible\":false},"
                + "{\"t\":4,\"x\":40,\"y\":0,\"w\":10,\"h\":10},{\"t\":6,\"x\":60,\"y\":0,\"w\":10,\"h\":10}]")!;

            Assert.Equal(10, DinhDangMoRongExporter.KhungTai(moc, 1)![0], 6);
            Assert.Null(DinhDangMoRongExporter.KhungTai(moc, 3));
            Assert.Equal(50, DinhDangMoRongExporter.KhungTai(moc, 5)![0], 6);
            Assert.Null(DinhDangMoRongExporter.KhungTai(moc, 7));
        }

        [Fact]
        public void Dinh_khung_xoay_90_do_doi_rong_cao()
        {
            JsonObject k = (JsonObject)JsonNode.Parse("{\"x\":0,\"y\":0,\"w\":20,\"h\":10,\"angle\":90}")!;
            List<double[]> d = DinhDangMoRongExporter.DinhKhung(k);
            // Tam (10, 5); xoay 90 do: dinh tren-trai (-10,-5) → (5,-10) + tam = (15, -5).
            Assert.Equal(15, d[0][0], 6);
            Assert.Equal(-5, d[0][1], 6);
            Assert.Equal(5, d[2][0], 6);
            Assert.Equal(15, d[2][1], 6);
        }
    }
}
