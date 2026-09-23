using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crowd.BuildingBlocks.Messaging
{
    /// <summary>
    /// Cấu hình JSON dùng chung cho mọi message trên bus.
    ///
    /// Không service nào được tự tạo JsonSerializerOptions riêng cho event —
    /// lệch một tùy chọn là Python đọc không ra, và lỗi đó rất khó lần.
    ///
    /// Dùng static readonly một instance duy nhất là quan trọng về hiệu năng:
    /// JsonSerializerOptions xây bộ nhớ đệm metadata bên trong lần dùng đầu.
    /// Tạo mới mỗi lần serialize sẽ chậm đi hàng chục lần — đây là lỗi hiệu
    /// năng phổ biến nhất với System.Text.Json.
    /// </summary>
    public static class CrowdJson
    {
        public static readonly JsonSerializerOptions Options = TaoOptions();

        private static JsonSerializerOptions TaoOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();

            // camelCase: khớp schema, và là quy ước của cả Python lẫn JS.
            // Thuộc tính EventId trong C# ghi ra JSON thành "eventId".
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

            // Enum ra chuỗi chữ thường: "labeler", không phải số 1.
            // Số thứ tự enum sẽ vỡ ngay khi ai đó chèn giá trị vào giữa danh sách.
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            // GHI null tường minh cho causationId/actor thay vì bỏ hẳn key.
            // Định dạng trên dây đoán trước được, nên Python và Node không phải
            // viết nhánh "key này có thể không tồn tại".
            options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;

            // KHÔNG bật: lệch hoa thường phải nổ ngay lúc test, không phải âm
            // thầm chạy được ở C# rồi hỏng khi Python đọc.
            options.PropertyNameCaseInsensitive = false;

            // Đây chính là "additionalProperties": false của schema, dịch sang C#.
            // Mặc định System.Text.Json BỎ QUA ÂM THẦM trường lạ — nghĩa là một
            // service thêm trường mới vào envelope sẽ không ai phát hiện cho tới
            // khi quá muộn. Xem VD-D-12 về hệ quả với deploy.
            options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

            options.WriteIndented = false;

            return options;
        }
    }
}
