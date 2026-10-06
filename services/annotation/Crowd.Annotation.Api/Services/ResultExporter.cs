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
    ///   csv  — mot dong mot mau, mot cot mot cong cu. Cong cu gop theo da so
    ///          (phan loai, cap) ghi ket qua CHOT; cong cu khac ghi JSON cac nhan
    ///          da duyet (moi nguoi mot phan tu).
    ///   coco — chi du an ANH co cong cu bbox / polygon. Moi khung cua moi nhan da
    ///          duyet la mot annotation COCO (chua gop khung — xem ResultAggregator).
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
                .Where(t => t.Kind == ToolKinds.Bbox || t.Kind == ToolKinds.Polygon)
                .ToList();

            if (tapNhan.Modality != Modalities.Image || congCuKhung.Count == 0)
            {
                throw new InvalidValueException(
                    "coco_chi_cho_khung_anh",
                    "COCO chi xuat duoc cho du an anh co cong cu bbox / polygon. Dung json hoac csv.");
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

            foreach (SampleResultResponse s in kq.Samples)
            {
                maAnh++;
                SampleMetadata md = SampleMetadata.Tu(s.SampleMetadata);

                JsonObject anh = new JsonObject
                {
                    ["id"] = maAnh,
                    ["file_name"] = s.StorageKey,
                    ["sample_id"] = s.SampleId.ToString(),
                };

                if (md.Width.HasValue && md.Height.HasValue)
                {
                    anh["width"] = md.Width.Value;
                    anh["height"] = md.Height.Value;
                }

                images.Add(anh);

                for (int nguoi = 0; nguoi < s.Labels.Count; nguoi++)
                {
                    JsonObject nhan = (JsonObject)s.Labels[nguoi].Node();

                    foreach (ToolDefinition t in congCuKhung)
                    {
                        JsonArray? muc = nhan[t.Name] as JsonArray;
                        if (muc == null)
                        {
                            continue;
                        }

                        foreach (JsonNode? m in muc)
                        {
                            maNhan++;
                            int danhMuc = maDanhMuc[t.Name + "/" + m!["labelId"]!.GetValue<string>()];
                            annotations.Add(t.Kind == ToolKinds.Bbox
                                ? KhungCoco(maNhan, maAnh, danhMuc, nguoi, (JsonObject)m)
                                : DaGiacCoco(maNhan, maAnh, danhMuc, nguoi, (JsonObject)m));
                        }
                    }
                }
            }

            JsonObject coco = new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["description"] = "Ket qua du an " + kq.ProjectId,
                    ["note"] = "Moi khung cua moi nhan DA DUYET la mot annotation (chua gop khung giua nguoi gan).",
                },
                ["images"] = images,
                ["annotations"] = annotations,
                ["categories"] = categories,
            };

            return Encoding.UTF8.GetBytes(coco.ToJsonString());
        }

        private static JsonObject KhungCoco(int id, int maAnh, int danhMuc, int nguoi, JsonObject m)
        {
            double x = m["x"]!.GetValue<double>();
            double y = m["y"]!.GetValue<double>();
            double w = m["w"]!.GetValue<double>();
            double h = m["h"]!.GetValue<double>();

            return new JsonObject
            {
                ["id"] = id,
                ["image_id"] = maAnh,
                ["category_id"] = danhMuc,
                ["bbox"] = new JsonArray(x, y, w, h),
                ["area"] = w * h,
                ["segmentation"] = new JsonArray(),
                ["iscrowd"] = 0,
                ["annotator_index"] = nguoi,
            };
        }

        private static JsonObject DaGiacCoco(int id, int maAnh, int danhMuc, int nguoi, JsonObject m)
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
                ["id"] = id,
                ["image_id"] = maAnh,
                ["category_id"] = danhMuc,
                ["segmentation"] = new JsonArray(phang),
                ["bbox"] = new JsonArray(minX, minY, maxX - minX, maxY - minY),
                ["area"] = Math.Abs(dienTich2) / 2,
                ["iscrowd"] = 0,
                ["annotator_index"] = nguoi,
            };
        }

        /// <summary>Gia tri o CSV cua mot cong cu.</summary>
        private static string GiaTriCongCu(ToolDefinition t, JsonNode? gop, IReadOnlyList<RawJson> nhan)
        {
            JsonNode? final = gop == null ? null : gop["final"];

            if (t.Kind == ToolKinds.Classification)
            {
                if (final == null)
                {
                    return string.Empty;
                }

                return string.Join(";", ((JsonArray)final["labelIds"]!).Select(n => n!.GetValue<string>()));
            }

            if (t.Kind == ToolKinds.Pairwise)
            {
                return final == null ? string.Empty : final["choice"]!.GetValue<string>();
            }

            JsonArray moiNguoi = new JsonArray();
            foreach (RawJson n in nhan)
            {
                JsonNode? k = n.Node()[t.Name];
                moiNguoi.Add(k == null ? null : k.DeepClone());
            }

            return moiNguoi.ToJsonString();
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
