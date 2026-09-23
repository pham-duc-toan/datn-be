namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Message không tuân thủ hợp đồng <c>contracts/events/envelope.schema.json</c>.
/// <para>
/// Consumer bắt đúng một loại ngoại lệ này rồi đẩy message sang DLQ — dù nguyên
/// nhân là JSON hỏng, thiếu trường bắt buộc, hay vi phạm quy tắc nghiệp vụ
/// (<c>eventType</c> sai định dạng, <c>version &lt; 1</c>).
/// </para>
/// </summary>
public sealed class EventContractException : Exception
{
    public EventContractException()
    {
    }

    public EventContractException(string message)
        : base(message)
    {
    }

    public EventContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
