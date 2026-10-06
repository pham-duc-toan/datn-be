using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            Kiem(TapNhan.Value, json, "tap_nhan_sai_dinh_dang", "Tap nhan sai dinh dang");
        }

        public static void KiemNhan(LabelSchema tapNhan, JsonElement duLieu)
        {
            JsonSchema schema = SchemaNhanTheoTapNhan.GetOrAdd(tapNhan.ToRawJson().Json, _ => GhepSchemaNhan(tapNhan));
            Kiem(schema, duLieu, "nhan_sai_dinh_dang", "Nhan sai dinh dang");
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

        private static void Kiem(JsonSchema schema, JsonElement json, string code, string tieuDe)
        {
            EvaluationResults kq = schema.Evaluate(json, TuyChon);
            if (kq.IsValid)
            {
                return;
            }

            throw new LabelFormatException(code, tieuDe + ": " + TomTatLoi(kq));
        }

        /// <summary>Gom toi da 3 loi dau tien, kem vi tri trong JSON — doc la biet sai cho nao.</summary>
        private static string TomTatLoi(EvaluationResults kq)
        {
            List<string> dong = new List<string>();
            IReadOnlyList<EvaluationResults>? chiTiet = kq.Details;

            if (chiTiet != null)
            {
                foreach (EvaluationResults mot in chiTiet)
                {
                    if (mot.Errors == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, string> loi in mot.Errors)
                    {
                        string viTri = mot.InstanceLocation.ToString();
                        dong.Add((viTri.Length == 0 ? "(goc)" : viTri) + ": " + loi.Value);
                        if (dong.Count >= 3)
                        {
                            return string.Join("; ", dong);
                        }
                    }
                }
            }

            return dong.Count == 0 ? "khong khop schema" : string.Join("; ", dong);
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
