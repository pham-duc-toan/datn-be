using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Xuat ket qua du an ra file (FB-25):
    ///
    ///   json — day du: tap nhan, ket qua gop tung cong cu, cac nhan da duyet.
    ///   csv  — mot dong mot mau, mot cot mot cong cu, ghi ket qua GOP: lop / lua chon / van ban / so /
    ///          thu tu xep hang; cong cu dang danh sach ghi JSON cac doi tuong da gop. Tranh chap de
    ///          trong. Cong cu khong gop (freeText) ghi JSON cac nhan da duyet (moi nguoi mot phan tu).
    ///   coco — chi du an ANH co cong cu bbox / polygon / mask. Moi doi tuong DA GOP la mot annotation
    ///          COCO (mat na: segmentation la RLE khong nen).
    /// </summary>
    public static class ResultExporter
    {
        public static byte[] Csv(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            bool coNoiDung = !Modalities.LaFile(tapNhan.Modality);

            StringBuilder sb = new StringBuilder();
            sb.Append("sample_id,").Append(coNoiDung ? "content" : "storage_key").Append(",approved_count,disputed");
            foreach (ToolDefinition t in tapNhan.Tools)
            {
                sb.Append(',').Append(OCsv(t.Name));
            }

            sb.Append('\n');

            foreach (SampleResultResponse s in kq.Samples)
            {
                string nguon = coNoiDung
                    ? (s.SampleContent == null ? string.Empty : s.SampleContent.Json)
                    : (s.StorageKey ?? string.Empty);

                sb.Append(s.SampleId).Append(',')
                  .Append(OCsv(nguon)).Append(',')
                  .Append(s.ApprovedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Disputed ? "true" : "false");

                JsonObject tools = (JsonObject)s.Tools.Node();
                foreach (ToolDefinition t in tapNhan.Tools)
                {
                    sb.Append(',').Append(OCsv(GiaTriCongCu(t, tools[t.Name], s.Labels)));
                }

                sb.Append('\n');
            }

            // BOM UTF-8: Excel mo file tieng Viet khong bi loi font.
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
            byte[] than = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] tatCa = new byte[bom.Length + than.Length];
            Buffer.BlockCopy(bom, 0, tatCa, 0, bom.Length);
            Buffer.BlockCopy(than, 0, tatCa, bom.Length, than.Length);
            return tatCa;
        }

        public static byte[] Coco(ProjectResultsResponse kq, LabelSchema tapNhan)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            List<ToolDefinition> congCuKhung = tapNhan.Tools
                .Where(t => t.Kind == ToolKinds.Bbox || t.Kind == ToolKinds.Polygon || t.Kind == ToolKinds.Mask)
                .ToList();

            if (tapNhan.Modality != Modalities.Image || congCuKhung.Count == 0)
            {
                throw new InvalidValueException(
                    "coco_chi_cho_khung_anh",
                    "COCO chỉ xuất được cho dự án ảnh có công cụ bbox / polygon / mask. Dùng json hoặc csv.");
            }

            // Danh muc: moi (cong cu, lop) mot id. supercategory = ten cong cu.
            JsonArray categories = new JsonArray();
            Dictionary<string, int> maDanhMuc = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ToolDefinition t in congCuKhung)
            {
                foreach (string lop in t.Classes)
                {
                    int id = maDanhMuc.Count + 1;
                    maDanhMuc[t.Name + "/" + lop] = id;
                    categories.Add(new JsonObject { ["id"] = id, ["name"] = lop, ["supercategory"] = t.Name });
                }
            }

            JsonArray images = new JsonArray();
            JsonArray annotations = new JsonArray();
            int maAnh = 0;
            int maNhan = 0;
            int soTranhChap = 0;

            foreach (SampleResultResponse s in kq.Samples)
            {
                maAnh++;
                SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);

                JsonObject anh = new JsonObject
                {
                    ["id"] = maAnh,
                    ["file_name"] = s.StorageKey,
                    ["sample_id"] = s.SampleId.ToString(),
                    ["disputed"] = s.Disputed,
                };

                if (md.Width.HasValue && md.Height.HasValue)
                {
                    anh["width"] = md.Width.Value;
                    anh["height"] = md.Height.Value;
                }

                images.Add(anh);
                if (s.Disputed)
                {
                    soTranhChap++;
                }

                foreach (ToolDefinition t in congCuKhung)
                {
                    foreach (DoiTuongChot d in DoiTuongDaGop(s, t))
                    {
                        maNhan++;
                        int danhMuc = maDanhMuc[t.Name + "/" + d.DoiTuong["labelId"]!.GetValue<string>()];
                        JsonObject nhan;
                        if (t.Kind == ToolKinds.Bbox)
                        {
                            nhan = KhungCoco(d.DoiTuong);
                        }
                        else if (t.Kind == ToolKinds.Polygon)
                        {
                            nhan = DaGiacCoco(d.DoiTuong);
                        }
                        else
                        {
                            nhan = MatNaCoco(d.DoiTuong);
                        }

                        nhan["id"] = maNhan;
                        nhan["image_id"] = maAnh;
                        nhan["category_id"] = danhMuc;
                        nhan["iscrowd"] = 0;
                        nhan["support"] = d.SoNguoi;

                        JsonNode? thuocTinh = d.DoiTuong["attributes"];
                        if (thuocTinh != null)
                        {
                            nhan["attributes"] = thuocTinh.DeepClone();
                        }

                        annotations.Add(nhan);
                    }
                }
            }

            JsonObject coco = new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["description"] = "Kết quả dự án " + kq.ProjectId,
                    ["note"] = "Moi annotation la mot doi tuong DA GOP tu cac nhan da duyet (ghep cum theo IoU; support = so nguoi ve). "
                               + "Ảnh tranh chấp (disputed = true, " + soTranhChap + " ảnh) không có annotation của công cụ tranh chấp.",
                },
                ["images"] = images,
                ["annotations"] = annotations,
                ["categories"] = categories,
            };

            return Encoding.UTF8.GetBytes(coco.ToJsonString());
        }

        /// <summary>Mot doi tuong trong ket qua gop cua mot cong cu dang danh sach + so nguoi ve no.</summary>
        internal sealed class DoiTuongChot
        {
            public DoiTuongChot(JsonObject doiTuong, int soNguoi)
            {
                DoiTuong = doiTuong;
                SoNguoi = soNguoi;
            }

            public JsonObject DoiTuong { get; }

            public int SoNguoi { get; }
        }

        /// <summary>
        /// Cac doi tuong DA GOP cua mot cong cu dang danh sach tren mot mau (tools[ten].final). Rong khi
        /// chua ai lam cong cu nay, hoac cong cu tranh chap (final = null).
        /// </summary>
        internal static List<DoiTuongChot> DoiTuongDaGop(SampleResultResponse s, ToolDefinition t)
        {
            List<DoiTuongChot> ds = new List<DoiTuongChot>();
            JsonObject? gop = s.Tools.Node()[t.Name] as JsonObject;
            JsonArray? cuoi = gop == null ? null : gop["final"] as JsonArray;
            if (cuoi == null)
            {
                return ds;
            }

            JsonArray? ungHo = gop!["supports"] as JsonArray;
            for (int i = 0; i < cuoi.Count; i++)
            {
                int soNguoi = ungHo != null && i < ungHo.Count ? ungHo[i]!.GetValue<int>() : 0;
                ds.Add(new DoiTuongChot((JsonObject)cuoi[i]!, soNguoi));
            }

            return ds;
        }

        private static JsonObject KhungCoco(JsonObject m)
        {
            double x = m["x"]!.GetValue<double>();
            double y = m["y"]!.GetValue<double>();
            double w = m["w"]!.GetValue<double>();
            double h = m["h"]!.GetValue<double>();

            return new JsonObject
            {
                ["bbox"] = new JsonArray(x, y, w, h),
                ["area"] = w * h,
                ["segmentation"] = new JsonArray(),
            };
        }

        private static JsonObject DaGiacCoco(JsonObject m)
        {
            List<(double X, double Y)> diem = new List<(double X, double Y)>();
            foreach (JsonNode? p in (JsonArray)m["points"]!)
            {
                diem.Add((p![0]!.GetValue<double>(), p[1]!.GetValue<double>()));
            }

            JsonArray phang = new JsonArray();
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double dienTich2 = 0;

            for (int i = 0; i < diem.Count; i++)
            {
                (double X, double Y) a = diem[i];
                (double X, double Y) b = diem[(i + 1) % diem.Count];

                phang.Add(a.X);
                phang.Add(a.Y);
                minX = Math.Min(minX, a.X);
                minY = Math.Min(minY, a.Y);
                maxX = Math.Max(maxX, a.X);
                maxY = Math.Max(maxY, a.Y);

                // Cong thuc shoelace.
                dienTich2 = dienTich2 + (a.X * b.Y - b.X * a.Y);
            }

            return new JsonObject
            {
                ["segmentation"] = new JsonArray(phang),
                ["bbox"] = new JsonArray(minX, minY, maxX - minX, maxY - minY),
                ["area"] = Math.Abs(dienTich2) / 2,
            };
        }

        /// <summary>Mat na: segmentation la RLE khong nen cua COCO ({"size":[cao, rong],"counts":[...]}) — dung luon dinh dang cua nhan.</summary>
        private static JsonObject MatNaCoco(JsonObject m)
        {
            double[] hop = MatNa.HopBao(m) ?? new double[] { 0, 0, 0, 0 };
            return new JsonObject
            {
                ["segmentation"] = m["rle"]!.DeepClone(),
                ["bbox"] = new JsonArray(hop[0], hop[1], hop[2], hop[3]),
                ["area"] = MatNa.DienTich(m),
            };
        }

        /// <summary>Gia tri o CSV cua mot cong cu: ket qua GOP; cong cu khong gop (freeText) ghi JSON cac nhan da duyet.</summary>
        private static string GiaTriCongCu(ToolDefinition t, JsonNode? gop, IReadOnlyList<RawJson> nhan)
        {
            string cachGop = gop == null || gop["method"] == null ? "none" : gop["method"]!.GetValue<string>();

            if (cachGop == "none")
            {
                JsonArray moiNguoi = new JsonArray();
                foreach (RawJson n in nhan)
                {
                    JsonNode? k = n.Node()[t.Name];
                    moiNguoi.Add(k == null ? null : k.DeepClone());
                }

                return moiNguoi.ToJsonString();
            }

            JsonNode? final = gop!["final"];
            if (final == null)
            {
                // Tranh chap: khong co ket qua (cot "disputed" = true).
                return string.Empty;
            }

            if (t.Kind == ToolKinds.Classification)
            {
                return string.Join(";", ((JsonArray)final["labelIds"]!).Select(n => n!.GetValue<string>()));
            }

            if (t.Kind == ToolKinds.Pairwise)
            {
                return final["choice"]!.GetValue<string>();
            }

            if (t.Kind == ToolKinds.Transcription)
            {
                return final["text"]!.GetValue<string>();
            }

            if (t.Kind == ToolKinds.Number)
            {
                return final["value"]!.ToJsonString();
            }

            if (t.Kind == ToolKinds.Ranking)
            {
                return string.Join(";", ((JsonArray)final["order"]!).Select(n => n!.ToJsonString()));
            }

            // Cong cu dang danh sach: JSON cac doi tuong da gop.
            return final.ToJsonString();
        }

        /// <summary>
        /// Mot o CSV an toan:
        ///   - boc ngoac kep neu co dau phay/ngoac kep/xuong dong;
        ///   - CHONG CSV INJECTION: o bat dau bang = + - @ se bi Excel chay nhu
        ///     CONG THUC (vd ten lop "=HYPERLINK(...)" do doanh nghiep khac dat).
        ///     Chen dau ' phia truoc de Excel coi la chu.
        /// </summary>
        internal static string OCsv(string giaTri)
        {
            string v = giaTri;

            if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '-' || v[0] == '@'))
            {
                v = "'" + v;
            }

            if (v.Contains(',', StringComparison.Ordinal) || v.Contains('"', StringComparison.Ordinal)
                || v.Contains('\n', StringComparison.Ordinal) || v.Contains('\r', StringComparison.Ordinal))
            {
                v = "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            }

            return v;
        }
    }
}
