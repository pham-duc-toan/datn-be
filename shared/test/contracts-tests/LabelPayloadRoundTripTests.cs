using System.Text.Json;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Tasking;
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

    [Fact]
    public void Attempt_submitted_di_qua_bus_khong_doi()
    {
        AttemptAnswer cau = new AttemptAnswer
        {
            SampleId = Guid.NewGuid(),
            StorageKey = "p/s.png",
            SampleMetadata = new SampleMetadata { Width = 64, Height = 48 }.ToRawJson(),
            LabelPayload = LabelPayload.Tao(TapNhan, "{\"label\":{\"labelIds\":[\"do\"]}}", null),
        };
        AttemptAnswer boTrong = new AttemptAnswer
        {
            SampleId = Guid.NewGuid(),
            SampleMetadata = new SampleMetadata { Width = 10, Height = 10 }.ToRawJson(),
        };
        AttemptSubmitted goc = new AttemptSubmitted
        {
            AttemptId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LabelerId = Guid.NewGuid(),
            Answers = new[] { cau, boTrong },
            GoldTotal = 2,
            GoldCorrect = 2,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        EventEnvelope<AttemptSubmitted> e = EventEnvelope.Create("task-svc", Guid.NewGuid(), goc);
        string json = JsonSerializer.Serialize(e, CrowdJson.Options);

        // "data" va metadata la JSON long nhau that, khong phai chuoi chua JSON.
        Assert.Contains("\"labelPayload\":{\"taskType\":\"image\",\"schemaVersion\":1,\"data\":{\"label\":{\"labelIds\":[\"do\"]}}}", json);
        Assert.Contains("\"sampleMetadata\":{\"height\":48,\"width\":64}", json);

        EventEnvelope<AttemptSubmitted> doc = EventEnvelope.Deserialize<AttemptSubmitted>(json);
        // Record chua danh sach so sanh danh sach theo tham chieu → so tung cau.
        Assert.Equal(goc.AttemptId, doc.Payload.AttemptId);
        Assert.Equal(goc.Answers, doc.Payload.Answers);
        Assert.Null(doc.Payload.Answers[1].LabelPayload);
    }
}
