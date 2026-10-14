using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Xuat khung chu nhat cua du an anh sang hai dinh dang huan luyen pho bien (FB-25), dong goi zip:
    ///   - YOLO: labels/{sampleId}.txt "lop cx cy w h" (chuan hoa 0-1 theo kich thuoc anh), data.yaml.
    ///   - Pascal VOC: Annotations/{sampleId}.xml (toa do pixel, goc 1 theo quy uoc VOC).
    /// Chi cong cu bbox (hai dinh dang nay khong co da giac). Giong COCO: moi khung DA GOP tu cac nhan
    /// da duyet la mot dong (mau tranh chap khong co khung). Anh khong co kich thuoc (metadata) bi bo qua
    /// va dem trong README.txt. File anh KHONG nam trong zip: images.txt anh xa sampleId → khoa file.
    /// </summary>
    public static class KhungAnhExporter
    {
        public static byte[] Yolo(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCuKhung(kq, tapNhan, "yolo");
            List<string> tenLop = TenLop(congCu);
            Dictionary<string, int> maLop = MaLop(congCu);

            int boQua = 0;
            int soKhung = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    StringBuilder anh = new StringBuilder();
                    foreach (SampleResultResponse s in kq.Samples)
                    {
                        SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);
                        if (!md.Width.HasValue || !md.Height.HasValue || md.Width.Value <= 0 || md.Height.Value <= 0)
                        {
                            boQua++;
                            continue;
                        }

                        double rong = md.Width.Value;
                        double cao = md.Height.Value;
                        StringBuilder dong = new StringBuilder();
                        foreach (KhungNhan k in CacKhung(s, congCu))
                        {
                            double cx = KepDoan((k.X + (k.W / 2)) / rong);
                            double cy = KepDoan((k.Y + (k.H / 2)) / cao);
                            double w = KepDoan(k.W / rong);
                            double h = KepDoan(k.H / cao);
                            dong.Append(maLop[k.CongCu + "/" + k.Lop].ToString(CultureInfo.InvariantCulture))
                                .Append(' ').Append(So(cx)).Append(' ').Append(So(cy))
                                .Append(' ').Append(So(w)).Append(' ').Append(So(h)).Append('\n');
                            soKhung++;
                        }

                        Ghi(zip, "labels/" + s.SampleId + ".txt", dong.ToString());
                        anh.Append(s.SampleId).Append('\t').Append(s.StorageKey).Append('\n');
                    }

                    StringBuilder yaml = new StringBuilder();
                    yaml.Append("# Du an ").Append(kq.ProjectId).Append('\n');
                    yaml.Append("nc: ").Append(tenLop.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    yaml.Append("names: [").Append(string.Join(", ", tenLop.Select(t => "'" + t.Replace("'", "''", StringComparison.Ordinal) + "'"))).Append("]\n");
                    Ghi(zip, "data.yaml", yaml.ToString());
                    Ghi(zip, "classes.txt", string.Join("\n", tenLop) + "\n");
                    Ghi(zip, "images.txt", anh.ToString());
                    Ghi(zip, "README.txt", GhiChu("YOLO", kq.Samples.Count - boQua, boQua, soKhung,
                        "Moi dong labels/*.txt: <so thu tu lop> <tam x> <tam y> <rong> <cao>, chuan hoa 0-1 theo kich thuoc anh."));
                }

                return ms.ToArray();
            }
        }

        public static byte[] PascalVoc(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCuKhung(kq, tapNhan, "voc");
            bool nhieuCongCu = congCu.Count > 1;

            int boQua = 0;
            int soKhung = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    StringBuilder danhSach = new StringBuilder();
                    StringBuilder anh = new StringBuilder();
                    foreach (SampleResultResponse s in kq.Samples)
                    {
                        SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);
                        if (!md.Width.HasValue || !md.Height.HasValue || md.Width.Value <= 0 || md.Height.Value <= 0)
                        {
                            boQua++;
                            continue;
                        }

                        int rong = md.Width.Value;
                        int cao = md.Height.Value;
                        XElement goc = new XElement("annotation",
                            new XElement("folder", "images"),
                            new XElement("filename", s.StorageKey ?? s.SampleId.ToString()),
                            new XElement("sample_id", s.SampleId),
                            new XElement("size",
                                new XElement("width", rong),
                                new XElement("height", cao),
                                new XElement("depth", 3)),
                            new XElement("segmented", 0));

                        foreach (KhungNhan k in CacKhung(s, congCu))
                        {
                            // VOC dung toa do pixel goc 1, xmax/ymax bao gom diem cuoi.
                            int xmin = KepNguyen((int)Math.Floor(k.X) + 1, 1, rong);
                            int ymin = KepNguyen((int)Math.Floor(k.Y) + 1, 1, cao);
                            int xmax = KepNguyen((int)Math.Ceiling(k.X + k.W), xmin, rong);
                            int ymax = KepNguyen((int)Math.Ceiling(k.Y + k.H), ymin, cao);
                            goc.Add(new XElement("object",
                                new XElement("name", nhieuCongCu ? k.CongCu + "/" + k.Lop : k.Lop),
                                new XElement("pose", "Unspecified"),
                                new XElement("truncated", 0),
                                new XElement("difficult", 0),
                                new XElement("support", k.SoNguoi),
                                new XElement("bndbox",
                                    new XElement("xmin", xmin),
                                    new XElement("ymin", ymin),
                                    new XElement("xmax", xmax),
                                    new XElement("ymax", ymax))));
                            soKhung++;
                        }

                        Ghi(zip, "Annotations/" + s.SampleId + ".xml", new XDocument(goc).ToString());
                        danhSach.Append(s.SampleId).Append('\n');
                        anh.Append(s.SampleId).Append('\t').Append(s.StorageKey).Append('\n');
                    }

                    Ghi(zip, "ImageSets/Main/default.txt", danhSach.ToString());
                    Ghi(zip, "images.txt", anh.ToString());
                    Ghi(zip, "README.txt", GhiChu("Pascal VOC", kq.Samples.Count - boQua, boQua, soKhung,
                        "Moi file Annotations/*.xml mot anh; toa do pixel goc 1 (xmin, ymin, xmax, ymax)."));
                }

                return ms.ToArray();
            }
        }

        private static List<ToolDefinition> CongCuKhung(ProjectResultsResponse kq, LabelSchema tapNhan, string dinhDang)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            List<ToolDefinition> congCu = tapNhan.Tools.Where(t => t.Kind == ToolKinds.Bbox).ToList();
            if (tapNhan.Modality != Modalities.Image || congCu.Count == 0)
            {
                throw new InvalidValueException(
                    "khung_chi_cho_anh",
                    dinhDang + " chi xuat duoc cho du an anh co cong cu bbox (khung chu nhat). Da giac: dung coco.");
            }

            return congCu;
        }

        /// <summary>Ten lop YOLO theo thu tu: mot cong cu → ten lop; nhieu cong cu → "cong_cu/lop".</summary>
        private static List<string> TenLop(List<ToolDefinition> congCu)
        {
            List<string> ten = new List<string>();
            foreach (ToolDefinition t in congCu)
            {
                foreach (string lop in t.Classes)
                {
                    ten.Add(congCu.Count > 1 ? t.Name + "/" + lop : lop);
                }
            }

            return ten;
        }

        private static Dictionary<string, int> MaLop(List<ToolDefinition> congCu)
        {
            Dictionary<string, int> ma = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ToolDefinition t in congCu)
            {
                foreach (string lop in t.Classes)
                {
                    ma[t.Name + "/" + lop] = ma.Count;
                }
            }

            return ma;
        }

        private static List<KhungNhan> CacKhung(SampleResultResponse s, List<ToolDefinition> congCu)
        {
            // Khung DA GOP cua tung cong cu (tools[ten].final) — xem ResultAggregator.
            List<KhungNhan> ds = new List<KhungNhan>();
            foreach (ToolDefinition t in congCu)
            {
                foreach (ResultExporter.DoiTuongChot d in ResultExporter.DoiTuongDaGop(s, t))
                {
                    JsonObject m = d.DoiTuong;
                    ds.Add(new KhungNhan
                    {
                        CongCu = t.Name,
                        Lop = m["labelId"]!.GetValue<string>(),
                        X = m["x"]!.GetValue<double>(),
                        Y = m["y"]!.GetValue<double>(),
                        W = m["w"]!.GetValue<double>(),
                        H = m["h"]!.GetValue<double>(),
                        SoNguoi = d.SoNguoi,
                    });
                }
            }

            return ds;
        }

        private static void Ghi(ZipArchive zip, string duong, string noiDung)
        {
            ZipArchiveEntry e = zip.CreateEntry(duong, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] b = Encoding.UTF8.GetBytes(noiDung);
                s.Write(b, 0, b.Length);
            }
        }

        private static string GhiChu(string dinhDang, int soAnh, int boQua, int soKhung, string moTa)
        {
            return dinhDang + " — " + soAnh + " anh, " + soKhung + " khung.\n"
                + moTa + "\n"
                + "Moi khung la mot doi tuong DA GOP tu cac nhan da duyet (ghep cum theo IoU, lay trung vi toa do). Mau tranh chap khong co khung.\n"
                + "File anh khong nam trong zip — images.txt: sampleId <TAB> khoa file trong kho.\n"
                + (boQua > 0 ? "Bo qua " + boQua + " anh khong co kich thuoc trong metadata.\n" : string.Empty);
        }

        private static double KepDoan(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        private static int KepNguyen(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static string So(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private sealed class KhungNhan
        {
            public string CongCu { get; init; } = string.Empty;

            public string Lop { get; init; } = string.Empty;

            public double X { get; init; }

            public double Y { get; init; }

            public double W { get; init; }

            public double H { get; init; }

            /// <summary>So nguoi ve khung nay (trong cac nhan da duyet).</summary>
            public int SoNguoi { get; init; }
        }
    }
}
