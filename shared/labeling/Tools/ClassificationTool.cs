using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Chon lop cho CA mau: {"labelIds":["do"]}. Dung cho moi loai du lieu.
    /// </summary>
    internal sealed class ClassificationTool : IToolKind
    {
        public string Kind
        {
            get { return ToolKinds.Classification; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            List<string> lop = DocLop(ketQua);

            foreach (string l in lop)
            {
                if (!tool.CoLop(l))
                {
                    throw Sai(tool, "lớp “" + l + "” không có trong tập nhãn.");
                }
            }

            if (!tool.AllowMultiple && lop.Count != 1)
            {
                throw Sai(tool, "phải chọn đúng một lớp.");
            }

            lop.Sort(StringComparer.Ordinal);
            return TaoKetQua(lop);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            HashSet<string> a = new HashSet<string>(DocLop(nop), StringComparer.Ordinal);
            return a.SetEquals(DocLop(dapAn));
        }
        internal static List<string> DocLop(JsonNode ketQua)
        {
            List<string> lop = new List<string>();
            foreach (JsonNode? n in (JsonArray)ketQua["labelIds"]!)
            {
                lop.Add(n!.GetValue<string>());
            }

            return lop;
        }

        private static JsonObject TaoKetQua(IEnumerable<string> lop)
        {
            JsonArray mang = new JsonArray();
            foreach (string l in lop)
            {
                mang.Add(l);
            }

            return new JsonObject { ["labelIds"] = mang };
        }

        private static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + loi);
        }
    }
}
