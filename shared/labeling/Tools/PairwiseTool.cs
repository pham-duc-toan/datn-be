using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// So sanh cap (a, b) — vd hai cau tra loi cua LLM (RLHF): {"choice":"a"}.
    /// Gop: da so tuyet doi; khong lua chon nao qua ban = tranh chap.
    /// </summary>
    internal sealed class PairwiseTool : IToolKind
    {
        public string Kind
        {
            get { return ToolKinds.Pairwise; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            string chon = DocChon(ketQua);
            if (chon == "tie" && !tool.AllowTie)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "' khong cho chon hoa.");
            }

            return new JsonObject { ["choice"] = chon };
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool)
        {
            return DocChon(nop) == DocChon(dapAn);
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool)
        {
            Dictionary<string, int> phieu = new Dictionary<string, int>();
            foreach (JsonNode k in ketQua)
            {
                string c = DocChon(k);
                int cu;
                phieu.TryGetValue(c, out cu);
                phieu[c] = cu + 1;
            }

            KeyValuePair<string, int>? thang = null;
            foreach (KeyValuePair<string, int> p in phieu.OrderBy(p => p.Key))
            {
                if (p.Value * 2 > ketQua.Count)
                {
                    thang = p;
                }
            }

            JsonObject votes = new JsonObject();
            foreach (KeyValuePair<string, int> p in phieu.OrderBy(p => p.Key))
            {
                votes[p.Key] = p.Value;
            }

            return new JsonObject
            {
                ["method"] = "majority",
                ["final"] = thang.HasValue ? new JsonObject { ["choice"] = thang.Value.Key } : null,
                ["votes"] = votes,
                ["disputed"] = !thang.HasValue,
            };
        }

        private static string DocChon(JsonNode ketQua)
        {
            return ketQua["choice"]!.GetValue<string>();
        }
    }
}
