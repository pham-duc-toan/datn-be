using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.Labeling.Schemas;

namespace Crowd.Labeling
{
    /// <summary>
    /// TAP NHAN cua du an: loai du lieu + danh sach cong cu.
    ///
    ///     {
    ///       "modality": "image",
    ///       "tools": [
    ///         {"name":"loai_anh","kind":"classification","classes":["ngay","dem"]},
    ///         {"name":"vat_the","kind":"bbox","classes":["xe","nguoi"],"maxItems":50}
    ///       ]
    ///     }
    ///
    /// Audio/video them "segmentSeconds": cat file dai thanh doan, moi doan mot task.
    ///
    /// Kiem hai lop: HINH DANG bang JSON Schema (contracts/labeling/label-schema.schema.json),
    /// roi NGU NGHIA trong code (ten cong cu khong trung, cong cu hop voi loai du
    /// lieu, ten lop khong trung, minItems &lt;= maxItems...).
    ///
    /// Bat bien. Luu (project_db), chep sang task-svc / annotation-svc qua
    /// project.published — o dang CHUAN (ToRawJson) nen ba noi y het nhau.
    /// </summary>
    public sealed class LabelSchema
    {
        private readonly List<ToolDefinition> _tools;

        private LabelSchema(string modality, int? segmentSeconds, List<ToolDefinition> tools)
        {
            Modality = modality;
            SegmentSeconds = segmentSeconds;
            _tools = tools;
        }

        public string Modality { get; }

        public int? SegmentSeconds { get; }

        public IReadOnlyList<ToolDefinition> Tools
        {
            get { return _tools; }
        }

        public static LabelSchema Doc(string json)
        {
            using (JsonDocument doc = DocJson(json))
            {
                return Doc(doc.RootElement);
            }
        }

        public static LabelSchema Doc(RawJson json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            return Doc(json.Json);
        }

        public static LabelSchema Doc(JsonElement json)
        {
            // 1. Hinh dang.
            JsonSchemas.KiemTapNhan(json);

            JsonObject o = (JsonObject)JsonNode.Parse(json.GetRawText())!;
            string modality = o["modality"]!.GetValue<string>();

            int? segment = null;
            JsonNode? seg = o["segmentSeconds"];
            if (seg != null)
            {
                segment = seg.GetValue<int>();
            }

            // 2. Ngu nghia.
            if (segment.HasValue && !Modalities.CoThoiGian(modality))
            {
                throw Loi("segmentSeconds chi dung cho audio / video.");
            }

            List<ToolDefinition> tools = new List<ToolDefinition>();
            HashSet<string> tenDaCo = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonNode? n in (JsonArray)o["tools"]!)
            {
                ToolDefinition t = ToolDefinition.Doc((JsonObject)n!);

                if (!tenDaCo.Add(t.Name))
                {
                    throw Loi("Ten cong cu '" + t.Name + "' bi trung.");
                }

                if (!ToolKinds.HopVoi(t.Kind, modality))
                {
                    throw Loi("Cong cu " + t.Kind + " khong dung duoc cho du lieu " + modality + ".");
                }

                KiemLop(t);

                if (ToolKinds.LaDanhSach(t.Kind) && t.MinItems > t.MaxItems)
                {
                    throw Loi("Cong cu '" + t.Name + "': minItems lon hon maxItems.");
                }

                tools.Add(t);
            }

            return new LabelSchema(modality, segment, tools);
        }

        public ToolDefinition? Tool(string name)
        {
            foreach (ToolDefinition t in _tools)
            {
                if (t.Name == name)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>Lop nay co trong it nhat mot cong cu khong (vd kiem vi du trong huong dan).</summary>
        public bool CoLop(string lop)
        {
            foreach (ToolDefinition t in _tools)
            {
                if (t.CoLop(lop))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Dang CHUAN: dien du gia tri mac dinh, khoa sap xep — luu va chep giua cac service.</summary>
        public RawJson ToRawJson()
        {
            JsonArray tools = new JsonArray();
            foreach (ToolDefinition t in _tools)
            {
                tools.Add(t.ToJson());
            }

            JsonObject o = new JsonObject();
            o["modality"] = Modality;
            if (SegmentSeconds.HasValue)
            {
                o["segmentSeconds"] = SegmentSeconds.Value;
            }

            o["tools"] = tools;
            return RawJson.Tu(o);
        }

        public override string ToString()
        {
            return ToRawJson().Json;
        }

        private static void KiemLop(ToolDefinition t)
        {
            HashSet<string> daGap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string lop in t.Classes)
            {
                if (lop.Length == 0)
                {
                    throw Loi("Cong cu '" + t.Name + "' co ten lop rong.");
                }

                // "Meo" va "meo" la mot lop — chong nham lan khi gan.
                if (!daGap.Add(lop))
                {
                    throw Loi("Cong cu '" + t.Name + "': lop '" + lop + "' bi trung.");
                }
            }
        }

        private static LabelFormatException Loi(string thongBao)
        {
            return new LabelFormatException("tap_nhan_khong_hop_le", thongBao);
        }

        private static JsonDocument DocJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LabelFormatException("tap_nhan_sai_dinh_dang", "Tap nhan rong.");
            }

            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new LabelFormatException("Tap nhan khong phai JSON hop le.", ex);
            }
        }
    }
}
