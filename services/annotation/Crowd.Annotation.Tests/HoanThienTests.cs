using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Tests
{
    public sealed class CanhBaoLechTests
    {
        private static SortedDictionary<string, SortedDictionary<string, int>> PhanBo(int cho, int meo, int ga)
        {
            SortedDictionary<string, int> loai = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (cho > 0)
            {
                loai["cho"] = cho;
            }

            if (meo > 0)
            {
                loai["meo"] = meo;
            }

            if (ga > 0)
            {
                loai["ga"] = ga;
            }

            return new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal) { ["loai"] = loai };
        }

        [Fact]
        public void Lop_ap_dao_va_lop_chua_co_nhan()
        {
            List<CanhBaoLech> c = ResultAggregator.CanhBaoLech(ResultAggregatorTests.TapNhan, PhanBo(90, 10, 0), 80, 20);

            Assert.Contains(c, x => x.Code == "lop_ap_dao" && x.Label == "cho" && x.Percent == 90);
            Assert.Contains(c, x => x.Code == "lop_chua_co_nhan" && x.Label == "ga");
            Assert.DoesNotContain(c, x => x.Label == "meo");
        }

        [Fact]
        public void It_nhan_thi_chua_canh_bao()
        {
            Assert.Empty(ResultAggregator.CanhBaoLech(ResultAggregatorTests.TapNhan, PhanBo(10, 0, 0), 80, 20));
        }

        [Fact]
        public void Phan_bo_deu_thi_khong_canh_bao()
        {
            Assert.Empty(ResultAggregator.CanhBaoLech(ResultAggregatorTests.TapNhan, PhanBo(40, 30, 30), 80, 20));
        }
    }

    public sealed class KhungAnhExporterTests
    {
        private static readonly LabelSchema TapNhan = ResultAggregatorTests.TapNhan;

        private static ProjectResultsResponse KetQua()
        {
            SampleResultResponse coKichThuoc = new SampleResultResponse
            {
                SampleId = Guid.NewGuid(),
                StorageKey = "p/raw/a.png",
                SampleMetadata = RawJson.Tu("{\"width\":200,\"height\":100}"),
                ApprovedCount = 1,
                Disputed = false,
                Tools = RawJson.Tu("{\"vat\":{\"kind\":\"bbox\",\"method\":\"cluster\",\"final\":[{\"labelId\":\"mat\",\"x\":50,\"y\":25,\"w\":100,\"h\":50}],\"supports\":[1],\"agrees\":[true],\"disputed\":false}}"),
                Labels = new List<RawJson> { RawJson.Tu("{\"vat\":[{\"labelId\":\"mat\",\"x\":50,\"y\":25,\"w\":100,\"h\":50}]}") },
            };
            SampleResultResponse khongKichThuoc = new SampleResultResponse
            {
                SampleId = Guid.NewGuid(),
                StorageKey = "p/raw/b.png",
                SampleMetadata = RawJson.Tu("{}"),
                ApprovedCount = 1,
                Disputed = false,
                Tools = RawJson.Tu("{\"vat\":{\"kind\":\"bbox\",\"method\":\"cluster\",\"final\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":2,\"h\":2}],\"supports\":[1],\"agrees\":[true],\"disputed\":false}}"),
                Labels = new List<RawJson> { RawJson.Tu("{\"vat\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":2,\"h\":2}]}") },
            };
            return new ProjectResultsResponse
            {
                ProjectId = Guid.NewGuid(),
                Modality = "image",
                LabelSchema = TapNhan.ToRawJson(),
                SampleCount = 2,
                DisputedCount = 0,
                LabelDistribution = new Dictionary<string, SortedDictionary<string, int>>(),
                ImbalanceWarnings = new List<ImbalanceWarningDto>(),
                Samples = new List<SampleResultResponse> { coKichThuoc, khongKichThuoc },
            };
        }

        private static Dictionary<string, string> MoZip(byte[] zip)
        {
            Dictionary<string, string> ds = new Dictionary<string, string>(StringComparer.Ordinal);
            using (ZipArchive a = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in a.Entries)
                {
                    using (StreamReader r = new StreamReader(e.Open(), Encoding.UTF8))
                    {
                        ds[e.FullName] = r.ReadToEnd();
                    }
                }
            }

            return ds;
        }

        [Fact]
        public void Yolo_chuan_hoa_tam_va_kich_thuoc_bo_anh_thieu_kich_thuoc()
        {
            ProjectResultsResponse kq = KetQua();
            Dictionary<string, string> zip = MoZip(KhungAnhExporter.Yolo(kq, TapNhan));

            Assert.Equal("0 0.5 0.5 0.5 0.5\n", zip["labels/" + kq.Samples[0].SampleId + ".txt"]);
            Assert.False(zip.ContainsKey("labels/" + kq.Samples[1].SampleId + ".txt"));
            Assert.Equal("mat\n", zip["classes.txt"]);
            Assert.Contains("Bỏ qua 1 ảnh", zip["README.txt"], StringComparison.Ordinal);
        }

        [Fact]
        public void Voc_toa_do_pixel_goc_1()
        {
            ProjectResultsResponse kq = KetQua();
            Dictionary<string, string> zip = MoZip(KhungAnhExporter.PascalVoc(kq, TapNhan));

            XElement box = XDocument.Parse(zip["Annotations/" + kq.Samples[0].SampleId + ".xml"]).Descendants("bndbox").Single();
            Assert.Equal("51", box.Element("xmin")!.Value);
            Assert.Equal("26", box.Element("ymin")!.Value);
            Assert.Equal("150", box.Element("xmax")!.Value);
            Assert.Equal("75", box.Element("ymax")!.Value);
        }

        [Fact]
        public void Du_an_khong_co_khung_thi_tu_choi()
        {
            LabelSchema chiPhanLoai = LabelSchema.Doc("{\"modality\":\"image\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}");

            Assert.Equal("khung_chi_cho_anh", Assert.Throws<InvalidValueException>(() => KhungAnhExporter.Yolo(KetQua(), chiPhanLoai)).Code);
        }
    }
}
