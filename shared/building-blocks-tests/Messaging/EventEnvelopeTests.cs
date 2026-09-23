using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Messaging;
using Json.Schema;

namespace Crowd.BuildingBlocks.Tests.Messaging;

public sealed class EventEnvelopeTests
{
    /// <summary>
    /// Payload mẫu. Viết dạng <c>required</c> chứ KHÔNG dùng positional record —
    /// positional record thiếu trường sẽ âm thầm thành 0 đồng (VD-M-05, VD-M-07).
    /// </summary>
    private sealed record AnnotationApproved : IEventPayload
    {
        public static string EventType => "annotation.approved";
        public static int Version => 1;

        public required Guid AnnotationId { get; init; }
        public required Guid LabelerId { get; init; }
        public required long AmountVnd { get; init; }
    }

    private static readonly Guid CorrelationId = Guid.CreateVersion7();

    private static AnnotationApproved Payload() => new()
    {
        AnnotationId = Guid.CreateVersion7(),
        LabelerId = Guid.CreateVersion7(),
        AmountVnd = 200_000,
    };

    private static EventEnvelope<AnnotationApproved> Sample(
        EventActor? actor = null, Guid? causationId = null) =>
        EventEnvelope.Create(
            producer: "annotation-svc",
            correlationId: CorrelationId,
            payload: Payload(),
            causationId: causationId,
            actor: actor);

    private static string Json(EventActor? actor = null) =>
        JsonSerializer.Serialize(Sample(actor), CrowdJson.Options);

    // ============================================================ CỔNG RA

    [Fact]
    public void Create_sinh_EventId_dang_UUIDv7()
    {
        // Ký tự thứ 15 trong dạng chuẩn là số hiệu phiên bản UUID.
        Assert.Equal('7', Sample().EventId.ToString()[14]);
    }

    [Fact]
    public void EventId_sap_xep_theo_thoi_diem_sinh()
    {
        // Tính chất quyết định vì sao chọn UUIDv7: ghi tuần tự vào cuối B-tree
        // của outbox và processed_events thay vì page split khắp nơi.
        //
        // LƯU Ý: Guid.CreateVersion7() của .NET chỉ tuần tự ở mức MILI GIÂY —
        // nó không gắn bộ đếm đơn điệu, nên hai GUID sinh trong cùng một mili
        // giây có thứ tự ngẫu nhiên. Đủ cho mục đích cục bộ hóa ghi đĩa, nhưng
        // KHÔNG được dùng EventId để suy ra thứ tự sự kiện (dùng OccurredAt).
        var moc = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 50)
            .Select(i => Guid.CreateVersion7(moc.AddMilliseconds(i)).ToString())
            .ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    // eventType và version giờ do CHÍNH payload khai, không truyền vào Create nữa.
    // Nên test chuyển thành: payload khai sai thì Create phải từ chối.

    private sealed record PayloadEventTypeXau : IEventPayload
    {
        public static string EventType => "KHONG_HOP_LE";
        public static int Version => 1;
        public required int X { get; init; }
    }

    private sealed record PayloadVersionXau : IEventPayload
    {
        public static string EventType => "thu.nghiem";
        public static int Version => 0;
        public required int X { get; init; }
    }

    [Fact]
    public void Create_tu_choi_payload_khai_eventType_sai()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EventEnvelope.Create("test-svc", CorrelationId, new PayloadEventTypeXau { X = 1 }));

        Assert.Contains("KHONG_HOP_LE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_tu_choi_payload_khai_version_nho_hon_1()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EventEnvelope.Create("test-svc", CorrelationId, new PayloadVersionXau { X = 1 }));

        Assert.Contains("version", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("annotation.approved", true)]
    [InlineData("project.publish_requested", true)]
    [InlineData("cpm.batch_settled", true)]
    [InlineData("AnnotationApproved", false)]      // PascalCase
    [InlineData("annotation-approved", false)]     // gạch ngang thay vì chấm
    [InlineData("annotation", false)]              // thiếu phần sau dấu chấm
    [InlineData("annotation.Approved", false)]     // hoa ở phần sau
    [InlineData("1annotation.approved", false)]    // bắt đầu bằng số
    [InlineData("annotation.approved.v2", false)]  // ba đoạn
    [InlineData("", false)]
    public void EventRules_kiem_dung_dinh_dang_eventType(string eventType, bool hopLe)
    {
        Assert.Equal(hopLe, EventRules.IsValidEventType(eventType));
    }

    [Fact]
    public void Create_giu_nguyen_OccurredAt_khi_duoc_truyen_vao()
    {
        // Sự kiện xảy ra trong quá khứ (nhập liệu bù, import) phải giữ đúng
        // thời điểm nghiệp vụ, không bị ghi đè thành "bây giờ" (VD-D-09).
        var quaKhu = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.FromHours(7));

        var envelope = EventEnvelope.Create(
            "test-svc", CorrelationId, Payload(), occurredAt: quaKhu);

        Assert.Equal(quaKhu, envelope.OccurredAt);
    }

    // ============================================================ CỔNG VÀO
    //
    // Bốn test dưới đây bịt bốn lỗ đã tìm ra: trước khi có cổng vào, chiều nhận
    // chỉ gọi thẳng JsonSerializer nên KHÔNG kiểm regex, version, và trường lạ.

    [Fact]
    public void Deserialize_doc_lai_dung_envelope_hop_le()
    {
        var goc = Sample(new EventActor(Guid.CreateVersion7(), ActorRole.Labeler));

        var lai = EventEnvelope.Deserialize<AnnotationApproved>(
            JsonSerializer.Serialize(goc, CrowdJson.Options));

        Assert.Equal(goc, lai);
    }

    [Fact]
    public void Deserialize_doc_duoc_tu_byte_utf8()
    {
        // RabbitMQ trả về byte, không trả về string.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Sample(), CrowdJson.Options);

        var lai = EventEnvelope.Deserialize<AnnotationApproved>(bytes.AsSpan());

        Assert.Equal("annotation.approved", lai.EventType);
    }

    [Fact]
    public void Deserialize_chan_truong_la()  // lỗ 1 — additionalProperties: false
    {
        var them = JsonNode.Parse(Json())!.AsObject();
        them["tenantId"] = "co-gi-do-sai";

        var ex = Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(them.ToJsonString()));

        Assert.Contains("tenantId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_chan_eventType_sai_dinh_dang()  // lỗ 2
    {
        var hong = JsonNode.Parse(Json())!.AsObject();
        hong["eventType"] = "GARBAGE";

        var ex = Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(hong.ToJsonString()));

        Assert.Contains("GARBAGE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_chan_version_nho_hon_1()  // lỗ 3
    {
        var hong = JsonNode.Parse(Json())!.AsObject();
        hong["version"] = -5;

        var ex = Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(hong.ToJsonString()));

        Assert.Contains("-5", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("khong-phai-json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Deserialize_gop_moi_loi_ve_EventContractException(string json)
    {
        // Consumer chỉ cần MỘT catch rồi đẩy sang DLQ (VD-D-06), bất kể nguyên nhân
        // là JSON hỏng, thiếu trường bắt buộc, hay vi phạm quy tắc nghiệp vụ.
        Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(json));
    }

    [Fact]
    public void Deserialize_chan_thieu_truong_bat_buoc()
    {
        var thieu = JsonNode.Parse(Json())!.AsObject();
        thieu.Remove("correlationId");

        Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(thieu.ToJsonString()));
    }

    [Fact]
    public void Deserialize_chan_sai_hoa_thuong()
    {
        // PropertyNameCaseInsensitive = false: lệch hoa thường nổ lúc test,
        // không âm thầm chạy được ở C# rồi hỏng khi Python đọc.
        var lech = JsonNode.Parse(Json())!.AsObject();
        lech["EventType"] = lech["eventType"]!.DeepClone();
        lech.Remove("eventType");

        Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(lech.ToJsonString()));
    }

    [Fact]
    public void Deserialize_chan_doc_nham_loai_payload()
    {
        // Message 'escrow.reserved' cố đọc thành AnnotationApproved sẽ THÀNH CÔNG
        // nếu các trường tình cờ khớp — nên phải so eventType với TPayload.EventType.
        var nham = JsonNode.Parse(Json())!.AsObject();
        nham["eventType"] = "escrow.reserved";

        var ex = Assert.Throws<EventContractException>(() =>
            EventEnvelope.Deserialize<AnnotationApproved>(nham.ToJsonString()));

        Assert.Contains("escrow.reserved", ex.Message, StringComparison.Ordinal);
    }

    // ============================================ QUY ƯỚC PAYLOAD (lỗ 4)

    /// <summary>Positional record: tham số vị trí KHÔNG mang <c>required</c>.</summary>
    private sealed record PayloadXau(long AmountVnd) : IEventPayload
    {
        public static string EventType => "thu.nghiem";
        public static int Version => 1;
    }

    private sealed record PayloadTot : IEventPayload
    {
        public static string EventType => "thu.nghiem";
        public static int Version => 1;

        public required long AmountVnd { get; init; }
        public string? GhiChu { get; init; }          // nullable = vắng mặt có chủ đích
        public Guid? NguoiDuyet { get; init; }
    }

    [Fact]
    public void PayloadContract_bat_duoc_positional_record()
    {
        var loi = EventPayloadContract.FindViolations(typeof(PayloadXau));

        // Không bắt được cái này thì một event annotation.approved thiếu trường
        // số tiền sẽ âm thầm thành 0 đồng.
        var chiTiet = Assert.Single(loi);
        Assert.Contains("AmountVnd", chiTiet, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadContract_chap_nhan_required_va_nullable()
    {
        Assert.Empty(EventPayloadContract.FindViolations(typeof(PayloadTot)));
    }

    [Fact]
    public void PayloadContract_bat_duoc_khai_bao_eventType_sai()
    {
        var loi = EventPayloadContract.FindViolations(typeof(PayloadEventTypeXau));

        Assert.Contains(loi, l => l.Contains("KHONG_HOP_LE", StringComparison.Ordinal));
    }

    [Fact]
    public void PayloadContract_chap_nhan_payload_that_dang_dung()
    {
        Assert.Empty(EventPayloadContract.FindViolations(typeof(AnnotationApproved)));
    }

    // ============================================== ĐỐI CHIẾU SCHEMA GỐC

    private static readonly JsonSchema Schema = JsonSchema.FromFile(
        Path.Combine(AppContext.BaseDirectory, "contracts", "envelope.schema.json"));

    private static EvaluationResults Validate(object envelope)
    {
        var json = JsonSerializer.Serialize(envelope, envelope.GetType(), CrowdJson.Options);
        using var doc = JsonDocument.Parse(json);

        return Schema.Evaluate(
            doc.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,   // ép kiểm format uuid và date-time
            });
    }

    [Fact]
    public void Envelope_day_du_khop_schema_goc()
    {
        var results = Validate(Sample(
            actor: new EventActor(Guid.CreateVersion7(), ActorRole.Admin),
            causationId: Guid.CreateVersion7()));

        Assert.True(results.IsValid, ThongBaoLoi(results));
    }

    [Fact]
    public void Envelope_khong_actor_khong_causation_khop_schema_goc()
    {
        Assert.True(Validate(Sample()).IsValid, ThongBaoLoi(Validate(Sample())));
    }

    [Theory]
    [InlineData(ActorRole.Business)]
    [InlineData(ActorRole.Labeler)]
    [InlineData(ActorRole.Sharer)]
    [InlineData(ActorRole.Guest)]
    [InlineData(ActorRole.Admin)]
    [InlineData(ActorRole.System)]
    public void Moi_ActorRole_nam_trong_enum_cua_schema(ActorRole role)
    {
        var results = Validate(Sample(new EventActor(Guid.CreateVersion7(), role)));

        Assert.True(results.IsValid, ThongBaoLoi(results));
    }

    // ---------------------------------------------------------- Định dạng

    [Fact]
    public void Serialize_dung_camelCase_cho_moi_truong()
    {
        var node = JsonNode.Parse(Json())!.AsObject();

        Assert.Equal(
            ["actor", "causationId", "correlationId", "eventId", "eventType",
             "occurredAt", "payload", "producer", "version"],
            node.Select(kv => kv.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Serialize_enum_thanh_chuoi_chu_thuong()
    {
        var json = Json(new EventActor(Guid.CreateVersion7(), ActorRole.Business));

        // Số thứ tự enum sẽ vỡ ngay khi ai đó chèn giá trị vào giữa danh sách.
        Assert.Equal("business", JsonNode.Parse(json)!["actor"]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_ghi_null_tuong_minh_thay_vi_bo_key()
    {
        // Định dạng trên dây đoán trước được => Python và Node không phải viết
        // nhánh "key này có thể không tồn tại".
        var node = JsonNode.Parse(Json())!.AsObject();

        Assert.True(node.ContainsKey("actor"));
        Assert.True(node.ContainsKey("causationId"));
        Assert.Null(node["actor"]);
        Assert.Null(node["causationId"]);
    }

    private static string ThongBaoLoi(EvaluationResults results) =>
        "Không khớp contracts/events/envelope.schema.json:\n" +
        string.Join("\n", (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}")));
}
