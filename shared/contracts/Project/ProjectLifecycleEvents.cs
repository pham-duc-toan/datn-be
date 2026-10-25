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

        /// <summary>
        /// So tien can giu, so nguyen dong = so suat x (thuong + phi moi nguoi dat) + ngan sach cong
        /// link (neu bat). Du an lam trung tam: tien di theo CA DU AN, khong theo tung nhan.
        /// </summary>
        public required long EscrowAmountVnd { get; init; }

        /// <summary>
        /// Ten du an luc publish — ledger ghi len dong hoa don VAT (FB-05). KHONG bat buoc: ban tin
        /// phat truoc khi them truong nay khong co, ledger in "Du an {ma}".
        /// </summary>
        public string? ProjectName { get; init; }
    }

    /// <summary>
    /// Du an bat dau chay. Mang du cau hinh ma service khac can de lam viec MA KHONG PHAI goi
    /// HTTP nguoc lai project-svc (luat 1, muc 4 docs).
    ///
    /// v2 — DU AN LAM TRUNG TAM (docs 3.18): moi labeler lam HET cac cau cua du an trong MOT bai,
    /// nop ca bai; du an ket thuc (du suat / het han / ket thuc som) thi cham: ti le cau giong da so
    /// &gt;= PassThresholdPercent VA dung het cau vang → duoc RewardVnd. Khong con don gia moi nhan,
    /// redundancy, duyet tung nhan.
    /// </summary>
    public sealed record ProjectPublished : IEventPayload
    {
        public static string EventType
        {
            get { return "project.published"; }
        }

        public static int Version
        {
            get { return 2; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>Loai du lieu: image | text | audio | video | pair | list (Crowd.Labeling.Modalities).</summary>
        public required string Modality { get; init; }

        /// <summary>
        /// TAP NHAN o dang chuan (Crowd.Labeling.LabelSchema): loai du lieu + danh
        /// sach cong cu. task-svc, annotation-svc, quality-svc chep nguyen khoi nay — kiem nhan,
        /// cham cau vang, gop ket qua deu theo no, khong phai hoi lai project-svc.
        /// </summary>
        public required RawJson LabelSchema { get; init; }

        /// <summary>Tien thuong cho MOT labeler lam dat ca du an, so nguyen dong.</summary>
        public required long RewardVnd { get; init; }

        /// <summary>
        /// Phi nen tang tren MOI nguoi dat, da CHOT luc publish (VD-M-15). Ledger chia khi tra thuong:
        /// ky quy −(thuong + phi) → labeler +thuong, platform +phi.
        /// </summary>
        public required long PlatformFeeVnd { get; init; }

        /// <summary>So suat: toi da bay nhieu labeler lam bai (moi nguoi lam het cac cau).</summary>
        public required int Slots { get; init; }

        /// <summary>t — ti le % cau giong da so toi thieu de dat (admin dat khoang [x, y]).</summary>
        public required int PassThresholdPercent { get; init; }

        /// <summary>So cau that cua de (khong tinh cau vang) luc publish.</summary>
        public required int QuestionCount { get; init; }

        /// <summary>Han chot: toi han thi task-svc dong thu bai (bai dang lam do dang khong duoc tinh).</summary>
        public required DateTimeOffset Deadline { get; init; }

        public required bool AllowProfessional { get; init; }

        public required bool AllowLinkGateway { get; init; }

        public required bool AllowCollaborative { get; init; }

        /// <summary>
        /// Cong vuot link (kenh phu, tinh tien theo LUOT khach gan, KHONG theo ca bai): gia moi nhan
        /// khach gan va phi nen tang moi nhan. null khi khong bat cong link.
        /// </summary>
        public long? GateUnitPriceVnd { get; init; }

        public long? GatePlatformFeeVnd { get; init; }

        /// <summary>Ngan sach rieng cua cong link (nam trong ky quy). null / 0 khi khong bat.</summary>
        public long? GateBudgetVnd { get; init; }

        /// <summary>true = chi thanh vien duoc moi moi thay du an (FP-05).</summary>
        public required bool IsPrivate { get; init; }

        /// <summary>null = khong gioi han. task-svc loc theo labeler_cache.</summary>
        public int? MinLevel { get; init; }

        /// <summary>Thang 0-100. null = khong gioi han.</summary>
        public int? MinReputation { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required int SampleCount { get; init; }

        /// <summary>
        /// Noi dung nhay cam (muc 2.9): frontend lam mo mac dinh. KHONG bat buoc — ban tin khong co =
        /// khong nhay cam.
        /// </summary>
        public bool? SensitiveContent { get; init; }
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
    /// Doanh nghiep bam "Ket thuc som": task-svc dong thu bai NGAY (bai dang lam do dang bi huy,
    /// khong tinh) roi phat project.collection_finished nhu khi du suat / het han.
    /// </summary>
    public sealed record ProjectFinishRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "project.finish_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }
    }

    /// <summary>
    /// Du an bi huy — CHI khi chua co bai nao duoc nop (con bai nop thi phai "ket thuc som" de cham,
    /// khong ai lam khong cong). task-svc huy bai dang lam; ledger hoan toan bo ky quy.
    /// </summary>
    public sealed record ProjectCancelled : IEventPayload
    {
        public static string EventType
        {
            get { return "project.cancelled"; }
        }

        public static int Version
        {
            get { return 2; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>
        /// true = da tung ky quy (can hoan tien). false = huy luc con Nhap,
        /// ledger khong phai lam gi.
        /// </summary>
        public required bool WasEscrowed { get; init; }

        public string? Reason { get; init; }
    }

    /// <summary>
    /// Du an ket thuc: da cham, het han khieu nai, khong con khieu nai mo (task-svc phat
    /// project.settled). Ledger hoan phan ky quy con lai SAU KHI da tra du PassedAttemptCount nguoi:
    /// project.graded / attempt.appeal_resolved va event nay di cac queue khac nhau, tien thuong co
    /// the toi SAU (bai hoc NC-B-01, TLA+).
    /// </summary>
    public sealed record ProjectCompleted : IEventPayload
    {
        public static string EventType
        {
            get { return "project.completed"; }
        }

        public static int Version
        {
            get { return 2; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        /// <summary>So bai DUOC THUONG cuoi cung (sau khieu nai, toi da so suat) — so nguoi ledger phai tra truoc khi hoan. Ten giu nguyen tu ban cu (khi moi bai dat deu duoc thuong).</summary>
        public required int PassedAttemptCount { get; init; }
    }
}
