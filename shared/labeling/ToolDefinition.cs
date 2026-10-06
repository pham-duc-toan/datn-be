using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Crowd.Labeling
{
    /// <summary>
    /// Mot CONG CU trong tap nhan cua du an, vd:
    ///     {"name":"vat_the","kind":"bbox","classes":["xe","nguoi"],"maxItems":50}
    ///
    /// "name" la khoa cua ket qua trong nhan: nhan nop len co dang
    ///     {"vat_the": [ ...cac khung... ], "loai_anh": {"labelIds":["ngoai_troi"]}}
    /// </summary>
    public sealed class ToolDefinition
    {
        public const int MaxItemsMacDinh = 1000;
        public const int MaxLengthMacDinh = 5000;

        private ToolDefinition()
        {
            Name = string.Empty;
            Kind = string.Empty;
            Classes = Array.Empty<string>();
        }

        public string Name { get; private set; }

        /// <summary>classification | bbox | polygon | span | transcription | temporalSegment | pairwise</summary>
        public string Kind { get; private set; }

        /// <summary>Cac lop duoc chon. Rong voi transcription, pairwise.</summary>
        public IReadOnlyList<string> Classes { get; private set; }

        /// <summary>classification: chon nhieu lop.</summary>
        public bool AllowMultiple { get; private set; }

        /// <summary>Nhan BAT BUOC co ket qua cua cong cu nay (cong cu dang danh sach van duoc nop mang rong).</summary>
        public bool Required { get; private set; }

        /// <summary>Cong cu dang danh sach (bbox, polygon, span, temporalSegment): so phan tu toi thieu / toi da.</summary>
        public int MinItems { get; private set; }

        public int MaxItems { get; private set; }

        /// <summary>transcription: so ky tu toi da.</summary>
        public int MaxLength { get; private set; }

        /// <summary>pairwise: cho phep chon "hoa".</summary>
        public bool AllowTie { get; private set; }

        /// <summary>
        /// Nguong cham cau vang. null = mac dinh cua cong cu:
        ///   bbox / polygon / temporalSegment: IoU toi thieu (0.5)
        ///   span: F1 toi thieu (1.0 = phai trung tung doan)
        ///   transcription: ti le loi ky tu (CER) TOI DA (0.1)
        /// </summary>
        public double? MatchThreshold { get; private set; }

        public bool CoLop(string lop)
        {
            foreach (string c in Classes)
            {
                if (string.Equals(c, lop, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static ToolDefinition Doc(JsonObject o)
        {
            ToolDefinition t = new ToolDefinition();
            t.Name = o["name"]!.GetValue<string>();
            t.Kind = o["kind"]!.GetValue<string>();

            List<string> lop = new List<string>();
            JsonArray? mang = o["classes"] as JsonArray;
            if (mang != null)
            {
                foreach (JsonNode? n in mang)
                {
                    lop.Add(n!.GetValue<string>().Trim());
                }
            }

            t.Classes = lop;
            t.AllowMultiple = DocBool(o, "allowMultiple", false);
            t.Required = DocBool(o, "required", true);
            t.MinItems = DocInt(o, "minItems", 0);
            t.MaxItems = DocInt(o, "maxItems", MaxItemsMacDinh);
            t.MaxLength = DocInt(o, "maxLength", MaxLengthMacDinh);
            t.AllowTie = DocBool(o, "allowTie", true);

            JsonNode? nguong = o["matchThreshold"];
            if (nguong != null)
            {
                t.MatchThreshold = double.Parse(nguong.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return t;
        }

        /// <summary>Ghi lai dang CHUAN (dien gia tri mac dinh tuong minh) — ban luu va ban sao giua cac service y het nhau.</summary>
        internal JsonObject ToJson()
        {
            JsonObject o = new JsonObject();
            o["name"] = Name;
            o["kind"] = Kind;
            o["required"] = Required;

            if (Classes.Count > 0)
            {
                JsonArray mang = new JsonArray();
                foreach (string c in Classes)
                {
                    mang.Add(c);
                }

                o["classes"] = mang;
            }

            if (Kind == ToolKinds.Classification)
            {
                o["allowMultiple"] = AllowMultiple;
            }

            if (ToolKinds.LaDanhSach(Kind))
            {
                o["minItems"] = MinItems;
                o["maxItems"] = MaxItems;
            }

            if (Kind == ToolKinds.Transcription)
            {
                o["maxLength"] = MaxLength;
            }

            if (Kind == ToolKinds.Pairwise)
            {
                o["allowTie"] = AllowTie;
            }

            if (MatchThreshold.HasValue)
            {
                o["matchThreshold"] = MatchThreshold.Value;
            }

            return o;
        }

        private static bool DocBool(JsonObject o, string ten, bool macDinh)
        {
            JsonNode? n = o[ten];
            return n == null ? macDinh : n.GetValue<bool>();
        }

        private static int DocInt(JsonObject o, string ten, int macDinh)
        {
            JsonNode? n = o[ten];
            return n == null ? macDinh : n.GetValue<int>();
        }
    }
}
