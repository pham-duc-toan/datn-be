using System.Text.Json;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;

namespace Crowd.BuildingBlocks.Persistence.Tests.Outbox;

public sealed class OutboxMessageTests
{
    private sealed record NhanDuocDuyet : IEventPayload
    {
        public static string EventType => "annotation.approved";
        public static int Version => 1;

        public required Guid AnnotationId { get; init; }
        public required long AmountVnd { get; init; }
    }

    /// <summary>Chín trường của envelope, theo thứ tự chữ cái.</summary>
    private static readonly string[] TruongCuaEnvelope = new string[]
    {
        "actor", "causationId", "correlationId", "eventId", "eventType",
        "occurredAt", "payload", "producer", "version",
    };

    /// <summary>Khoảng cách lùi lịch mong đợi sau 5 lần thất bại liên tiếp.</summary>
    private static readonly TimeSpan[] LuiLichMongDoi = new TimeSpan[]
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(32),
    };

    private static EventEnvelope<NhanDuocDuyet> Envelope() =>
        EventEnvelope.Create(
            producer: "annotation-svc",
            correlationId: Guid.CreateVersion7(),
            payload: new NhanDuocDuyet
            {
                AnnotationId = Guid.CreateVersion7(),
                AmountVnd = 200_000,
            });

    [Fact]
    public void From_dung_EventId_lam_khoa_chinh()
    {
        // Không sinh id mới: dùng lại EventId (UUIDv7) để ghi tuần tự vào cuối
        // B-tree, và để idempotency ở consumer khóa theo cùng một giá trị.
        var envelope = Envelope();

        Assert.Equal(envelope.EventId, OutboxMessage.From(envelope).Id);
    }

    [Fact]
    public void From_luu_nguyen_ca_envelope_chu_khong_chi_payload()
    {
        // Dispatcher đẩy nguyên khối byte này lên bus nên không cần biết payload
        // là kiểu gì — đó là lý do Crowd.BuildingBlocks.Persistence KHÔNG tham
        // chiếu Crowd.Contracts.
        var envelope = Envelope();

        var doc = JsonDocument.Parse(OutboxMessage.From(envelope).EnvelopeJson);

        Assert.Equal(
            TruongCuaEnvelope,
            doc.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void From_doc_lai_duoc_bang_cong_vao_chuan()
    {
        var goc = Envelope();

        var lai = EventEnvelope.Deserialize<NhanDuocDuyet>(
            OutboxMessage.From(goc).EnvelopeJson);

        Assert.Equal(goc, lai);
    }

    [Fact]
    public void Dong_moi_la_chua_gui()
    {
        var msg = OutboxMessage.From(Envelope());

        Assert.Null(msg.PublishedAt);
        Assert.Equal(0, msg.AttemptCount);
        Assert.Null(msg.LastError);
    }

    [Fact]
    public void MarkPublished_xoa_loi_cu()
    {
        var msg = OutboxMessage.From(Envelope());
        var luc = DateTimeOffset.UtcNow;

        msg.MarkFailed("RabbitMQ không kết nối được", luc, TimeSpan.FromMinutes(5));
        msg.MarkPublished(luc);

        Assert.Equal(luc, msg.PublishedAt);
        Assert.Null(msg.LastError);
    }

    [Fact]
    public void MarkFailed_lui_lich_theo_ham_mu()
    {
        var msg = OutboxMessage.From(Envelope());
        var luc = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        var khoangCach = new List<TimeSpan>();
        for (var i = 0; i < 5; i++)
        {
            msg.MarkFailed("lỗi", luc, TimeSpan.FromMinutes(5));
            khoangCach.Add(msg.NextAttemptAt - luc);
        }

        // 2s, 4s, 8s, 16s, 32s — tránh quay vòng liên tục khi RabbitMQ đang sập.
        Assert.Equal(
            LuiLichMongDoi,
            khoangCach);
    }

    [Fact]
    public void MarkFailed_khong_lui_qua_5_phut()
    {
        var msg = OutboxMessage.From(Envelope());
        var luc = DateTimeOffset.UtcNow;

        for (var i = 0; i < 20; i++)
        {
            msg.MarkFailed("lỗi", luc, TimeSpan.FromMinutes(5));
        }

        // Có trần: RabbitMQ sập nửa ngày thì vẫn phải thử lại mỗi 5 phút,
        // không được giãn tới hàng giờ.
        Assert.Equal(TimeSpan.FromMinutes(5), msg.NextAttemptAt - luc);
        Assert.Equal(20, msg.AttemptCount);
    }

    [Fact]
    public void MarkFailed_cat_bot_thong_bao_loi_qua_dai()
    {
        var msg = OutboxMessage.From(Envelope());

        msg.MarkFailed(new string('x', 5000), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        // Cột giới hạn 2000 ký tự — cắt ở tầng domain để INSERT không bị từ chối.
        Assert.Equal(2000, msg.LastError!.Length);
    }
}
