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
            LabelerId = Guid.NewGuid(),
            LabelPayload = LabelPayload.PhanLoai("do"),
            LeasedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        EventEnvelope<AssignmentSubmitted> e = EventEnvelope.Create("task-svc", Guid.NewGuid(), goc);
        string json = JsonSerializer.Serialize(e, CrowdJson.Options);

        // "data" la JSON long nhau that, khong phai chuoi chua JSON.
        Assert.Contains("\"labelPayload\":{\"taskType\":\"imageClassification\",\"schemaVersion\":1,\"data\":{\"labelIds\":[\"do\"]}}", json);

        EventEnvelope<AssignmentSubmitted> doc = EventEnvelope.Deserialize<AssignmentSubmitted>(json);
        Assert.Equal(goc, doc.Payload);
    }
}
