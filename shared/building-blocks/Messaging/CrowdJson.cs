using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Cấu hình JSON dùng chung cho mọi message trên bus.
/// Không service nào được tự tạo <see cref="JsonSerializerOptions"/> riêng cho event —
/// lệch một tùy chọn là Python đọc không ra, và lỗi đó rất khó lần.
/// </summary>
public static class CrowdJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        // camelCase: khớp schema, và là quy ước của cả Python lẫn JS.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Enum ra chuỗi chữ thường: "labeler", không phải 1.
        // Số thứ tự enum sẽ vỡ ngay khi ai đó chèn thêm một giá trị vào giữa.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // GHI null tường minh cho causationId/actor thay vì bỏ hẳn key.
        // Định dạng trên dây đoán trước được, nên Python và Node không phải
        // viết nhánh "key này có thể không tồn tại".
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // KHÔNG bật: lệch hoa thường phải nổ ngay lúc test, không phải âm thầm
        // chạy được ở C# rồi hỏng khi Python đọc.
        PropertyNameCaseInsensitive = false,

        // Đây chính là "additionalProperties": false của schema, dịch sang C#.
        // Mặc định System.Text.Json BỎ QUA ÂM THẦM trường lạ — nghĩa là một service
        // thêm trường mới vào envelope sẽ không ai phát hiện cho tới khi quá muộn.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

        WriteIndented = false,
    };
}
