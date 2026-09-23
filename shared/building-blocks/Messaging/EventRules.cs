using System.Text.RegularExpressions;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Luật của hợp đồng event, tách riêng để **một** định nghĩa phục vụ ba nơi:
/// cổng ra (<c>EventEnvelope.Create</c>), cổng vào (<c>EventEnvelope.Deserialize</c>),
/// và bộ quét khai báo payload (<see cref="EventPayloadContract"/>).
/// <para>
/// Regex dưới đây phải khớp <c>pattern</c> của <c>eventType</c> trong
/// <c>contracts/events/envelope.schema.json</c>. Hai chỗ đang đồng bộ bằng tay —
/// contract test là thứ giữ chúng không trôi khỏi nhau.
/// </para>
/// </summary>
public static partial class EventRules
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$")]
    private static partial Regex EventTypePattern();

    public static bool IsValidEventType(string eventType) =>
        !string.IsNullOrEmpty(eventType) && EventTypePattern().IsMatch(eventType);

    /// <summary>Trả về mô tả vi phạm, hoặc <c>null</c> nếu hợp lệ.</summary>
    public static string? Violation(string eventType, int version, string producer)
    {
        if (!IsValidEventType(eventType))
        {
            return $"eventType '{eventType}' sai định dạng. Phải là <aggregate>.<quá_khứ> " +
                   "chữ thường, vd: annotation.approved";
        }

        if (version < 1)
        {
            return $"version = {version}, phải >= 1";
        }

        return string.IsNullOrWhiteSpace(producer) ? "producer không được rỗng" : null;
    }
}
