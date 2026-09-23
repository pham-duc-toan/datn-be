using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crowd.BuildingBlocks.Messaging
{
    /// <summary>
    /// Vỏ bọc chung cho MỌI event trên bus.
    /// Phải khớp contracts/events/envelope.schema.json — C#, Python và Node
    /// cùng sinh code từ file schema đó, nên lệch một trường là hỏng liên thông.
    ///
    /// Vì sao là "record" chứ không phải "class": record được trình biên dịch
    /// tự sinh thêm Equals/GetHashCode so sánh theo GIÁ TRỊ (thay vì theo tham
    /// chiếu), và ToString() in ra mọi thuộc tính. Test round-trip dựa vào đúng
    /// tính chất đó: serialize rồi deserialize rồi so sánh bằng một dòng.
    ///
    /// Vì sao "sealed": event là sự kiện đã xảy ra, không có khái niệm event con.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EventEnvelope<TPayload> where TPayload : class, IEventPayload
    {
        // "required" = bắt buộc phải gán khi tạo object, trình biên dịch kiểm.
        //              System.Text.Json cũng tôn trọng: thiếu trường khi đọc JSON
        //              thì NÉM LỖI, không âm thầm để null.
        // "init"     = chỉ gán được lúc khởi tạo, sau đó không sửa được.

        /// <summary>UUIDv7. Consumer dùng làm khóa idempotency (VD-D-02).</summary>
        public required Guid EventId { get; init; }

        /// <summary>Dạng &lt;aggregate&gt;.&lt;quá_khứ&gt;, vd: annotation.approved</summary>
        public required string EventType { get; init; }

        /// <summary>Phiên bản schema của payload. Tăng khi có breaking change (VD-D-08).</summary>
        public required int Version { get; init; }

        /// <summary>
        /// Thời điểm SỰ KIỆN XẢY RA về mặt nghiệp vụ, không phải lúc publish.
        /// Hai cái lệch nhau khi outbox tồn đọng — mọi tính toán nghiệp vụ dùng cái này.
        /// </summary>
        public required DateTimeOffset OccurredAt { get; init; }

        /// <summary>Tên service phát, vd: annotation-svc</summary>
        public required string Producer { get; init; }

        /// <summary>Sinh ở gateway, xuyên mọi hop và mọi event phát sinh (VD-D-03).</summary>
        public required Guid CorrelationId { get; init; }

        /// <summary>
        /// EventId của event đã gây ra event này.
        /// Cho phép dựng lại cây nhân quả: "vì sao labeler này được trả tiền?"
        ///
        /// Guid? là Nullable&lt;Guid&gt; — Guid là struct nên không null được,
        /// dấu ? tạo ra một kiểu bọc cho phép mang giá trị null.
        /// </summary>
        public Guid? CausationId { get; init; }

        /// <summary>
        /// null = hệ thống tự động, không phải người dùng.
        ///
        /// EventActor? là kiểu tham chiếu nullable — dấu ? ở đây chỉ là CHÚ THÍCH
        /// cho trình biên dịch cảnh báo khi dùng mà chưa kiểm null. Hoạt động nhờ
        /// Nullable=enable trong Directory.Build.props.
        /// </summary>
        public EventActor? Actor { get; init; }

        public required TPayload Payload { get; init; }
    }

    /// <summary>
    /// Hai cổng duy nhất cho event: Create đi ra, Deserialize đi vào.
    /// Cả hai chạy CÙNG một bộ luật (EventRules) nên chiều vào và chiều ra
    /// không thể trôi khỏi nhau.
    ///
    /// KHÔNG gọi JsonSerializer trực tiếp cho event. Làm vậy là bỏ qua các luật
    /// chỉ tồn tại ở đây — đúng lý do chiều vào từng lỏng hơn chiều ra.
    ///
    /// Lớp này không generic, còn phương thức thì generic. Nhờ vậy C# tự suy ra
    /// TPayload từ chính payload truyền vào, không phải gõ lại tên kiểu.
    /// </summary>
    public static class EventEnvelope
    {
        /// <summary>
        /// Cổng RA. eventType và version lấy thẳng từ TPayload nên không thể
        /// gắn nhầm loại event cho payload.
        /// </summary>
        /// <param name="occurredAt">
        /// Bỏ trống = bây giờ. Chỉ truyền vào khi sự kiện đã xảy ra trong quá khứ.
        /// </param>
        public static EventEnvelope<TPayload> Create<TPayload>(
            string producer,
            Guid correlationId,
            TPayload payload,
            Guid? causationId = null,
            EventActor? actor = null,
            DateTimeOffset? occurredAt = null)
            where TPayload : class, IEventPayload
        {
            if (string.IsNullOrWhiteSpace(producer))
            {
                throw new ArgumentException("producer không được rỗng", nameof(producer));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            // TPayload.EventType đọc được vì IEventPayload khai hai thành viên đó
            // là "static abstract" — mỗi payload tự khai giá trị của mình.
            string eventType = TPayload.EventType;
            int version = TPayload.Version;

            string? viPham = EventRules.Violation(eventType, version, producer);
            if (viPham != null)
            {
                throw new ArgumentException(
                    typeof(TPayload).Name + " khai báo sai hợp đồng: " + viPham,
                    nameof(payload));
            }

            DateTimeOffset thoiDiem;
            if (occurredAt.HasValue)
            {
                thoiDiem = occurredAt.Value;
            }
            else
            {
                thoiDiem = DateTimeOffset.UtcNow;
            }

            return new EventEnvelope<TPayload>
            {
                // UUIDv7 nhúng timestamp ở đầu nên ghi tuần tự vào cuối B-tree.
                // UUIDv4 ngẫu nhiên gây page split liên tục trên outbox và
                // processed_events.
                EventId = Guid.CreateVersion7(),
                EventType = eventType,
                Version = version,
                OccurredAt = thoiDiem,
                Producer = producer,
                CorrelationId = correlationId,
                CausationId = causationId,
                Actor = actor,
                Payload = payload,
            };
        }

        /// <summary>Cổng VÀO cho chuỗi JSON.</summary>
        public static EventEnvelope<TPayload> Deserialize<TPayload>(string json)
            where TPayload : class, IEventPayload
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            EventEnvelope<TPayload>? envelope;

            try
            {
                envelope = JsonSerializer.Deserialize<EventEnvelope<TPayload>>(
                    json, CrowdJson.Options);
            }
            catch (JsonException ex)
            {
                throw BocLoiDoc(ex);
            }

            return Kiem(envelope);
        }

        /// <summary>
        /// Cổng VÀO cho message lấy thẳng từ bus — RabbitMQ trả về mảng byte
        /// UTF-8 chứ không trả về chuỗi.
        /// </summary>
        public static EventEnvelope<TPayload> Deserialize<TPayload>(byte[] utf8Json)
            where TPayload : class, IEventPayload
        {
            if (utf8Json == null)
            {
                throw new ArgumentNullException(nameof(utf8Json));
            }

            EventEnvelope<TPayload>? envelope;

            try
            {
                envelope = JsonSerializer.Deserialize<EventEnvelope<TPayload>>(
                    utf8Json, CrowdJson.Options);
            }
            catch (JsonException ex)
            {
                throw BocLoiDoc(ex);
            }

            return Kiem(envelope);
        }

        /// <summary>
        /// Gộp mọi nguyên nhân đọc hỏng về một loại ngoại lệ, để consumer chỉ
        /// cần một catch rồi đẩy sang DLQ (VD-D-06).
        /// </summary>
        private static EventContractException BocLoiDoc(JsonException ex)
        {
            return new EventContractException(
                "Message không đọc được theo hợp đồng envelope: " + ex.Message, ex);
        }

        /// <summary>Chạy các luật mà System.Text.Json không biết.</summary>
        private static EventEnvelope<TPayload> Kiem<TPayload>(EventEnvelope<TPayload>? envelope)
            where TPayload : class, IEventPayload
        {
            if (envelope == null)
            {
                throw new EventContractException("Message là JSON null.");
            }

            string? viPham = EventRules.Violation(
                envelope.EventType, envelope.Version, envelope.Producer);

            if (viPham != null)
            {
                throw new EventContractException(
                    "Envelope " + envelope.EventId + " vi phạm hợp đồng: " + viPham);
            }

            // Chặn đọc nhầm loại: message 'escrow.reserved' mà cố đọc thành
            // AnnotationApproved sẽ thành công nếu các trường tình cờ khớp nhau.
            if (!string.Equals(envelope.EventType, TPayload.EventType, StringComparison.Ordinal))
            {
                throw new EventContractException(
                    "Envelope " + envelope.EventId + " mang eventType '" + envelope.EventType
                    + "' nhưng đang được đọc thành " + typeof(TPayload).Name
                    + " ('" + TPayload.EventType + "').");
            }

            return envelope;
        }
    }

    /// <summary>
    /// Ai gây ra hành động. Nền của audit log FP-06.
    ///
    /// Viết đầy đủ thay vì dạng rút gọn "record EventActor(Guid UserId, ...)"
    /// để thấy rõ có constructor và có hai thuộc tính.
    /// </summary>
    public sealed record EventActor
    {
        public EventActor(Guid userId, ActorRole role)
        {
            UserId = userId;
            Role = role;
        }

        public Guid UserId { get; init; }

        public ActorRole Role { get; init; }
    }

    /// <summary>
    /// Serialize thành chuỗi chữ thường qua CrowdJson.Options.
    ///
    /// KHÔNG gắn [JsonConverter] lên enum này: attribute sẽ thắng tùy chọn và
    /// ghi ra "Business" thay vì "business", phá schema.
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
}
