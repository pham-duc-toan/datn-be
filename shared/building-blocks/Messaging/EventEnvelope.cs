using System.Text.Json;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Vỏ bọc chung cho MỌI event trên bus.
/// Phải khớp <c>contracts/events/envelope.schema.json</c> — C#, Python và Node
/// cùng sinh code từ file schema đó, nên lệch một trường là hỏng liên thông.
/// </summary>
public sealed record EventEnvelope<TPayload> where TPayload : class, IEventPayload
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
/// Hai cổng duy nhất cho event: <see cref="Create"/> đi ra, <see cref="Deserialize{TPayload}(string)"/>
/// đi vào. Cả hai chạy <b>cùng một bộ luật</b> nên chiều vào và chiều ra không thể trôi khỏi nhau.
/// <para>
/// <b>Không gọi <see cref="JsonSerializer"/> trực tiếp cho event.</b> Làm vậy là bỏ qua
/// các luật chỉ tồn tại ở đây (regex <c>eventType</c>, <c>version &gt;= 1</c>) — đúng lý do
/// chiều vào từng lỏng hơn chiều ra.
/// </para>
/// </summary>
public static class EventEnvelope
{
    /// <summary>
    /// Cổng RA. <c>eventType</c> và <c>version</c> lấy thẳng từ <typeparamref name="TPayload"/>
    /// nên không thể gắn nhầm loại event cho payload.
    /// </summary>
    /// <param name="occurredAt">
    /// Bỏ trống = bây giờ. Chỉ truyền vào khi sự kiện đã xảy ra trong quá khứ.
    /// </param>
    /// <exception cref="ArgumentException">Tham số vi phạm hợp đồng.</exception>
    public static EventEnvelope<TPayload> Create<TPayload>(
        string producer,
        Guid correlationId,
        TPayload payload,
        Guid? causationId = null,
        EventActor? actor = null,
        DateTimeOffset? occurredAt = null)
        where TPayload : class, IEventPayload
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentNullException.ThrowIfNull(payload);

        var eventType = TPayload.EventType;
        var version = TPayload.Version;

        if (EventRules.Violation(eventType, version, producer) is { } loi)
        {
            throw new ArgumentException(
                $"{typeof(TPayload).Name} khai báo sai hợp đồng: {loi}", nameof(payload));
        }

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

    /// <summary>Cổng VÀO. Đọc JSON rồi chạy đúng bộ luật của <see cref="Create"/>.</summary>
    /// <exception cref="EventContractException">JSON hỏng, thiếu trường, hoặc vi phạm hợp đồng.</exception>
    public static EventEnvelope<TPayload> Deserialize<TPayload>(string json)
        where TPayload : class, IEventPayload
    {
        ArgumentNullException.ThrowIfNull(json);
        return Kiem(Doc<TPayload>(() => JsonSerializer.Deserialize<EventEnvelope<TPayload>>(
            json, CrowdJson.Options)));
    }

    /// <summary>Cổng VÀO cho message lấy thẳng từ bus (RabbitMQ trả về UTF-8 byte).</summary>
    /// <exception cref="EventContractException">JSON hỏng, thiếu trường, hoặc vi phạm hợp đồng.</exception>
    public static EventEnvelope<TPayload> Deserialize<TPayload>(ReadOnlySpan<byte> utf8Json)
        where TPayload : class, IEventPayload
    {
        // Sao ra mảng vì span không đi qua được closure của Doc().
        var buffer = utf8Json.ToArray();
        return Kiem(Doc<TPayload>(() => JsonSerializer.Deserialize<EventEnvelope<TPayload>>(
            buffer, CrowdJson.Options)));
    }


    private static EventEnvelope<TPayload> Doc<TPayload>(
        Func<EventEnvelope<TPayload>?> deserialize)
        where TPayload : class, IEventPayload
    {
        try
        {
            return deserialize()
                   ?? throw new EventContractException("Message là JSON null.");
        }
        catch (JsonException ex)
        {
            // Gộp mọi nguyên nhân về một loại ngoại lệ để consumer chỉ cần một catch
            // rồi đẩy sang DLQ (VD-D-06).
            throw new EventContractException(
                $"Message không đọc được theo hợp đồng envelope: {ex.Message}", ex);
        }
    }

    private static EventEnvelope<TPayload> Kiem<TPayload>(EventEnvelope<TPayload> envelope)
        where TPayload : class, IEventPayload
    {
        if (EventRules.Violation(envelope.EventType, envelope.Version, envelope.Producer) is { } loi)
        {
            throw new EventContractException(
                $"Envelope {envelope.EventId} vi phạm hợp đồng: {loi}");
        }

        // Chặn đọc nhầm loại: message 'escrow.reserved' mà cố đọc thành
        // AnnotationApproved sẽ thành công nếu các trường tình cờ khớp nhau.
        if (!string.Equals(envelope.EventType, TPayload.EventType, StringComparison.Ordinal))
        {
            throw new EventContractException(
                $"Envelope {envelope.EventId} mang eventType '{envelope.EventType}' " +
                $"nhưng đang được đọc thành {typeof(TPayload).Name} " +
                $"('{TPayload.EventType}').");
        }

        return envelope;
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
