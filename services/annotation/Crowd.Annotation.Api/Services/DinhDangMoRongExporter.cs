using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Cac dinh dang xuat bo sung (FB-25), cung nguyen tac voi COCO / YOLO / VOC: moi doi tuong la ket qua DA
    /// GOP tu cac nhan da duyet; mau tranh chap khong co doi tuong cua cong cu tranh chap.
    ///
    ///   yolo-seg       — da giac (polygon) cua du an anh: labels/*.txt "lop x1 y1 x2 y2 ..." chuan hoa 0-1
    ///                    (dinh dang phan doan cua Ultralytics).
    ///   yolo-obb       — khung (ke ca khung xoay) cua du an anh: "lop x1 y1 x2 y2 x3 y3 x4 y4" chuan hoa 0-1,
    ///                    bon dinh theo chieu kim dong ho (dinh dang OBB cua Ultralytics).
    ///   coco-keypoints — diem moc (keypoint) cua du an anh. Tap nhan khong co khai niem "the hien" (nguoi thu
    ///                    nhat, nguoi thu hai...) nen gom theo THU TU: the hien k gom diem thu k (xep trai → phai)
    ///                    cua moi lop; lop thieu diem → v = 0.
    ///   conll          — doan van (span / NER) cua du an van ban: moi dong "tu&lt;TAB&gt;nhan BIO", cau cach nhau dong trong.
    ///                    Tu tach theo khoang trang; doan cat giua tu thi tu do van nhan nhan.
    ///   mot            — chuoi khung (track) cua du an video: MOT Challenge "khung,id,trai,tren,rong,cao,1,-1,-1,-1"
    ///                    moi lop mot file gt/{lop}.txt, noi suy tung khung theo fps (metadata, mac dinh 25).
    /// </summary>
    public static class DinhDangMoRongExporter
    {
        private const double FpsMacDinh = 25;

        // =====================================================================
        // YOLO segmentation
        // =====================================================================

        public static byte[] YoloSeg(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCu(kq, tapNhan, Modalities.Image, ToolKinds.Polygon, "yolo-seg", "anh co cong cu polygon");
            return ZipYolo(kq, congCu, "YOLO segmentation", "Moi dong: <lop> <x1> <y1> <x2> <y2> ... (dinh da giac, chuan hoa 0-1).",
                (d, rong, cao) =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (JsonNode? p in (JsonArray)d["points"]!)
                    {
                        sb.Append(' ').Append(So(Kep(p![0]!.GetValue<double>() / rong))).Append(' ').Append(So(Kep(p[1]!.GetValue<double>() / cao)));
                    }

                    return sb.ToString();
                });
        }

        // =====================================================================
        // YOLO OBB (khung xoay)
        // =====================================================================

        public static byte[] YoloObb(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCu(kq, tapNhan, Modalities.Image, ToolKinds.Bbox, "yolo-obb", "anh co cong cu bbox");
            return ZipYolo(kq, congCu, "YOLO OBB", "Moi dong: <lop> <x1> <y1> ... <x4> <y4> (bon dinh khung, ke ca khung xoay, chuan hoa 0-1).",
                (d, rong, cao) =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (double[] p in DinhKhung(d))
                    {
                        sb.Append(' ').Append(So(Kep(p[0] / rong))).Append(' ').Append(So(Kep(p[1] / cao)));
                    }

                    return sb.ToString();
                });
        }

        /// <summary>Bon dinh khung (x, y, w, h, angle?) — cung cong thuc voi Crowd.Labeling (xoay quanh tam).</summary>
        public static List<double[]> DinhKhung(JsonObject d)
        {
            double x = d["x"]!.GetValue<double>();
            double y = d["y"]!.GetValue<double>();
            double w = d["w"]!.GetValue<double>();
            double h = d["h"]!.GetValue<double>();
            double goc = d["angle"] == null ? 0 : d["angle"]!.GetValue<double>();
            double cx = x + (w / 2);
            double cy = y + (h / 2);
            double rad = goc * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double[][] lech = new double[][]
            {
                new double[] { -w / 2, -h / 2 },
                new double[] { w / 2, -h / 2 },
                new double[] { w / 2, h / 2 },
                new double[] { -w / 2, h / 2 },
            };

            List<double[]> ds = new List<double[]>();
            foreach (double[] l in lech)
            {
                ds.Add(new double[] { cx + (l[0] * cos) - (l[1] * sin), cy + (l[0] * sin) + (l[1] * cos) });
            }

            return ds;
        }

        // =====================================================================
        // COCO keypoints
        // =====================================================================

        public static byte[] CocoKeypoints(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCu(kq, tapNhan, Modalities.Image, ToolKinds.Keypoint, "coco-keypoints", "anh co cong cu keypoint");

            JsonArray categories = new JsonArray();
            for (int i = 0; i < congCu.Count; i++)
            {
                categories.Add(new JsonObject
                {
                    ["id"] = i + 1,
                    ["name"] = congCu[i].Name,
                    ["supercategory"] = congCu[i].Name,
                    ["keypoints"] = new JsonArray(congCu[i].Classes.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                    ["skeleton"] = new JsonArray(),
                });
            }

            JsonArray images = new JsonArray();
            JsonArray annotations = new JsonArray();
            int maAnh = 0;
            int maNhan = 0;
            foreach (SampleResultResponse s in kq.Samples)
            {
                maAnh++;
                SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);
                JsonObject anh = new JsonObject { ["id"] = maAnh, ["file_name"] = s.StorageKey, ["sample_id"] = s.SampleId.ToString(), ["disputed"] = s.Disputed };
                if (md.Width.HasValue && md.Height.HasValue)
                {
                    anh["width"] = md.Width.Value;
                    anh["height"] = md.Height.Value;
                }

                images.Add(anh);

                for (int ci = 0; ci < congCu.Count; ci++)
                {
                    ToolDefinition t = congCu[ci];
                    Dictionary<string, List<JsonObject>> theoLop = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
                    foreach (ResultExporter.DoiTuongChot d in ResultExporter.DoiTuongDaGop(s, t))
                    {
                        string lop = d.DoiTuong["labelId"]!.GetValue<string>();
                        List<JsonObject>? ds;
                        if (!theoLop.TryGetValue(lop, out ds))
                        {
                            ds = new List<JsonObject>();
                            theoLop[lop] = ds;
                        }

                        ds.Add(d.DoiTuong);
                    }

                    if (theoLop.Count == 0)
                    {
                        continue;
                    }

                    int soTheHien = theoLop.Values.Max(l => l.Count);
                    foreach (List<JsonObject> l in theoLop.Values)
                    {
                        l.Sort((a, b) => a["x"]!.GetValue<double>().CompareTo(b["x"]!.GetValue<double>()));
                    }

                    for (int k = 0; k < soTheHien; k++)
                    {
                        JsonArray diem = new JsonArray();
                        int soDiem = 0;
                        double minX = double.MaxValue;
                        double minY = double.MaxValue;
                        double maxX = double.MinValue;
                        double maxY = double.MinValue;
                        foreach (string lop in t.Classes)
                        {
                            List<JsonObject>? l;
                            if (theoLop.TryGetValue(lop, out l) && k < l.Count)
                            {
                                double x = l[k]["x"]!.GetValue<double>();
                                double y = l[k]["y"]!.GetValue<double>();
                                diem.Add(x);
                                diem.Add(y);
                                diem.Add(2);
                                soDiem++;
                                minX = Math.Min(minX, x);
                                minY = Math.Min(minY, y);
                                maxX = Math.Max(maxX, x);
                                maxY = Math.Max(maxY, y);
                            }
                            else
                            {
                                diem.Add(0);
                                diem.Add(0);
                                diem.Add(0);
                            }
                        }

                        maNhan++;
                        annotations.Add(new JsonObject
                        {
                            ["id"] = maNhan,
                            ["image_id"] = maAnh,
                            ["category_id"] = ci + 1,
                            ["keypoints"] = diem,
                            ["num_keypoints"] = soDiem,
                            ["bbox"] = new JsonArray(minX, minY, maxX - minX, maxY - minY),
                            ["area"] = (maxX - minX) * (maxY - minY),
                            ["iscrowd"] = 0,
                        });
                    }
                }
            }

            JsonObject coco = new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["description"] = "Điểm mốc dự án " + kq.ProjectId,
                    ["note"] = "Thể hiện k = điểm thứ k (trái sang phải) của mỗi lớp — tập nhãn không gom điểm theo thể hiện.",
                },
                ["images"] = images,
                ["annotations"] = annotations,
                ["categories"] = categories,
            };
            return Encoding.UTF8.GetBytes(coco.ToJsonString());
        }

        // =====================================================================
        // CoNLL (NER)
        // =====================================================================

        public static byte[] Conll(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCu(kq, tapNhan, Modalities.Text, ToolKinds.Span, "conll", "van ban co cong cu span");
            ToolDefinition t = congCu[0];
            StringBuilder sb = new StringBuilder();
            sb.Append("# CoNLL (BIO) — cong cu '").Append(t.Name).Append("', du an ").Append(kq.ProjectId).Append('\n');

            foreach (SampleResultResponse s in kq.Samples)
            {
                string van = s.SampleContent == null ? string.Empty : (s.SampleContent.Node()["text"] == null ? string.Empty : s.SampleContent.Node()["text"]!.GetValue<string>());
                List<int[]> doan = new List<int[]>();
                List<string> lop = new List<string>();
                foreach (ResultExporter.DoiTuongChot d in ResultExporter.DoiTuongDaGop(s, t))
                {
                    doan.Add(new int[] { d.DoiTuong["start"]!.GetValue<int>(), d.DoiTuong["end"]!.GetValue<int>() });
                    lop.Add(d.DoiTuong["labelId"]!.GetValue<string>());
                }

                sb.Append("# sample_id = ").Append(s.SampleId).Append(s.Disputed ? " (tranh chap)" : string.Empty).Append('\n');
                foreach (KeyValuePair<int[], string> tu in GanBio(van, doan, lop))
                {
                    sb.Append(van, tu.Key[0], tu.Key[1] - tu.Key[0]).Append('\t').Append(tu.Value).Append('\n');
                }

                sb.Append('\n');
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>Tach tu theo khoang trang va gan BIO theo cac doan [start, end). Ham thuan (unit test).</summary>
        public static List<KeyValuePair<int[], string>> GanBio(string van, IReadOnlyList<int[]> doan, IReadOnlyList<string> lop)
        {
            List<KeyValuePair<int[], string>> ra = new List<KeyValuePair<int[], string>>();
            int i = 0;
            int doanTruoc = -1;
            while (i < van.Length)
            {
                if (char.IsWhiteSpace(van[i]))
                {
                    i++;
                    continue;
                }

                int dau = i;
                while (i < van.Length && !char.IsWhiteSpace(van[i]))
                {
                    i++;
                }

                int chiSo = -1;
                for (int k = 0; k < doan.Count; k++)
                {
                    if (dau < doan[k][1] && i > doan[k][0])
                    {
                        chiSo = k;
                        break;
                    }
                }

                string nhan = chiSo < 0 ? "O" : ((chiSo == doanTruoc ? "I-" : "B-") + lop[chiSo]);
                doanTruoc = chiSo;
                ra.Add(new KeyValuePair<int[], string>(new int[] { dau, i }, nhan));
            }

            return ra;
        }

        // =====================================================================
        // MOT
        // =====================================================================

        public static byte[] Mot(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            List<ToolDefinition> congCu = CongCu(kq, tapNhan, Modalities.Video, ToolKinds.Track, "mot", "video co cong cu track");
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    int maDoiTuong = 0;
                    StringBuilder chiMuc = new StringBuilder();
                    foreach (SampleResultResponse s in kq.Samples)
                    {
                        SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);
                        JsonNode? fpsNut = s.SampleMetadata.Node()["fps"];
                        double fps = fpsNut != null && fpsNut.GetValue<double>() > 0 ? fpsNut.GetValue<double>() : FpsMacDinh;
                        StringBuilder sb = new StringBuilder();
                        foreach (ToolDefinition t in congCu)
                        {
                            foreach (ResultExporter.DoiTuongChot d in ResultExporter.DoiTuongDaGop(s, t))
                            {
                                maDoiTuong++;
                                JsonArray moc = (JsonArray)d.DoiTuong["keyframes"]!;
                                double dau = moc[0]!["t"]!.GetValue<double>();
                                double cuoi = moc[moc.Count - 1]!["t"]!.GetValue<double>();
                                int khungDau = (int)Math.Ceiling(dau * fps);
                                int khungCuoi = (int)Math.Floor(cuoi * fps);
                                for (int f = khungDau; f <= khungCuoi; f++)
                                {
                                    double[]? k = KhungTai(moc, f / fps);
                                    if (k == null)
                                    {
                                        continue;
                                    }

                                    sb.Append((f + 1).ToString(CultureInfo.InvariantCulture)).Append(',')
                                      .Append(maDoiTuong.ToString(CultureInfo.InvariantCulture)).Append(',')
                                      .Append(So(k[0])).Append(',').Append(So(k[1])).Append(',')
                                      .Append(So(k[2])).Append(',').Append(So(k[3])).Append(",1,-1,-1,-1\n");
                                }

                                chiMuc.Append(maDoiTuong).Append('\t').Append(s.SampleId).Append('\t').Append(t.Name).Append('\t')
                                      .Append(d.DoiTuong["labelId"]!.GetValue<string>()).Append('\n');
                            }
                        }

                        Ghi(zip, s.SampleId + "/gt/gt.txt", sb.ToString());
                        Ghi(zip, s.SampleId + "/seqinfo.ini", "[Sequence]\nname=" + s.SampleId + "\nframeRate=" + So(fps)
                            + "\nimWidth=" + (md.Width.HasValue ? md.Width.Value.ToString(CultureInfo.InvariantCulture) : "0")
                            + "\nimHeight=" + (md.Height.HasValue ? md.Height.Value.ToString(CultureInfo.InvariantCulture) : "0") + "\n");
                    }

                    Ghi(zip, "objects.tsv", "id\tsample_id\ttool\tlabel\n" + chiMuc);
                    Ghi(zip, "README.txt", "MOT Challenge — moi mau mot thu muc {sampleId}/gt/gt.txt: khung,id,trai,tren,rong,cao,1,-1,-1,-1.\n"
                        + "Khung danh so tu 1, toa do pixel; noi suy tuyen tinh giua cac moc, doan AN (visible=false) khong co dong.\n"
                        + "objects.tsv: id doi tuong → mau, cong cu, lop.\n");
                }

                return ms.ToArray();
            }
        }

        /// <summary>Khung tai thoi diem t (noi suy, ton trong visible=false) — cung luat TrackTool.KhungTai.</summary>
        public static double[]? KhungTai(JsonArray moc, double t)
        {
            double dau = moc[0]!["t"]!.GetValue<double>();
            double cuoi = moc[moc.Count - 1]!["t"]!.GetValue<double>();
            if (t < dau - 1e-9 || t > cuoi + 1e-9)
            {
                return null;
            }

            for (int i = 1; i < moc.Count; i++)
            {
                JsonNode truoc = moc[i - 1]!;
                JsonNode sau = moc[i]!;
                if (t <= sau["t"]!.GetValue<double>() + 1e-9)
                {
                    if (truoc["visible"] != null && !truoc["visible"]!.GetValue<bool>())
                    {
                        return null;
                    }

                    double t0 = truoc["t"]!.GetValue<double>();
                    double t1 = sau["t"]!.GetValue<double>();
                    double f = t1 - t0 <= 0 ? 0 : (t - t0) / (t1 - t0);
                    f = f < 0 ? 0 : (f > 1 ? 1 : f);
                    return new double[]
                    {
                        NoiSuy(truoc, sau, "x", f), NoiSuy(truoc, sau, "y", f), NoiSuy(truoc, sau, "w", f), NoiSuy(truoc, sau, "h", f),
                    };
                }
            }

            JsonNode chot = moc[moc.Count - 1]!;
            if (chot["visible"] != null && !chot["visible"]!.GetValue<bool>())
            {
                return null;
            }

            return new double[] { chot["x"]!.GetValue<double>(), chot["y"]!.GetValue<double>(), chot["w"]!.GetValue<double>(), chot["h"]!.GetValue<double>() };
        }

        // =====================================================================

        private static double NoiSuy(JsonNode a, JsonNode b, string ten, double f)
        {
            double va = a[ten]!.GetValue<double>();
            return va + (f * (b[ten]!.GetValue<double>() - va));
        }

        private static List<ToolDefinition> CongCu(ProjectResultsResponse kq, LabelSchema tapNhan, string modality, string kind, string dinhDang, string dieuKien)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            List<ToolDefinition> ds = tapNhan.Tools.Where(t => t.Kind == kind).ToList();
            if (tapNhan.Modality != modality || ds.Count == 0)
            {
                throw new InvalidValueException("dinh_dang_khong_hop_du_an", dinhDang + " chỉ xuất cho dự án " + dieuKien + ". Dùng json hoặc csv.");
            }

            return ds;
        }

        private static byte[] ZipYolo(
            ProjectResultsResponse kq, List<ToolDefinition> congCu, string ten, string moTa, Func<JsonObject, double, double, string> toaDo)
        {
            Dictionary<string, int> maLop = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> tenLop = new List<string>();
            foreach (ToolDefinition t in congCu)
            {
                foreach (string lop in t.Classes)
                {
                    maLop[t.Name + "/" + lop] = maLop.Count;
                    tenLop.Add(congCu.Count > 1 ? t.Name + "/" + lop : lop);
                }
            }

            int boQua = 0;
            int soDoiTuong = 0;
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

                        StringBuilder dong = new StringBuilder();
                        foreach (ToolDefinition t in congCu)
                        {
                            foreach (ResultExporter.DoiTuongChot d in ResultExporter.DoiTuongDaGop(s, t))
                            {
                                dong.Append(maLop[t.Name + "/" + d.DoiTuong["labelId"]!.GetValue<string>()].ToString(CultureInfo.InvariantCulture))
                                    .Append(toaDo(d.DoiTuong, md.Width.Value, md.Height.Value)).Append('\n');
                                soDoiTuong++;
                            }
                        }

                        Ghi(zip, "labels/" + s.SampleId + ".txt", dong.ToString());
                        anh.Append(s.SampleId).Append('\t').Append(s.StorageKey).Append('\n');
                    }

                    Ghi(zip, "data.yaml", "# Du an " + kq.ProjectId + "\nnc: " + tenLop.Count.ToString(CultureInfo.InvariantCulture)
                        + "\nnames: [" + string.Join(", ", tenLop.Select(t => "'" + t.Replace("'", "''", StringComparison.Ordinal) + "'")) + "]\n");
                    Ghi(zip, "classes.txt", string.Join("\n", tenLop) + "\n");
                    Ghi(zip, "images.txt", anh.ToString());
                    Ghi(zip, "README.txt", ten + " — " + (kq.Samples.Count - boQua) + " anh, " + soDoiTuong + " doi tuong.\n" + moTa + "\n"
                        + "Moi doi tuong DA GOP tu cac nhan da duyet; mau tranh chap khong co doi tuong.\n"
                        + "File anh khong nam trong zip — images.txt: sampleId <TAB> khoa file trong kho.\n"
                        + (boQua > 0 ? "Bỏ qua " + boQua + " ảnh không có kích thước trong metadata.\n" : string.Empty));
                }

                return ms.ToArray();
            }
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

        private static double Kep(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        private static string So(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
