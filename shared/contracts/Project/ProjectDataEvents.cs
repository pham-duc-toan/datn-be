using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Project
{
    /// <summary>
    /// Mot LO mau vua nap vao du an. task-svc giu ban sao danh sach mau de sinh
    /// task khi du an publish; ml-svc bat dau pre-label.
    ///
    /// Chia lo vi mot dataset co the vai chuc nghin mau — nhet het vao mot
    /// message thi message nang vai MB, vuot xa kich thuoc hop ly cua bus.
    /// Consumer biet da nhan du khi dem du BatchCount lo cua cung DatasetId.
    /// </summary>
    public sealed record DatasetIngested : IEventPayload
    {
        public static string EventType
        {
            get { return "dataset.ingested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid DatasetId { get; init; }

        /// <summary>Bat dau tu 0.</summary>
        public required int BatchIndex { get; init; }

        public required int BatchCount { get; init; }

        public required IReadOnlyList<IngestedSample> Samples { get; init; }
    }

    /// <summary>
    /// Mot mau trong lo. Mang du thong tin de task-svc phat viec ma khong phai
    /// hoi lai project-svc: file (khoa MinIO) hoac noi dung (text), va metadata
    /// de kiem nhan (kich thuoc anh, thoi luong, do dai van ban).
    /// </summary>
    public sealed record IngestedSample
    {
        public required Guid SampleId { get; init; }

        /// <summary>image | text | audio | video | pair.</summary>
        public required string Modality { get; init; }

        /// <summary>Khoa trong bucket "datasets" cua MinIO. null voi du lieu khong phai file (text, pair).</summary>
        public string? StorageKey { get; init; }

        /// <summary>Noi dung khi du lieu khong phai file: {"text"} hoac {"prompt","a","b"}. null voi file.</summary>
        public RawJson? Content { get; init; }

        /// <summary>Thong tin mau (Crowd.Labeling.SampleMetadata): width/height, durationSec, length, doan cat.</summary>
        public required RawJson Metadata { get; init; }
    }

    /// <summary>
    /// Toan bo tap cau hoi vang cua du an SAU khi thay doi (thay the, khong
    /// phai cong don). task-svc tron cau hoi vang vao task; gate-svc dung lam
    /// thu thach o cong link.
    /// </summary>
    public sealed record GoldSetUpdated : IEventPayload
    {
        public static string EventType
        {
            get { return "gold_set.updated"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required IReadOnlyList<GoldSetItem> Items { get; init; }
    }

    public sealed record GoldSetItem
    {
        public required Guid SampleId { get; init; }

        /// <summary>Dap an dung, cung dinh dang voi nhan labeler nop — xem Crowd.Labeling.</summary>
        public required LabelPayload ExpectedPayload { get; init; }

        public required GoldPurpose Purpose { get; init; }
    }
}
