using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Project
{
    /// <summary>
    /// SAGA BUOC 1 (docs 3.4): doanh nghiep bam publish, project-svc xin ledger
    /// giu tien ky quy. Ledger tra loi bang escrow.reserved hoac escrow.rejected.
    /// </summary>
    public sealed record ProjectPublishRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "project.publish_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency cua ledger: moi du an ky quy DUNG MOT lan.</summary>
        public required Guid ProjectId { get; init; }

        /// <summary>Tai khoan doanh nghiep bi tru tien.</summary>
        public required Guid OwnerId { get; init; }

        /// <summary>So tien can giu, so nguyen dong = ngan sach doanh nghiep dat.</summary>
        public required long EscrowAmountVnd { get; init; }
    }

    /// <summary>
    /// Du an bat dau chay. task-svc sinh task tu cac mau da nhan qua
    /// dataset.ingested; notification bao cho labeler phu hop.
    ///
    /// Mang du cau hinh ma service khac can de lam viec MA KHONG PHAI goi HTTP
    /// nguoc lai project-svc (luat 1, muc 4 docs).
    /// </summary>
    public sealed record ProjectPublished : IEventPayload
    {
        public static string EventType
        {
            get { return "project.published"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>Loai du lieu: image | text | audio | video | pair (Crowd.Labeling.Modalities).</summary>
        public required string Modality { get; init; }

        /// <summary>
        /// TAP NHAN o dang chuan (Crowd.Labeling.LabelSchema): loai du lieu + danh
        /// sach cong cu. task-svc va annotation-svc chep nguyen khoi nay — kiem nhan,
        /// cham cau vang, gop ket qua deu theo no, khong phai hoi lai project-svc.
        /// </summary>
        public required RawJson LabelSchema { get; init; }

        /// <summary>Thu lao moi nhan, so nguyen dong.</summary>
        public required long UnitPriceVnd { get; init; }

        /// <summary>
        /// Phi nen tang moi nhan da CHOT luc publish, so nguyen dong (VD-M-15).
        /// annotation-svc mang con so nay vao annotation.approved de ledger chia:
        /// escrow −(don gia + phi) → labeler +don gia, platform +phi.
        /// </summary>
        public required long PlatformFeeVnd { get; init; }

        /// <summary>So nguoi gan cung mot mau.</summary>
        public required int Redundancy { get; init; }

        /// <summary>
        /// TRAN redundancy thich ung (>= Redundancy): mau tranh chap duoc quality-svc
        /// xin them nguoi gan toi toi da so nay. Ky quy tinh theo tran nen tien luon
        /// du; ledger chan chi qua tran moi task (VD-M-03).
        /// </summary>
        public required int MaxRedundancy { get; init; }

        /// <summary>
        /// Phan tram so lan cap task la CAU VANG KIEM TRA (qualityCheck) tron vao,
        /// 0-50. Labeler khong phan biet duoc; cau vang khong tra tien.
        /// </summary>
        public required int GoldCheckPercent { get; init; }

        public required DateTimeOffset Deadline { get; init; }

        public required bool AllowProfessional { get; init; }

        public required bool AllowLinkGateway { get; init; }

        public required bool AllowCollaborative { get; init; }

        /// <summary>true = chi thanh vien duoc moi moi thay du an (FP-05).</summary>
        public required bool IsPrivate { get; init; }

        /// <summary>null = khong gioi han. task-svc loc theo labeler_cache.</summary>
        public int? MinLevel { get; init; }

        /// <summary>Thang 0-100. null = khong gioi han.</summary>
        public int? MinReputation { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required int SampleCount { get; init; }
    }

    public sealed record ProjectPaused : IEventPayload
    {
        public static string EventType
        {
            get { return "project.paused"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }
    }

    public sealed record ProjectResumed : IEventPayload
    {
        public static string EventType
        {
            get { return "project.resumed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }
    }

    /// <summary>
    /// Du an bi huy. task-svc dung cap task; ledger hoan phan ky quy chua dung.
    /// </summary>
    public sealed record ProjectCancelled : IEventPayload
    {
        public static string EventType
        {
            get { return "project.cancelled"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>
        /// true = da tung ky quy (can hoan tien). false = huy luc con Nhap,
        /// ledger khong phai lam gi.
        /// </summary>
        public required bool WasEscrowed { get; init; }

        public string? Reason { get; init; }

        /// <summary>
        /// So nhan (labeler chuyen nghiep) DA DUYET luc dong so (annotation-svc tra ve khi dong).
        /// Ledger chi hoan ky quy khi da chi DU so nhan nay: event annotation.approved va event nay
        /// di hai queue khac nhau, approved co the toi SAU (NC-B-01, TLA+ tim ra). 0 = khong co nhan
        /// nao; null = event cu truoc thay doi nay (hoan ngay nhu truoc).
        /// </summary>
        public int? ApprovedAnnotationCount { get; init; }
    }

    /// <summary>Du an ket thuc binh thuong. Ledger giai phong phan escrow con du.</summary>
    public sealed record ProjectCompleted : IEventPayload
    {
        public static string EventType
        {
            get { return "project.completed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>
        /// So nhan (labeler chuyen nghiep) DA DUYET luc dong so (annotation-svc tra ve khi dong).
        /// Ledger chi hoan ky quy khi da chi DU so nhan nay: event annotation.approved va event nay
        /// di hai queue khac nhau, approved co the toi SAU (NC-B-01, TLA+ tim ra). 0 = khong co nhan
        /// nao; null = event cu truoc thay doi nay (hoan ngay nhu truoc).
        /// </summary>
        public int? ApprovedAnnotationCount { get; init; }
    }
}
