using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Labeling
{
    /// <summary>
    /// Viet JSON o dang CHUAN: khong khoang trang, khoa cua moi object sap xep
    /// theo thu tu ky tu. Hai JSON cung y nghia (khac thu tu khoa, khac khoang
    /// trang — vd Postgres tra jsonb ve co them dau cach) ra cung mot chuoi, nen
    /// so sanh chuoi la du.
    ///
    /// Thu tu phan tu trong MANG giu nguyen — mang co nghia thu tu (diem cua da
    /// giac). Cong cu nao coi mang la tap hop (danh sach khung) thi tu sap xep
    /// truoc khi goi vao day.
    /// </summary>
    public static class JsonCanonical
    {
        public static string Viet(JsonNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            using (MemoryStream ra = new MemoryStream())
            {
                using (Utf8JsonWriter w = new Utf8JsonWriter(ra, new JsonWriterOptions { Indented = false }))
                {
                    Ghi(w, node);
                }

                return Encoding.UTF8.GetString(ra.ToArray());
            }
        }

        private static void Ghi(Utf8JsonWriter w, JsonNode? node)
        {
            if (node == null)
            {
                w.WriteNullValue();
                return;
            }

            JsonObject? obj = node as JsonObject;
            if (obj != null)
            {
                w.WriteStartObject();
                foreach (KeyValuePair<string, JsonNode?> kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    w.WritePropertyName(kv.Key);
                    Ghi(w, kv.Value);
                }

                w.WriteEndObject();
                return;
            }

            JsonArray? arr = node as JsonArray;
            if (arr != null)
            {
                w.WriteStartArray();
                foreach (JsonNode? phanTu in arr)
                {
                    Ghi(w, phanTu);
                }

                w.WriteEndArray();
                return;
            }

            node.WriteTo(w);
        }
    }
}
