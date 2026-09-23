using System.Text.RegularExpressions;

namespace Crowd.BuildingBlocks.Messaging
{
    /// <summary>
    /// Luật của hợp đồng event, tách riêng để MỘT định nghĩa phục vụ ba nơi:
    /// cổng ra (EventEnvelope.Create), cổng vào (EventEnvelope.Deserialize),
    /// và bộ quét khai báo payload (EventPayloadContract).
    ///
    /// Regex dưới đây phải khớp "pattern" của eventType trong
    /// contracts/events/envelope.schema.json. Hai chỗ đang đồng bộ bằng tay —
    /// contract test là thứ giữ chúng không trôi khỏi nhau.
    /// </summary>
    public static class EventRules
    {
        // RegexOptions.Compiled: biên dịch regex thành IL ngay lần dùng đầu tiên.
        // Tốn chút thời gian khởi động, đổi lại mỗi lần so khớp sau đó nhanh hơn.
        // static readonly nên chỉ tạo một lần cho cả vòng đời ứng dụng.
        private static readonly Regex EventTypePattern = new Regex(
            @"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidEventType(string eventType)
        {
            if (string.IsNullOrEmpty(eventType))
            {
                return false;
            }

            return EventTypePattern.IsMatch(eventType);
        }

        /// <summary>
        /// Trả về mô tả vi phạm, hoặc null nếu hợp lệ.
        /// Trả chuỗi thay vì ném ngoại lệ, để nơi gọi tự chọn ném loại nào —
        /// cổng ra ném ArgumentException, cổng vào ném EventContractException.
        /// </summary>
        public static string? Violation(string eventType, int version, string producer)
        {
            if (!IsValidEventType(eventType))
            {
                return "eventType '" + eventType + "' sai định dạng. "
                     + "Phải là <aggregate>.<quá_khứ> chữ thường, vd: annotation.approved";
            }

            if (version < 1)
            {
                return "version = " + version + ", phải >= 1";
            }

            if (string.IsNullOrWhiteSpace(producer))
            {
                return "producer không được rỗng";
            }

            return null;
        }
    }
}
