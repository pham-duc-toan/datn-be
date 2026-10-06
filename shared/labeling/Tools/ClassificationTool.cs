using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Chon lop cho CA mau: {"labelIds":["do"]}. Dung cho moi loai du lieu.
    ///
    /// Gop ket qua: DA SO TUYET DOI theo tung lop — lop duoc chon neu hon mot nua
    /// so nguoi chon no. Khong lop nao qua ban = tranh chap (FB-22).
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
                    throw Sai(tool, "lop '" + l + "' khong co trong tap nhan.");
                }
            }

            if (!tool.AllowMultiple && lop.Count != 1)
            {
                throw Sai(tool, "phai chon DUNG MOT lop.");
            }

            lop.Sort(StringComparer.Ordinal);
            return TaoKetQua(lop);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool)
        {
            HashSet<string> a = new HashSet<string>(DocLop(nop), StringComparer.Ordinal);
            return a.SetEquals(DocLop(dapAn));
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool)
        {
            int soNguoi = ketQua.Count;
            SortedDictionary<string, int> phieu = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (JsonNode k in ketQua)
            {
                foreach (string l in DocLop(k).Distinct(StringComparer.Ordinal))
                {
                    int cu;
                    phieu.TryGetValue(l, out cu);
                    phieu[l] = cu + 1;
                }
            }

            List<string> chot = phieu.Where(p => p.Value * 2 > soNguoi).Select(p => p.Key).ToList();

            JsonObject votes = new JsonObject();
            foreach (KeyValuePair<string, int> p in phieu)
            {
                votes[p.Key] = p.Value;
            }

            return new JsonObject
            {
                ["method"] = "majority",
                ["final"] = chot.Count == 0 ? null : TaoKetQua(chot),
                ["votes"] = votes,
                ["disputed"] = chot.Count == 0,
            };
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
            return new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': " + loi);
        }
    }
}
