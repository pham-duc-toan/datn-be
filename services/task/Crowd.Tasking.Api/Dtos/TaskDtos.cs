using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Crowd.Tasking.Api.Dtos
{
    /// <summary>Mot task dang giu — du de frontend dung man hinh gan nhan.</summary>
    public sealed class LeaseResponse
    {
        public required Guid AssignmentId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Link xem anh co han vai phut (S-07).</summary>
        public required string ImageUrl { get; init; }

        /// <summary>
        /// Loai nhan + phien ban dinh dang, vd "imageClassification" v1 — cho
        /// frontend biet mo workspace nao va nop "payload" hinh dang gi.
        /// </summary>
        public required string TaskType { get; init; }

        public required int SchemaVersion { get; init; }

        public required IReadOnlyList<string> LabelClasses { get; init; }

        public required bool AllowMultiple { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>Thu lao neu nhan duoc duyet, so nguyen dong.</summary>
        public required long UnitPriceVnd { get; init; }
    }

    public sealed class SubmitRequest
    {
        /// <summary>
        /// Phan DU LIEU cua nhan theo loai nhan cua du an. Phan loai anh:
        /// {"labelIds":["do"]}. Loai nhan khong can gui — lay theo du an.
        /// </summary>
        public JsonElement? Payload { get; init; }

        /// <summary>Phien ban dinh dang nhan. Bo trong = phien ban moi nhat.</summary>
        public int? SchemaVersion { get; init; }
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
}
