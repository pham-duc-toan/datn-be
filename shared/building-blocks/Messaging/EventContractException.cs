using System;

namespace Crowd.BuildingBlocks.Messaging
{
    /// <summary>
    /// Message không tuân thủ hợp đồng contracts/events/envelope.schema.json.
    ///
    /// Consumer bắt đúng MỘT loại ngoại lệ này rồi đẩy message sang DLQ — dù
    /// nguyên nhân là JSON hỏng, thiếu trường bắt buộc, hay vi phạm quy tắc
    /// nghiệp vụ (eventType sai định dạng, version nhỏ hơn 1).
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
}
