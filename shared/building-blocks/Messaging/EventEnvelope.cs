using System.Text.RegularExpressions;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Vỏ bọc chung cho MỌI event trên bus.
/// Phải khớp <c>contracts/events/envelope.schema.json</c> — C#, Python và Node
/// cùng sinh code từ file schema đó, nên lệch một trường là hỏng liên thông.
/// </summary>
public sealed record EventEnvelope<TPayload> where TPayload : class
{
    /// <summary>UUIDv7. Consumer dùng làm khóa idempotency (VD-D-02).</summary>
    public required Guid EventId { get; init; }

    /// <summary>Dạng <c>&lt;aggregate&gt;.&lt;quá_khứ&gt;</c>, vd: <c>annotation.approved</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>Phiên bản schema của payload. Tăng khi có breaking change (VD-D-08).</summary>
    public required int Version { get; init; }

    /// <summary>
    /// Thời điểm SỰ KIỆN XẢY RA về mặt nghiệp vụ, không phải lúc publish.
    /// Hai cái lệch nhau khi outbox tồn đọng — mọi tính toán nghiệp vụ dùng cái này.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Tên service phát, vd: <c>annotation-svc</c>.</summary>
    public required string Producer { get; init; }

    /// <summary>Sinh ở gateway, xuyên mọi hop và mọi event phát sinh (VD-D-03).</summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// <see cref="EventId"/> của event đã gây ra event này.
    /// Cho phép dựng lại cây nhân quả: "vì sao labeler này được trả tiền?"
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary><c>null</c> = hệ thống tự động, không phải người dùng.</summary>
    public EventActor? Actor { get; init; }

    public required TPayload Payload { get; init; }
}

/// <summary>
/// Nơi tạo <see cref="EventEnvelope{TPayload}"/>.
/// Tách khỏi kiểu generic để C# suy luận <c>TPayload</c> từ chính payload:
/// viết <c>EventEnvelope.Create(..., payload)</c> thay vì lặp lại tên kiểu.
/// </summary>
public static partial class EventEnvelope
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$")]
    private static partial Regex EventTypePattern();

    /// <summary>
    /// Đường tạo envelope DUY NHẤT. Ép <c>EventId</c> là UUIDv7 và
    /// validate <paramref name="eventType"/> đúng regex của schema.
    /// </summary>
    /// <param name="occurredAt">
    /// Bỏ trống = bây giờ. Chỉ truyền vào khi sự kiện đã xảy ra trong quá khứ.
    /// </param>
    public static EventEnvelope<TPayload> Create<TPayload>(
        string eventType,
        int version,
        string producer,
        Guid correlationId,
        TPayload payload,
        Guid? causationId = null,
        EventActor? actor = null,
        DateTimeOffset? occurredAt = null)
        where TPayload : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentNullException.ThrowIfNull(payload);

        if (!EventTypePattern().IsMatch(eventType))
        {
            throw new ArgumentException(
                $"eventType '{eventType}' sai định dạng. Phải là <aggregate>.<quá_khứ> " +
                "chữ thường, vd: annotation.approved",
                nameof(eventType));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return new EventEnvelope<TPayload>
        {
            // UUIDv7 nhúng timestamp ở đầu nên ghi tuần tự vào cuối B-tree.
            // UUIDv4 ngẫu nhiên gây page split liên tục trên outbox/processed_events.
            EventId = Guid.CreateVersion7(),
            EventType = eventType,
            Version = version,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Producer = producer,
            CorrelationId = correlationId,
            CausationId = causationId,
            Actor = actor,
            Payload = payload,
        };
    }
}

/// <summary>Ai gây ra hành động. Nền của audit log FP-06.</summary>
public sealed record EventActor(Guid UserId, ActorRole Role);

/// <summary>
/// Serialize thành chuỗi chữ thường qua <see cref="CrowdJson.Options"/>.
/// KHÔNG gắn <c>[JsonConverter]</c> lên enum này: attribute sẽ thắng tùy chọn
/// và ghi ra "Business" thay vì "business", phá schema.
/// </summary>
public enum ActorRole
{
    Business,
    Labeler,
    Sharer,
    Guest,
    Admin,
    System,
}
