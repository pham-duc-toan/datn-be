using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// So sanh cap (a, b) — vd hai cau tra loi cua LLM (RLHF): {"choice":"a"}.
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
                throw new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + "không cho chọn “ngang nhau”.");
            }

            return new JsonObject { ["choice"] = chon };
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            return DocChon(nop) == DocChon(dapAn);
        }
        private static string DocChon(JsonNode ketQua)
        {
            return ketQua["choice"]!.GetValue<string>();
        }
    }
}
