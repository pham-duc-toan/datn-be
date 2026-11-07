using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Hanh vi cua MOT loai cong cu. Hinh dang JSON da duoc JSON Schema kiem
    /// truoc khi toi day — o day chi con NGU NGHIA.
    ///
    /// GOP nhan cua nhieu nguoi KHONG nam o day: thuat toan gop chi con o quality-svc (app/gop.py),
    /// project-svc goi qua gRPC LabelAggregation (kien truc 3.24).
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
        bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau);
    }
}
