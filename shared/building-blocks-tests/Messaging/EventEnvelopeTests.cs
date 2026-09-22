using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Messaging;
using Json.Schema;

namespace Crowd.BuildingBlocks.Tests.Messaging;

public sealed class EventEnvelopeTests
{
    /// <summary>Payload mẫu. <c>long</c> cho tiền = số nguyên đồng (VD-M-05, VD-M-07).</summary>
    private sealed record AnnotationApproved(Guid AnnotationId, Guid LabelerId, long AmountVnd);

    private static readonly Guid CorrelationId = Guid.CreateVersion7();

    private static EventEnvelope<AnnotationApproved> Sample(
        EventActor? actor = null, Guid? causationId = null) =>
        EventEnvelope.Create(
            eventType: "annotation.approved",
            version: 1,
            producer: "annotation-svc",
            correlationId: CorrelationId,
            payload: new AnnotationApproved(Guid.CreateVersion7(), Guid.CreateVersion7(), 200_000),
            causationId: causationId,
            actor: actor);

    // ---------------------------------------------------------------- Create

    [Fact]
    public void Create_sinh_EventId_dang_UUIDv7()
    {
        var envelope = Sample();

        // Ký tự thứ 15 trong dạng chuẩn là số hiệu phiên bản UUID.
        Assert.Equal('7', envelope.EventId.ToString()[14]);
    }

    [Fact]
    public void EventId_sap_xep_theo_thoi_diem_sinh()
    {
        // Tính chất quyết định vì sao chọn UUIDv7: ghi tuần tự vào cuối B-tree
        // của outbox và processed_events thay vì page split khắp nơi.
        //
        // LƯU Ý: Guid.CreateVersion7() của .NET chỉ tuần tự ở mức MILLI GIÂY —
        // nó không gắn bộ đếm đơn điệu, nên hai GUID sinh trong cùng một mili
        // giây có thứ tự ngẫu nhiên. Đủ cho mục đích cục bộ hóa ghi đĩa, nhưng
        // KHÔNG được dùng EventId để suy ra thứ tự sự kiện (dùng OccurredAt).
        var moc = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 50)
            .Select(i => Guid.CreateVersion7(moc.AddMilliseconds(i)).ToString())
            .ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Theory]
    [InlineData("annotation.approved")]
    [InlineData("project.publish_requested")]
    [InlineData("redundancy.increase_requested")]
    [InlineData("cpm.batch_settled")]
    public void Create_chap_nhan_eventType_trong_catalog(string eventType)
    {
        var envelope = EventEnvelope.Create(
            eventType, 1, "test-svc", CorrelationId,
            new AnnotationApproved(Guid.Empty, Guid.Empty, 0));

        Assert.Equal(eventType, envelope.EventType);
    }

    [Theory]
    [InlineData("AnnotationApproved")]      // PascalCase
    [InlineData("annotation-approved")]     // gạch ngang thay vì chấm
    [InlineData("annotation")]              // thiếu phần sau dấu chấm
    [InlineData("annotation.Approved")]     // hoa ở phần sau
    [InlineData("1annotation.approved")]    // bắt đầu bằng số
    [InlineData("annotation.approved.v2")]  // ba đoạn
    [InlineData("")]
    public void Create_tu_choi_eventType_sai_dinh_dang(string eventType)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EventEnvelope.Create(
                eventType, 1, "test-svc", CorrelationId,
                new AnnotationApproved(Guid.Empty, Guid.Empty, 0)));

        Assert.Equal("eventType", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_tu_choi_version_nho_hon_1(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventEnvelope.Create(
                "annotation.approved", version, "test-svc", CorrelationId,
                new AnnotationApproved(Guid.Empty, Guid.Empty, 0)));
    }

    [Fact]
    public void Create_giu_nguyen_OccurredAt_khi_duoc_truyen_vao()
    {
        // Sự kiện xảy ra trong quá khứ (nhập liệu bù, import) phải giữ đúng
        // thời điểm nghiệp vụ, không bị ghi đè thành "bây giờ" (VD-D-09).
        var quaKhu = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.FromHours(7));

        var envelope = EventEnvelope.Create(
            "annotation.approved", 1, "test-svc", CorrelationId,
            new AnnotationApproved(Guid.Empty, Guid.Empty, 0), occurredAt: quaKhu);

        Assert.Equal(quaKhu, envelope.OccurredAt);
    }

    // ------------------------------------------------------------- Định dạng

    [Fact]
    public void Serialize_dung_camelCase_cho_moi_truong()
    {
        var json = JsonSerializer.Serialize(Sample(), CrowdJson.Options);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(
            ["actor", "causationId", "correlationId", "eventId", "eventType",
             "occurredAt", "payload", "producer", "version"],
            node.Select(kv => kv.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Serialize_enum_thanh_chuoi_chu_thuong()
    {
        var actor = new EventActor(Guid.CreateVersion7(), ActorRole.Business);

        var json = JsonSerializer.Serialize(Sample(actor), CrowdJson.Options);
        var role = JsonNode.Parse(json)!["actor"]!["role"]!.GetValue<string>();

        // Số thứ tự enum sẽ vỡ ngay khi ai đó chèn giá trị vào giữa danh sách.
        Assert.Equal("business", role);
    }

    [Fact]
    public void Serialize_ghi_null_tuong_minh_thay_vi_bo_key()
    {
        // Định dạng trên dây đoán trước được => Python và Node không phải viết
        // nhánh "key này có thể không tồn tại".
        var json = JsonSerializer.Serialize(Sample(), CrowdJson.Options);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.True(node.ContainsKey("actor"));
        Assert.True(node.ContainsKey("causationId"));
        Assert.Null(node["actor"]);
        Assert.Null(node["causationId"]);
    }

    [Fact]
    public void RoundTrip_giu_nguyen_moi_gia_tri()
    {
        var goc = Sample(
            actor: new EventActor(Guid.CreateVersion7(), ActorRole.Labeler),
            causationId: Guid.CreateVersion7());

        var json = JsonSerializer.Serialize(goc, CrowdJson.Options);
        var lai = JsonSerializer.Deserialize<EventEnvelope<AnnotationApproved>>(
            json, CrowdJson.Options);

        Assert.Equal(goc, lai);
    }

    [Fact]
    public void Deserialize_that_bai_khi_thieu_truong_bat_buoc()
    {
        var json = JsonSerializer.Serialize(Sample(), CrowdJson.Options);
        var thieu = JsonNode.Parse(json)!.AsObject();
        thieu.Remove("correlationId");

        // Phải nổ ngay, không được âm thầm trả về Guid.Empty.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EventEnvelope<AnnotationApproved>>(
                thieu.ToJsonString(), CrowdJson.Options));
    }

    [Fact]
    public void Deserialize_that_bai_khi_sai_hoa_thuong()
    {
        // PropertyNameCaseInsensitive = false: lệch hoa thường nổ lúc test,
        // không âm thầm chạy được ở C# rồi hỏng khi Python đọc.
        var json = JsonSerializer.Serialize(Sample(), CrowdJson.Options);
        var lech = JsonNode.Parse(json)!.AsObject();
        lech["EventType"] = lech["eventType"]!.DeepClone();
        lech.Remove("eventType");

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EventEnvelope<AnnotationApproved>>(
                lech.ToJsonString(), CrowdJson.Options));
    }

    // ------------------------------------------- Đối chiếu với schema gốc

    private static readonly JsonSchema Schema =
        JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "contracts", "envelope.schema.json"));

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
        var results = Validate(Sample());

        Assert.True(results.IsValid, ThongBaoLoi(results));
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

    private static string ThongBaoLoi(EvaluationResults results) =>
        "Không khớp contracts/events/envelope.schema.json:\n" +
        string.Join("\n", (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}")));
}
