using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Chep lai loi noi thanh van ban: {"text":"xin chao"}.
    ///
    /// Cham dap an bang ti le loi ky tu (CER) sau khi chuan hoa (chu thuong, gop
    /// khoang trang): CER &lt;= nguong (mac dinh 0.1) la khop.
    ///
    /// Gop tu dong: CHUA — gop nhieu ban chep (ROVER) la viec cua quality-svc.
    /// </summary>
    internal sealed class TranscriptionTool : IToolKind
    {
        public const double NguongMacDinh = 0.1;

        public string Kind
        {
            get { return ToolKinds.Transcription; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            string text = DocText(ketQua);

            if (tool.Required && text.Trim().Length == 0)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': van ban khong duoc rong.");
            }

            if (text.Length > tool.MaxLength)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': toi da " + tool.MaxLength + " ky tu.");
            }

            return new JsonObject { ["text"] = text };
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh)
        {
            double nguong = tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
            return HinhHoc.Cer(DocText(nop), DocText(dapAn)) <= nguong;
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool)
        {
            return new JsonObject { ["method"] = "none" };
        }

        private static string DocText(JsonNode ketQua)
        {
            return ketQua["text"]!.GetValue<string>();
        }
    }
}
