using System;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Tham số vận hành của dispatcher. Đặt trong appsettings để chỉnh được mà
    /// không phải build lại.
    /// </summary>
    public sealed class OutboxOptions
    {
        public const string SectionName = "Outbox";

        /// <summary>
        /// Nghỉ bao lâu khi bảng rỗng, trước khi hỏi lại.
        ///
        /// Đây là ĐỘ TRỄ TỆ NHẤT của mọi event trong hệ thống: nhãn duyệt xong
        /// thì chậm nhất từng này tiền mới vào ví labeler. Đặt nhỏ thì đỡ trễ
        /// nhưng bơm thêm truy vấn rỗng vào database.
        ///
        /// Lưu ý: khi có việc thì dispatcher KHÔNG nghỉ — nó lấy lô tiếp ngay,
        /// nên lúc tải cao con số này không ảnh hưởng gì.
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Số dòng lấy mỗi lô.
        ///
        /// Cả lô nằm trong MỘT transaction, và transaction đó mở suốt thời gian
        /// gửi lên bus. Lô càng lớn thì transaction mở càng lâu — xem chú thích
        /// trong OutboxDispatcher về vì sao vẫn chấp nhận được.
        /// </summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>
        /// Chờ broker xác nhận tối đa bao lâu cho một message.
        ///
        /// Phải có trần: không có nó, RabbitMQ treo mà không đứt kết nối sẽ giữ
        /// transaction database mở vô thời hạn.
        /// </summary>
        public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
