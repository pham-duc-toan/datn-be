using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Annotation;

/// <summary>
/// Một nhãn đã được chốt duyệt — <c>ledger-svc</c> chi trả, <c>identity-svc</c> cộng uy tín.
/// <para>Nguồn: <c>contracts/events/CATALOG.md</c>, mục <c>annotation-svc</c>.</para>
/// </summary>
/// <remarks>
/// Khuôn mẫu cho mọi payload khác — bốn quy tắc:
/// <list type="number">
/// <item>Cài <see cref="IEventPayload"/>, khai <c>EventType</c> và <c>Version</c> ngay trong kiểu.</item>
/// <item>Mọi thuộc tính là <c>required</c> hoặc nullable. Không dùng positional record:
///       tham số vị trí không mang <c>required</c> nên JSON thiếu trường sẽ thành
///       giá trị mặc định — với tiền là <b>0 đồng, không một lời cảnh báo</b>.</item>
/// <item>Tiền là <see cref="long"/> số nguyên đồng. Không <c>decimal</c>, không
///       <c>double</c> — VND không có đơn vị lẻ (VD-M-07).</item>
/// <item>Chỉ dữ liệu, không hành vi. Payload là bản ghi chép một sự việc đã xảy ra.</item>
/// </list>
/// </remarks>
public sealed record AnnotationApproved : IEventPayload
{
    public static string EventType => "annotation.approved";

    public static int Version => 1;

    /// <summary>Nhãn được duyệt. Khóa chống chi trả trùng ở ledger (VD-M-02).</summary>
    public required Guid AnnotationId { get; init; }

    /// <summary>Task chứa nhãn này. Ledger đếm theo đây để chặn chi vượt redundancy (VD-M-03).</summary>
    public required Guid TaskId { get; init; }

    /// <summary>Dự án — xác định tài khoản escrow nào bị ghi nợ.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Người được trả tiền. <c>null</c> khi nhãn đến từ cổng link (khách vãng lai).</summary>
    public Guid? LabelerId { get; init; }

    /// <summary>Thù lao của labeler, số nguyên đồng.</summary>
    public required long AmountVnd { get; init; }

    /// <summary>Phí nền tảng, số nguyên đồng. Ký quỹ đã trừ = <c>AmountVnd + PlatformFeeVnd</c>.</summary>
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
