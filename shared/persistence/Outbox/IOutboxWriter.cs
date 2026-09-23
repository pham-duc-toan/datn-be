using Crowd.BuildingBlocks.Messaging;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Cách duy nhất để phát event từ code nghiệp vụ.
    ///
    /// KHÔNG service nào được gọi thẳng vào message bus. Làm vậy là bỏ qua tính
    /// nguyên tử với dữ liệu nghiệp vụ — đúng lỗi VD-D-01 mà outbox sinh ra để chặn.
    ///
    /// GIAO KÈO QUAN TRỌNG NHẤT: hàm này KHÔNG gọi SaveChanges. Nó chỉ thêm dòng
    /// vào change tracker của EF; việc commit là của code nghiệp vụ. Đó chính là
    /// điều khiến hai thứ nằm chung một transaction:
    ///
    ///     annotation.State = "approved";        // ghi nghiệp vụ
    ///     outbox.Enqueue(envelope);             // ghi event
    ///     await db.SaveChangesAsync(ct);        // MỘT transaction cho cả hai
    ///
    /// Nếu Enqueue tự SaveChanges thì nó sẽ commit riêng, tách khỏi thay đổi
    /// nghiệp vụ — và outbox mất sạch ý nghĩa.
    /// </summary>
    public interface IOutboxWriter
    {
        /// <summary>
        /// Xếp envelope vào hàng chờ gửi. Chưa ghi xuống đĩa cho tới khi code
        /// nghiệp vụ gọi SaveChanges.
        /// </summary>
        void Enqueue<TPayload>(EventEnvelope<TPayload> envelope)
            where TPayload : class, IEventPayload;
    }
}
