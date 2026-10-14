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
            Attributes = Array.Empty<AttributeDefinition>();
        }

        public string Name { get; private set; }

        /// <summary>Mot trong ToolKinds (classification, bbox, polygon, span, mask, track, ranking...).</summary>
        public string Kind { get; private set; }

        /// <summary>Cac lop duoc chon. Rong voi cong cu khong chon lop (transcription, freeText, number, pairwise, ranking).</summary>
        public IReadOnlyList<string> Classes { get; private set; }

        /// <summary>classification: chon nhieu lop.</summary>
        public bool AllowMultiple { get; private set; }

        /// <summary>Nhan BAT BUOC co ket qua cua cong cu nay (cong cu dang danh sach van duoc nop mang rong).</summary>
        public bool Required { get; private set; }

        /// <summary>Cong cu dang danh sach (ToolKinds.LaDanhSach): so phan tu toi thieu / toi da.</summary>
        public int MinItems { get; private set; }

        public int MaxItems { get; private set; }

        /// <summary>transcription / freeText: so ky tu toi da.</summary>
        public int MaxLength { get; private set; }

        /// <summary>pairwise: cho phep chon "hoa".</summary>
        public bool AllowTie { get; private set; }

        /// <summary>
        /// Nguong cham cau vang va ghep doi tuong khi gop nhan nhieu nguoi. null = mac dinh cua cong cu:
        ///   bbox / polygon / mask / temporalSegment / track: IoU toi thieu (0.5)
        ///   keypoint / polyline: do giong toi thieu = 1 − (do lech / duong cheo anh) (0.98 / 0.97)
        ///   span / relation: F1 toi thieu (1.0 = phai trung tung doan / tung quan he)
        ///   transcription: ti le loi ky tu (CER) TOI DA (0.1)
        ///   ranking: ti le cap muc xep cung thu tu (Kendall) toi thieu (0.8)
        /// </summary>
        public double? MatchThreshold { get; private set; }

        /// <summary>Cong cu dang danh sach (tru relation): thuoc tinh cua tung doi tuong.</summary>
        public IReadOnlyList<AttributeDefinition> Attributes { get; private set; }

        /// <summary>relation: ten cong cu span ma hai dau quan he tro toi.</summary>
        public string? SpanTool { get; private set; }

        /// <summary>number: gioi han gia tri (null = khong gioi han).</summary>
        public double? Min { get; private set; }

        public double? Max { get; private set; }

        /// <summary>number: chi nhan so nguyen (thang diem, so dem).</summary>
        public bool IntegerOnly { get; private set; }

        /// <summary>number: lech toi da so voi dap an / ket qua gop van tinh la khop (mac dinh 0 = phai bang).</summary>
        public double Tolerance { get; private set; }

        public AttributeDefinition? ThuocTinh(string ten)
        {
            foreach (AttributeDefinition a in Attributes)
            {
                if (a.Name == ten)
                {
                    return a;
                }
            }

            return null;
        }

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

            t.MatchThreshold = DocSoThuc(o, "matchThreshold");

            List<AttributeDefinition> thuocTinh = new List<AttributeDefinition>();
            JsonArray? mangThuocTinh = o["attributes"] as JsonArray;
            if (mangThuocTinh != null)
            {
                foreach (JsonNode? n in mangThuocTinh)
                {
                    thuocTinh.Add(AttributeDefinition.Doc((JsonObject)n!));
                }
            }

            t.Attributes = thuocTinh;

            JsonNode? congCuSpan = o["spanTool"];
            t.SpanTool = congCuSpan == null ? null : congCuSpan.GetValue<string>();

            t.Min = DocSoThuc(o, "min");
            t.Max = DocSoThuc(o, "max");
            t.IntegerOnly = DocBool(o, "integer", false);
            t.Tolerance = DocSoThuc(o, "tolerance") ?? 0;

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

            if (Kind == ToolKinds.Transcription || Kind == ToolKinds.FreeText)
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

            // Cac truong duoi day chi ghi khi cong cu co — ban chuan cua tap nhan cu khong doi.
            if (Attributes.Count > 0)
            {
                JsonArray mang = new JsonArray();
                foreach (AttributeDefinition a in Attributes)
                {
                    mang.Add(a.ToJson());
                }

                o["attributes"] = mang;
            }

            if (Kind == ToolKinds.Relation)
            {
                o["spanTool"] = SpanTool;
            }

            if (Kind == ToolKinds.Number)
            {
                o["integer"] = IntegerOnly;
                o["tolerance"] = Tolerance;

                if (Min.HasValue)
                {
                    o["min"] = Min.Value;
                }

                if (Max.HasValue)
                {
                    o["max"] = Max.Value;
                }
            }

            return o;
        }

        private static bool DocBool(JsonObject o, string ten, bool macDinh)
        {
            JsonNode? n = o[ten];
            return n == null ? macDinh : n.GetValue<bool>();
        }

        private static double? DocSoThuc(JsonObject o, string ten)
        {
            JsonNode? n = o[ten];
            if (n == null)
            {
                return null;
            }

            return double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int DocInt(JsonObject o, string ten, int macDinh)
        {
            JsonNode? n = o[ten];
            return n == null ? macDinh : Crowd.Labeling.Tools.HinhHoc.SoNguyen(n, "tap_nhan_sai_dinh_dang");
        }
    }
}
