using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Crowd.Labeling.Schemas
{
    /// <summary>
    /// Kiem HINH DANG bang JSON Schema — cac file goc nam o contracts/labeling,
    /// duoc nhung vao assembly (xem Crowd.Labeling.csproj).
    ///
    /// Schema cho NHAN cua mot du an duoc GHEP luc chay tu tap nhan cua du an:
    ///     { type: object, additionalProperties: false,
    ///       required: [cac cong cu bat buoc],
    ///       properties: { &lt;ten cong cu&gt;: &lt;schema ket qua cua loai cong cu&gt; } }
    /// Ghep san trong bo nho (khong dung $ref cheo file) va cache theo tap nhan.
    /// </summary>
    internal static class JsonSchemas
    {
        private static readonly Lazy<JsonSchema> TapNhan = new Lazy<JsonSchema>(
            () => JsonSchema.FromText(DocTaiNguyen("schemas/label-schema.schema.json")));

        private static readonly ConcurrentDictionary<string, JsonObject> KetQuaCongCu = new ConcurrentDictionary<string, JsonObject>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, JsonSchema> SchemaNhanTheoTapNhan = new ConcurrentDictionary<string, JsonSchema>(StringComparer.Ordinal);

        private static readonly EvaluationOptions TuyChon = new EvaluationOptions { OutputFormat = OutputFormat.List };

        public static void KiemTapNhan(JsonElement json)
        {
            EvaluationResults kq = TapNhan.Value.Evaluate(json, TuyChon);
            if (kq.IsValid)
            {
                return;
            }

            throw new LabelFormatException("tap_nhan_sai_dinh_dang", "Tập nhãn chưa hợp lệ: " + TomTatLoi(kq, json, null));
        }

        public static void KiemNhan(LabelSchema tapNhan, JsonElement duLieu)
        {
            JsonSchema schema = SchemaNhanTheoTapNhan.GetOrAdd(tapNhan.ToRawJson().Json, _ => GhepSchemaNhan(tapNhan));
            EvaluationResults kq = schema.Evaluate(duLieu, TuyChon);
            if (kq.IsValid)
            {
                return;
            }

            throw new LabelFormatException("nhan_sai_dinh_dang", "Câu trả lời chưa đúng định dạng: " + TomTatLoi(kq, duLieu, tapNhan));
        }

        /// <summary>Schema ket qua cua mot loai cong cu (ban sao, sua thoai mai).</summary>
        public static JsonObject SchemaKetQua(string kind)
        {
            JsonObject goc = KetQuaCongCu.GetOrAdd(kind, k =>
            {
                JsonObject? o = JsonNode.Parse(DocTaiNguyen("schemas/tools/" + k + ".result.schema.json")) as JsonObject;
                if (o == null)
                {
                    throw new InvalidOperationException("Schema cong cu " + k + " hong.");
                }

                o.Remove("$schema");
                o.Remove("title");
                o.Remove("description");
                return o;
            });

            return (JsonObject)goc.DeepClone();
        }

        private static JsonSchema GhepSchemaNhan(LabelSchema tapNhan)
        {
            JsonObject properties = new JsonObject();
            JsonArray batBuoc = new JsonArray();

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                properties[t.Name] = SchemaKetQua(t.Kind);
                if (t.Required)
                {
                    batBuoc.Add(t.Name);
                }
            }

            JsonObject schema = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = batBuoc,
                ["properties"] = properties,
            };

            return JsonSchema.FromText(schema.ToJsonString());
        }

        /// <summary>
        /// Tu khoa GOP: loi cua chung chi la "co phan tu con sai" — loi that nam o vi tri con,
        /// nen bo qua de khong lap lai cau vo nghia kieu "Some properties did not match".
        /// </summary>
        private static readonly HashSet<string> TuKhoaGop = new HashSet<string>(StringComparer.Ordinal)
        {
            "properties", "items", "prefixItems", "allOf", "anyOf", "oneOf", "if", "then", "else", "$ref",
            "dependentSchemas", "contains", "not", "patternProperties", "unevaluatedProperties", "unevaluatedItems",
        };

        /// <summary>
        /// Gom toi da 3 loi THAT (bo loi gop), dich sang cau tieng Viet kem vi tri de doc la biet sai cho nao,
        /// vd "Công cụ thứ 2 (Phân loại) — danh sách lớp: cần ít nhất 2 mục."
        /// </summary>
        private static string TomTatLoi(EvaluationResults kq, JsonElement goc, LabelSchema? tapNhan)
        {
            List<string> dong = new List<string>();
            IReadOnlyList<EvaluationResults>? chiTiet = kq.Details;

            if (chiTiet != null)
            {
                foreach (EvaluationResults mot in chiTiet)
                {
                    // Dieu kien "if" khong thoa chi la re nhanh (vd kind khac "classification"), khong phai loi.
                    if (mot.Errors == null || NamTrongIf(mot.EvaluationPath.ToString()))
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, string> loi in mot.Errors)
                    {
                        if (TuKhoaGop.Contains(loi.Key))
                        {
                            continue;
                        }

                        string[] doan = TachViTri(mot.InstanceLocation.ToString());
                        string viTri = tapNhan == null ? ViTriTapNhan(doan, goc) : ViTriNhan(doan, tapNhan);
                        string cau = viTri + ": " + NoiDungLoi(loi.Key, loi.Value, doan);
                        if (!dong.Contains(cau))
                        {
                            dong.Add(cau);
                        }

                        if (dong.Count >= 3)
                        {
                            return string.Join("; ", dong);
                        }
                    }
                }
            }

            return dong.Count == 0 ? "không đúng cấu trúc." : string.Join("; ", dong);
        }

        private static bool NamTrongIf(string duongDanDanhGia)
        {
            return duongDanDanhGia.Contains("/if/", StringComparison.Ordinal) || duongDanDanhGia.EndsWith("/if", StringComparison.Ordinal);
        }

        private static string[] TachViTri(string conTro)
        {
            if (string.IsNullOrEmpty(conTro) || conTro == "/")
            {
                return Array.Empty<string>();
            }

            string[] doan = conTro.TrimStart('/').Split('/');
            for (int i = 0; i < doan.Length; i++)
            {
                doan[i] = doan[i].Replace("~1", "/").Replace("~0", "~");
            }

            return doan;
        }

        /// <summary>/tools/1/classes/0 -&gt; "Công cụ thứ 2 (Phân loại) — lớp thứ 1".</summary>
        private static string ViTriTapNhan(string[] doan, JsonElement goc)
        {
            if (doan.Length == 0)
            {
                return "Tập nhãn";
            }

            if (doan[0] != "tools" || doan.Length == 1)
            {
                return VietHoa(TenTruong(doan[0]));
            }

            int chiSo;
            if (!int.TryParse(doan[1], out chiSo))
            {
                return "Danh sách công cụ";
            }

            string congCu = "Công cụ thứ " + (chiSo + 1);
            string? kind = LoaiCongCu(goc, chiSo);
            if (kind != null)
            {
                congCu = congCu + " (" + TenHienThi.CuaCongCu(kind) + ")";
            }

            if (doan.Length == 2)
            {
                return congCu;
            }

            string phan = TenTruong(doan[2]);
            int con;
            if (doan.Length >= 4 && int.TryParse(doan[3], out con))
            {
                if (doan[2] == "classes")
                {
                    phan = "lớp thứ " + (con + 1);
                }
                else if (doan[2] == "attributes")
                {
                    phan = "thuộc tính thứ " + (con + 1);
                    if (doan.Length >= 5)
                    {
                        phan = phan + " — " + (doan[4] == "options" && doan.Length >= 6 ? "lựa chọn thứ " + (int.TryParse(doan[5], out con) ? con + 1 : 0) : TenTruong(doan[4]));
                    }
                }
            }

            return congCu + " — " + phan;
        }

        /// <summary>/&lt;ten cong cu&gt;/... -&gt; "Khung chữ nhật" (+ "đối tượng thứ n").</summary>
        private static string ViTriNhan(string[] doan, LabelSchema tapNhan)
        {
            if (doan.Length == 0)
            {
                return "Bài làm";
            }

            ToolDefinition? t = tapNhan.Tool(doan[0]);
            string ten = t == null ? "Câu trả lời" : TenHienThi.CuaCongCu(t.Kind);
            int con;
            if (doan.Length >= 2 && int.TryParse(doan[1], out con))
            {
                ten = ten + " — đối tượng thứ " + (con + 1);
            }

            return ten;
        }

        private static string? LoaiCongCu(JsonElement goc, int chiSo)
        {
            JsonElement tools;
            if (goc.ValueKind != JsonValueKind.Object || !goc.TryGetProperty("tools", out tools) || tools.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (chiSo < 0 || chiSo >= tools.GetArrayLength())
            {
                return null;
            }

            JsonElement kind;
            JsonElement t = tools[chiSo];
            if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("kind", out kind) && kind.ValueKind == JsonValueKind.String)
            {
                return kind.GetString();
            }

            return null;
        }

        private static string TenTruong(string truong)
        {
            string? ten = TenHienThi.CuaTruong(truong);
            return ten ?? "trường “" + truong + "”";
        }

        private static string VietHoa(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Dich mot loi cua JsonSchema.Net (tu khoa + cau tieng Anh) sang tieng Viet.</summary>
        private static string NoiDungLoi(string tuKhoa, string goc, string[] doan)
        {
            string? so = SoCuoi(goc);
            string cuoi = doan.Length == 0 ? "" : doan[doan.Length - 1];
            switch (tuKhoa)
            {
                case "required":
                    return "thiếu " + DanhSachTruong(goc) + ".";
                case "minItems":
                    return "cần ít nhất " + (so ?? "1") + (cuoi == "classes" ? " lớp." : " mục.");
                case "maxItems":
                    return "tối đa " + (so ?? "") + (cuoi == "classes" ? " lớp." : " mục.");
                case "minLength":
                    return so == "1" || so == null ? "không được để trống." : "cần ít nhất " + so + " ký tự.";
                case "maxLength":
                    return "tối đa " + (so ?? "") + " ký tự.";
                case "minimum":
                case "exclusiveMinimum":
                    return "phải từ " + (so ?? "") + " trở lên.";
                case "maximum":
                case "exclusiveMaximum":
                    return "không được lớn hơn " + (so ?? "") + ".";
                case "pattern":
                    return "chỉ gồm chữ thường không dấu, số và dấu gạch dưới, bắt đầu bằng chữ (tối đa 40 ký tự).";
                case "type":
                    return "sai kiểu — cần " + TenKieu(goc) + ".";
                case "enum":
                case "const":
                    return "giá trị không nằm trong danh sách cho phép.";
                case "uniqueItems":
                    return "có mục bị trùng.";
                case "additionalProperties":
                    return "có trường không được phép.";
                case "minProperties":
                    return "không được để trống.";
                default:
                    // Schema "false" (truong khong dung duoc o vi tri nay, vd "classes" cho Câu trả lời mở).
                    if (goc.Contains("false schema", StringComparison.OrdinalIgnoreCase))
                    {
                        return "không dùng được ở đây.";
                    }

                    return "không hợp lệ.";
            }
        }

        /// <summary>So cuoi cung trong cau loi (gioi han), vd "... expected at least 2" -&gt; "2".</summary>
        private static string? SoCuoi(string cau)
        {
            MatchCollection ms = Regex.Matches(cau, @"-?\d+(\.\d+)?");
            return ms.Count == 0 ? null : ms[ms.Count - 1].Value;
        }

        /// <summary>Required properties ["a","b"] are not present -&gt; "danh sách lớp, công cụ đoạn gốc".</summary>
        private static string DanhSachTruong(string cau)
        {
            MatchCollection ms = Regex.Matches(cau, "\"([^\"]+)\"");
            List<string> ten = new List<string>();
            foreach (Match m in ms)
            {
                ten.Add(TenHienThi.CuaTruong(m.Groups[1].Value) ?? "“" + m.Groups[1].Value + "”");
            }

            return ten.Count == 0 ? "trường bắt buộc" : string.Join(", ", ten);
        }

        /// <summary>Value is "string" but should be "integer" -&gt; "số nguyên".</summary>
        private static string TenKieu(string cau)
        {
            MatchCollection ms = Regex.Matches(cau, "\"([a-z]+)\"");
            string kieu = ms.Count == 0 ? "" : ms[ms.Count - 1].Groups[1].Value;
            switch (kieu)
            {
                case "integer":
                    return "số nguyên";
                case "number":
                    return "số";
                case "string":
                    return "chữ";
                case "boolean":
                    return "đúng / sai";
                case "array":
                    return "danh sách";
                case "object":
                    return "đối tượng";
                default:
                    return "kiểu khác";
            }
        }

        private static string DocTaiNguyen(string ten)
        {
            Assembly a = typeof(JsonSchemas).Assembly;
            using (Stream? s = a.GetManifestResourceStream(ten))
            {
                if (s == null)
                {
                    throw new InvalidOperationException("Thieu tai nguyen nhung " + ten + " — kiem Crowd.Labeling.csproj.");
                }

                using (StreamReader r = new StreamReader(s))
                {
                    return r.ReadToEnd();
                }
            }
        }
    }
}
