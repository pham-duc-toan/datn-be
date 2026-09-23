using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Annotation
{
    /// <summary>
    /// Một nhãn đã được chốt duyệt — ledger-svc chi trả, identity-svc cộng uy tín.
    /// Nguồn: contracts/events/CATALOG.md, mục annotation-svc.
    ///
    /// KHUÔN MẪU cho mọi payload khác. Bốn quy tắc:
    ///
    /// 1. Cài IEventPayload, khai EventType và Version ngay trong kiểu.
    ///
    /// 2. Mọi thuộc tính là "required" hoặc nullable. KHÔNG dùng dạng rút gọn
    ///    "record X(long AmountVnd)" — tham số vị trí không mang required nên
    ///    JSON thiếu trường sẽ thành giá trị mặc định. Với tiền là 0 đồng,
    ///    không một lời cảnh báo.
    ///
    /// 3. Tiền là long, số nguyên đồng. Không decimal, không double — VND
    ///    không có đơn vị lẻ (VD-M-07).
    ///
    /// 4. Chỉ dữ liệu, không hành vi. Payload là bản ghi chép một sự việc
    ///    đã xảy ra.
    /// </summary>
    public sealed record AnnotationApproved : IEventPayload
    {
        // Hai thuộc tính static này là phần IEventPayload bắt buộc phải có.
        // Chúng gắn tên event vào chính kiểu dữ liệu, nên không thể gắn nhầm.
        public static string EventType
        {
            get { return "annotation.approved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Nhãn được duyệt. Khóa chống chi trả trùng ở ledger (VD-M-02).</summary>
        public required Guid AnnotationId { get; init; }

        /// <summary>
        /// Task chứa nhãn này. Ledger đếm theo đây để chặn chi vượt redundancy
        /// đã ký quỹ (VD-M-03).
        /// </summary>
        public required Guid TaskId { get; init; }

        /// <summary>Dự án — xác định tài khoản escrow nào bị ghi nợ.</summary>
        public required Guid ProjectId { get; init; }

        /// <summary>
        /// Người được trả tiền. null khi nhãn đến từ cổng link, vì người vượt
        /// link là khách vãng lai không có tài khoản.
        /// </summary>
        public Guid? LabelerId { get; init; }

        /// <summary>Thù lao của labeler, số nguyên đồng.</summary>
        public required long AmountVnd { get; init; }

        /// <summary>
        /// Phí nền tảng, số nguyên đồng.
        /// Ký quỹ bị trừ = AmountVnd + PlatformFeeVnd (VD-M-15).
        /// </summary>
        public required long PlatformFeeVnd { get; init; }

        /// <summary>Kênh thu thập: quyết định ngưỡng chất lượng và ai được trả tiền.</summary>
        public required AnnotationSource Source { get; init; }
    }

    /// <summary>Ba kênh thu thập nhãn — mục 1.3 của đặc tả.</summary>
    public enum AnnotationSource
    {
        /// <summary>Labeler đã đăng ký, có điểm uy tín.</summary>
        Professional,

        /// <summary>Khách vãng lai giải nhãn để vượt link.</summary>
        LinkGateway,

        /// <summary>Nhóm labeler cùng xử lý một mẫu trong phòng realtime.</summary>
        Collaborative,
    }
}
