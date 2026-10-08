using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Hanh vi cua MOT loai cong cu. Hinh dang JSON da duoc JSON Schema kiem
    /// truoc khi toi day — o day chi con NGU NGHIA.
    /// </summary>
    internal interface IToolKind
    {
        string Kind { get; }

        /// <summary>
        /// Kiem ket qua theo cau hinh cong cu va thong tin mau (khung trong anh,
        /// doan van trong van ban...). Tra ve ban CHUAN HOA. Sai thi nem
        /// LabelFormatException ma "nhan_khong_hop_le".
        /// </summary>
        JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau);

        /// <summary>
        /// Ket qua nop co khop dap an khong — cham cau vang, bai test dau vao.
        /// Nguong: matchThreshold cua cong cu, khong co thi nguongMacDinh (setting
        /// labeling.threshold.*), khong co nua thi mac dinh cua thu vien.
        /// </summary>
        bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh);

        /// <summary>
        /// Gop ket qua cua nhieu nguoi (da duyet) cho mot mau. Cong cu chua co cach
        /// gop tu dong tra ve {"method":"none"} — ket qua cuoi la cac nhan da duoc
        /// nguoi duyet chap nhan (quality-svc se lam phan nay sau).
        /// </summary>
        JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool);
    }
}
