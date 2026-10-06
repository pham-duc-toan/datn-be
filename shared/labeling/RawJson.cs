using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Crowd.Labeling
{
    /// <summary>
    /// Mot khoi JSON tuy y, BAT BIEN, so sanh THEO GIA TRI — dung cho nhung thu
    /// hinh dang thay doi theo loai du lieu: metadata mau, noi dung mau (text, cap
    /// cau tra loi), tap nhan cua du an.
    ///
    /// Vi sao khong dung JsonElement thang: JsonElement so sanh theo tham chieu,
    /// lam hong record event (can so sanh theo gia tri khi test round-trip).
    ///
    /// Tren day la JSON LONG NHAU that (khong phai chuoi chua JSON). Luu xuong
    /// database la cot jsonb.
    /// </summary>
    [JsonConverter(typeof(RawJsonConverter))]
    public sealed class RawJson : IEquatable<RawJson>
    {
        private RawJson(string json)
        {
            Json = json;
        }

        /// <summary>JSON gon (khong khoang trang thua), thu tu khoa SAP XEP — hai khoi cung y nghia ra cung chuoi.</summary>
        public string Json { get; }

        public static RawJson Tu(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LabelFormatException("JSON rong.");
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new LabelFormatException("Khong phai JSON hop le.", ex);
            }

            return Tu(node);
        }

        public static RawJson Tu(JsonElement element)
        {
            return Tu(element.GetRawText());
        }

        public static RawJson Tu(JsonNode? node)
        {
            if (node == null)
            {
                throw new LabelFormatException("JSON null.");
            }

            return new RawJson(JsonCanonical.Viet(node));
        }

        /// <summary>Ban sao doc lap de doc / sua (khong anh huong ban goc).</summary>
        public JsonNode Node()
        {
            JsonNode? n = JsonNode.Parse(Json);
            if (n == null)
            {
                throw new InvalidOperationException("RawJson hong.");
            }

            return n;
        }

        public JsonElement Element()
        {
            using (JsonDocument doc = JsonDocument.Parse(Json))
            {
                return doc.RootElement.Clone();
            }
        }

        public bool Equals(RawJson? other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(Json, other.Json, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RawJson);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Json);
        }

        public override string ToString()
        {
            return Json;
        }
    }

    public sealed class RawJsonConverter : JsonConverter<RawJson>
    {
        public override RawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return RawJson.Tu(doc.RootElement);
            }
        }

        public override void Write(Utf8JsonWriter writer, RawJson value, JsonSerializerOptions options)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (JsonDocument doc = JsonDocument.Parse(value.Json))
            {
                doc.RootElement.WriteTo(writer);
            }
        }
    }
}
