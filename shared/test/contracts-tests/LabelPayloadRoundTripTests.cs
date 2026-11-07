using System.Text.Json;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Gate;
using Crowd.Labeling;

namespace Crowd.Contracts.Tests;

/// <summary>
/// Event mang nhan (LabelPayload) phai di qua bus va quay ve BANG NHAU — record
/// so sanh theo gia tri, nen LabelPayload cung phai so sanh theo gia tri.
/// </summary>
public sealed class LabelPayloadRoundTripTests
{
    private static readonly LabelSchema TapNhan = LabelSchema.Doc(
        "{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\",\"classes\":[\"do\",\"xanh\"]}]}");

    /// <summary>
    /// gate.solved la event con mang nhan (attempt.submitted v2 chi con la tin hieu — quality-svc doc nhan qua
    /// gRPC, kien truc 3.22).
    /// </summary>
    [Fact]
    public void Gate_solved_di_qua_bus_khong_doi()
    {
        GateLabel cau = new GateLabel
        {
            SampleId = Guid.NewGuid(),
            StorageKey = "p/s.png",
            SampleMetadata = new SampleMetadata { Width = 64, Height = 48 }.ToRawJson(),
            LabelPayload = LabelPayload.Tao(TapNhan, "{\"label\":{\"labelIds\":[\"do\"]}}", null),
        };
        GateSolved goc = new GateSolved
        {
            SessionId = Guid.NewGuid(),
            LinkId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Labels = new[] { cau },
            SolvedAt = DateTimeOffset.UtcNow,
        };

        EventEnvelope<GateSolved> e = EventEnvelope.Create("gate-svc", Guid.NewGuid(), goc);
        string json = JsonSerializer.Serialize(e, CrowdJson.Options);

        // "data" va metadata la JSON long nhau that, khong phai chuoi chua JSON.
        Assert.Contains("\"labelPayload\":{\"taskType\":\"image\",\"schemaVersion\":1,\"data\":{\"label\":{\"labelIds\":[\"do\"]}}}", json);
        Assert.Contains("\"sampleMetadata\":{\"height\":48,\"width\":64}", json);

        EventEnvelope<GateSolved> doc = EventEnvelope.Deserialize<GateSolved>(json);
        // Record chua danh sach so sanh danh sach theo tham chieu → so tung cau.
        Assert.Equal(goc.SessionId, doc.Payload.SessionId);
        Assert.Equal(goc.Labels, doc.Payload.Labels);
    }
}
