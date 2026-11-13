using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Project
{
    /// <summary>
    /// Cac mau VIDEO vua xu ly xong (ready): mot file video + cac doan cat cua no. media-svc chuan hoa san ban
    /// chuan (watermark chi nhung khung dau + cuoi). Chi video moi can nghe nen project-svc chi phat cho video;
    /// anh / am thanh / van ban khong ai nghe. gate-svc KHONG nghe: no doc mau qua view v_gate_samples (kien truc 3.32) sau khi du an
    /// publish bat cong link.
    ///
    /// Moi event la MOT file (kem cac doan cat) — worker xu ly tung file mot nen message luon nho.
    /// </summary>
    public sealed record SamplesIngested : IEventPayload
    {
        public static string EventType
        {
            get { return "samples.ingested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required IReadOnlyList<IngestedSample> Samples { get; init; }
    }

    /// <summary>
    /// Mot mau trong event. Mang du thong tin de service nghe phat viec ma khong phai
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

        /// <summary>Noi dung khi du lieu khong phai file: {"text"}, {"a","b"} hoac {"items"} (cau hoi khong nam trong content). null voi file.</summary>
        public RawJson? Content { get; init; }

        /// <summary>Thong tin mau (Crowd.Labeling.SampleMetadata): width/height, durationSec, length, doan cat.</summary>
        public required RawJson Metadata { get; init; }
    }
}
