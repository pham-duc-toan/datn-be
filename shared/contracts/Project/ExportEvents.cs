using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Project
{
    /// <summary>
    /// Doanh nghiep bam "tao file ket qua" (kien truc 3.41): project-svc ghi dong export_jobs "queued" va event nay
    /// CUNG transaction (outbox), roi tra 202 ngay. Consumer project-svc.export-requested (chinh project-svc) gop ket qua,
    /// dung file theo dinh dang, luu MinIO roi chot job ready | failed. Thoi gian tao file khong con nam trong request.
    ///
    /// Event mang du thong tin de lam viec ma khong can doc lai request: job nao, du an nao, dinh dang nao.
    /// </summary>
    public sealed record ExportRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "export.requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid JobId { get; init; }

        public required Guid ProjectId { get; init; }

        /// <summary>json | csv | coco | yolo | voc | yolo-seg | yolo-obb | coco-keypoints | conll | mot (da chuan hoa chu thuong).</summary>
        public required string Format { get; init; }
    }
}
