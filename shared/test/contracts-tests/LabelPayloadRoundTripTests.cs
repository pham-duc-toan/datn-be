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
    public void Assignment_submitted_di_qua_bus_khong_doi()
    {
        AssignmentSubmitted goc = new AssignmentSubmitted
        {
            AssignmentId = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            SampleId = Guid.NewGuid(),
            StorageKey = "p/s.png",
            SampleMetadata = new SampleMetadata { Width = 64, Height = 48 }.ToRawJson(),
            LabelerId = Guid.NewGuid(),
            LabelPayload = LabelPayload.Tao(TapNhan, "{\"label\":{\"labelIds\":[\"do\"]}}", null),
            LeasedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        EventEnvelope<AssignmentSubmitted> e = EventEnvelope.Create("task-svc", Guid.NewGuid(), goc);
        string json = JsonSerializer.Serialize(e, CrowdJson.Options);

        // "data" va metadata la JSON long nhau that, khong phai chuoi chua JSON.
        Assert.Contains("\"labelPayload\":{\"taskType\":\"image\",\"schemaVersion\":1,\"data\":{\"label\":{\"labelIds\":[\"do\"]}}}", json);
        Assert.Contains("\"sampleMetadata\":{\"height\":48,\"width\":64}", json);

        EventEnvelope<AssignmentSubmitted> doc = EventEnvelope.Deserialize<AssignmentSubmitted>(json);
        Assert.Equal(goc, doc.Payload);
    }
}
