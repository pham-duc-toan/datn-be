using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Labeling;

namespace Crowd.Tasking.Api.Dtos
{
    /// <summary>Mot task dang giu — du de frontend dung man hinh gan nhan.</summary>
    public sealed class LeaseResponse
    {
        public required Guid AssignmentId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Loai du lieu: image | text | audio | video | pair — frontend mo workspace tuong ung.</summary>
        public required string Modality { get; init; }

        /// <summary>Link xem / nghe file co han vai phut (S-07). null voi text / pair.</summary>
        public string? FileUrl { get; init; }

        /// <summary>Noi dung text / pair: {"text"} hoac {"prompt"?,"a","b"}. null voi du lieu file.</summary>
        public RawJson? Content { get; init; }

        /// <summary>
        /// Metadata mau: width/height (anh, video), durationSec, segmentStart/segmentEnd
        /// (doan cat tu file dai — player chi phat doan nay; thoi gian trong nhan
        /// tinh tu DAU DOAN).
        /// </summary>
        public required RawJson Metadata { get; init; }

        /// <summary>
        /// Tap nhan cua du an: danh sach cong cu (ten, loai, lop...). "payload" nop
        /// len la object co khoa = ten cong cu, vd {"label":{"labelIds":["do"]}}.
        /// </summary>
        public required RawJson LabelSchema { get; init; }

        public required int SchemaVersion { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>Thu lao neu nhan duoc duyet, so nguyen dong.</summary>
        public required long UnitPriceVnd { get; init; }
    }

    public sealed class SubmitRequest
    {
        /// <summary>
        /// Phan DU LIEU cua nhan, khoa = ten cong cu trong tap nhan, vd
        /// {"label":{"labelIds":["do"]}} hoac {"vat":[{"labelId":"xe","x":1,"y":2,"w":30,"h":20}]}.
        /// Loai du lieu khong can gui — lay theo du an.
        /// </summary>
        public JsonElement? Payload { get; init; }
    }

    public sealed class SubmitResponse
    {
        public required Guid AssignmentId { get; init; }

        /// <summary>true = luot nop nay lam task du so nguoi gan.</summary>
        public required bool TaskCompleted { get; init; }
    }

    public sealed class ProgressResponse
    {
        public required Guid ProjectId { get; init; }

        public required int TotalTasks { get; init; }

        public required int OpenTasks { get; init; }

        public required int CompletedTasks { get; init; }

        /// <summary>Mau la cau hoi vang — khong tinh vao viec phai lam.</summary>
        public required int ExcludedTasks { get; init; }

        public required int ActiveLeases { get; init; }

        public required int Submissions { get; init; }

        /// <summary>Tong so luot can = so task (tru vang) x redundancy.</summary>
        public required int RequiredSubmissions { get; init; }
    }

    /// <summary>
    /// GET /internal/projects/{id}/close-check — project-svc hoi truoc khi dong du an.
    /// </summary>
    public sealed class CloseCheckResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>task-svc da nhan project.paused (khong cap task moi nua).</summary>
        public required bool Paused { get; init; }

        /// <summary>Luot dang giu con han — labeler con nop duoc.</summary>
        public required int ActiveLeases { get; init; }

        /// <summary>Luot da nop cua task THAT (khong tinh cau vang) — phai bang so nhan ben annotation-svc.</summary>
        public required int SubmittedCount { get; init; }
    }
}
