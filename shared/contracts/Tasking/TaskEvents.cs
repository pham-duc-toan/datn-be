using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Tasking
{
    /// <summary>
    /// Labeler nop nhan cho mot luot lease hop le. annotation-svc nhan de LUU
    /// nhan (phien ban, duyet, khieu nai) roi phat annotation.submitted.
    ///
    /// Vi sao nop o task-svc chu khong phai thang annotation-svc: kiem "lease con
    /// hieu luc" va "danh dau da nop" phai NGUYEN TU tren CUNG mot dong du lieu
    /// (VD-T-01) — ma lease la du lieu cua task-svc. Tach ra hai service thi phai
    /// goi HTTP dong bo trong duong nong, dung dieu CATALOG cam.
    ///
    /// Namespace la Crowd.Contracts.Tasking chu khong phai ".Task": chu "Task" se
    /// che System.Threading.Tasks.Task — cung ly do voi assembly Crowd.Tasking.
    /// </summary>
    public sealed record AssignmentSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "assignment.submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency cua annotation-svc: moi luot nop thanh DUNG MOT nhan.</summary>
        public required Guid AssignmentId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Khoa file trong MinIO — annotation-svc hien file khi duyet ma khong phai hoi ai. null voi text / pair.</summary>
        public string? StorageKey { get; init; }

        /// <summary>Noi dung mau khi khong phai file (text, pair) — nguoi duyet can doc lai.</summary>
        public RawJson? SampleContent { get; init; }

        /// <summary>Thong tin mau — annotation-svc dung khi xuat (COCO can kich thuoc anh, doan cat can moc thoi gian).</summary>
        public required RawJson SampleMetadata { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>Noi dung nhan o moi dang (phan loai, bounding box...) — xem Crowd.Labeling.</summary>
        public required LabelPayload LabelPayload { get; init; }

        public required DateTimeOffset LeasedAt { get; init; }

        public required DateTimeOffset SubmittedAt { get; init; }
    }

    /// <summary>
    /// Task da du so nguoi gan theo redundancy. quality-svc bat dau tinh dong
    /// thuan (docs 3.7).
    /// </summary>
    public sealed record TaskRedundancyReached : IEventPayload
    {
        public static string EventType
        {
            get { return "task.redundancy_reached"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required int Redundancy { get; init; }
    }

    /// <summary>Mot luot lease vua bat dau — fraud-svc do toc do lam bai (FM-07).</summary>
    public sealed record TaskLeased : IEventPayload
    {
        public static string EventType
        {
            get { return "task.leased"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AssignmentId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
