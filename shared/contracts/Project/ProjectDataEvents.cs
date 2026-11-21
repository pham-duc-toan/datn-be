using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Project
{
    /// <summary>
    /// Cac mau VIDEO vua xu ly xong (ready): mot file video + cac doan cat cua no. media-svc cat san HLS cho tung
    /// doan cua mau (phan giua in chu WatermarkText, doan dau + cuoi de sach cho chu rieng tung nguoi xem). Chi video
    /// moi can nghe nen project-svc chi phat cho video; anh / am thanh / van ban khong ai nghe. gate-svc KHONG nghe:
    /// no doc mau qua view v_gate_samples (kien truc 3.32) sau khi du an publish bat cong link.
    ///
    /// Moi event la MOT file (kem cac doan cat) — worker xu ly tung file mot nen message luon nho.
    /// v2: them WatermarkText.
    /// </summary>
    public sealed record SamplesIngested : IEventPayload
    {
        public static string EventType
        {
            get { return "samples.ingested"; }
        }

        public static int Version
        {
            get { return 2; }
        }

        public required Guid ProjectId { get; init; }

        public required IReadOnlyList<IngestedSample> Samples { get; init; }

        /// <summary>
        /// Chu watermark noi in vao phan giua video (dung chung moi nguoi xem): "nguồn: &lt;doanh nghiep&gt; - &lt;domain&gt;"
        /// (Crowd.BuildingBlocks.Storage.ChuWatermark). media-svc ve nguyen chu nay, khong tu ghep.
        /// </summary>
        public required string WatermarkText { get; init; }
    }

    /// <summary>
    /// project-svc → media-svc (kien truc 3.40): tao SAN ban xem truoc DUNG CHUNG cua the cho du an luc du an bat dau chay
    /// (duyet / tu duyet) va luc doanh nghiep bat lai xem truoc. Mau = mau he thong tu lay (mau that nap som nhat, khong
    /// phai cau vang). Anh: thu nho canh dai 1080 px; video / am thanh: clip 15 giay dau doan mau. Chu in vao = chu DUNG
    /// CHUNG (khong co nguoi xem) — frontend ve them chu cua nguoi xem de len. Chi gui voi mau co file (anh / video / am thanh).
    /// </summary>
    public sealed record ProjectPreviewRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "project.preview_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required string Modality { get; init; }

        public required string StorageKey { get; init; }

        /// <summary>Video / am thanh cat doan: mili-giay bat dau cua mau trong file (null = 0).</summary>
        public long? SegmentStartMs { get; init; }

        /// <summary>Mili-giay ket thuc cua mau trong file (null = het file).</summary>
        public long? SegmentEndMs { get; init; }

        /// <summary>"nguồn: &lt;doanh nghiep&gt; - &lt;domain&gt;" (ChuWatermark.DungChung); null = admin tat watermark.</summary>
        public string? WatermarkText { get; init; }
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
