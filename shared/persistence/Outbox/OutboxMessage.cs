using System;
using System.Text.Json;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Một event đang chờ đẩy lên bus. Là hình dạng của MỘT DÒNG trong bảng outbox.
    ///
    /// LÝ DO TỒN TẠI (VD-D-01): nếu service ghi nghiệp vụ xong rồi mới publish,
    /// chết ở giữa hai bước là mất event vĩnh viễn — nhãn đã duyệt trong database
    /// nhưng ledger-svc không bao giờ biết, labeler không được trả tiền, và
    /// không có gì trong hệ thống phát hiện ra.
    ///
    /// CÁCH GIẢI: dòng này được INSERT trong CÙNG transaction với dữ liệu nghiệp
    /// vụ. Hoặc cả hai cùng commit, hoặc cả hai cùng rollback. Một tiến trình nền
    /// riêng đọc bảng này rồi đẩy lên RabbitMQ — chết thì lần sau chạy lại.
    ///
    /// Bảng này nằm trong database của CHÍNH service, không phải một DB dùng
    /// chung — đúng ý nghĩa của outbox là tính nguyên tử với dữ liệu nghiệp vụ.
    /// </summary>
    public sealed class OutboxMessage
    {
        /// <summary>Trần giãn cách: RabbitMQ sập nửa ngày thì vẫn thử lại mỗi 5 phút.</summary>

        /// <summary>Độ dài tối đa của cột last_error trong database.</summary>
        private const int DoDaiLoiToiDa = 2000;

        /// <summary>
        /// EF Core cần một constructor không tham số để dựng lại đối tượng khi
        /// đọc từ database. Để private nên code nghiệp vụ không gọi được — muốn
        /// tạo phải qua phương thức From().
        /// </summary>
        private OutboxMessage()
        {
            EventType = string.Empty;
            EnvelopeJson = string.Empty;
        }

        /// <summary>
        /// Bằng chính EventId của envelope, nên là UUIDv7 — ghi tuần tự vào cuối
        /// B-tree thay vì page split khắp nơi. Consumer cũng khóa idempotency
        /// theo cùng giá trị này.
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>Tra cứu và lọc mà không phải mở EnvelopeJson ra đọc.</summary>
        public string EventType { get; private set; }

        public int Version { get; private set; }

        /// <summary>Lần theo một luồng xuyên 17 service khi điều tra sự cố (VD-D-03).</summary>
        public Guid CorrelationId { get; private set; }

        /// <summary>Thời điểm nghiệp vụ, không phải lúc ghi vào bảng này.</summary>
        public DateTimeOffset OccurredAt { get; private set; }

        /// <summary>
        /// TOÀN BỘ envelope đã serialize, không phải chỉ payload.
        /// Dispatcher chỉ việc đẩy nguyên khối byte này lên bus — nó không cần
        /// biết payload là kiểu gì, nên Crowd.BuildingBlocks.Persistence KHÔNG
        /// phải tham chiếu Crowd.Contracts.
        /// </summary>
        public string EnvelopeJson { get; private set; }

        /// <summary>null = chưa gửi. Là cột quyết định của index bộ phận.</summary>
        public DateTimeOffset? PublishedAt { get; private set; }

        /// <summary>Số lần đã thử. Dùng cho backoff và để nhận ra message độc (VD-D-06).</summary>
        public int AttemptCount { get; private set; }

        /// <summary>Lỗi gần nhất — để chẩn đoán mà không phải bới log.</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// Sớm nhất được thử lại. Thất bại thì lùi theo hàm mũ, tránh quay vòng
        /// liên tục khi RabbitMQ đang sập.
        /// </summary>
        public DateTimeOffset NextAttemptAt { get; private set; }

        /// <summary>Đóng gói một envelope thành dòng chờ gửi.</summary>
        public static OutboxMessage From<TPayload>(EventEnvelope<TPayload> envelope)
            where TPayload : class, IEventPayload
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            OutboxMessage dong = new OutboxMessage();

            dong.Id = envelope.EventId;
            dong.EventType = envelope.EventType;
            dong.Version = envelope.Version;
            dong.CorrelationId = envelope.CorrelationId;
            dong.OccurredAt = envelope.OccurredAt;
            dong.EnvelopeJson = JsonSerializer.Serialize(envelope, CrowdJson.Options);
            dong.PublishedAt = null;
            dong.AttemptCount = 0;
            dong.LastError = null;
            dong.NextAttemptAt = envelope.OccurredAt;

            return dong;
        }

        /// <summary>Đánh dấu đã đẩy lên bus thành công.</summary>
        public void MarkPublished(DateTimeOffset at)
        {
            PublishedAt = at;
            LastError = null;
        }

        /// <summary>
        /// Ghi nhận thất bại và lùi lịch thử lại: 2s, 4s, 8s... tới trần tranGianCach
        /// (setting outbox.retry_max_delay, mặc định 5 phút).
        /// Không bỏ cuộc hẳn — message nằm lại cho tới khi gửi được hoặc có
        /// người can thiệp.
        /// </summary>
        public void MarkFailed(string error, DateTimeOffset now, TimeSpan tranGianCach)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            AttemptCount = AttemptCount + 1;

            if (error.Length > DoDaiLoiToiDa)
            {
                LastError = error.Substring(0, DoDaiLoiToiDa);
            }
            else
            {
                LastError = error;
            }

            // Chặn số mũ ở 20 chỉ để Math.Pow không tràn thành vô cực; trần thật
            // sự là tranGianCach (mặc định 300 giây: có hiệu lực từ lần thử thứ 9,
            // vì 2 mũ 9 = 512 giây).
            int soMu = AttemptCount;
            if (soMu > 20)
            {
                soMu = 20;
            }

            double giay = Math.Pow(2, soMu);
            if (giay > tranGianCach.TotalSeconds)
            {
                giay = tranGianCach.TotalSeconds;
            }

            NextAttemptAt = now + TimeSpan.FromSeconds(giay);
        }
    }
}
