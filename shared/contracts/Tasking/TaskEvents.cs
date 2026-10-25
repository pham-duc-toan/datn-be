using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Tasking
{
    /// <summary>
    /// Mot cau tra loi trong bai nop (chi cau THAT — cau vang da duoc task-svc cham va chi mang
    /// so dung / tong). Kem du thong tin mau de annotation-svc luu va xuat ma khong phai hoi ai.
    /// </summary>
    public sealed record AttemptAnswer
    {
        public required Guid SampleId { get; init; }

        /// <summary>Khoa file trong MinIO. null voi text / pair / list.</summary>
        public string? StorageKey { get; init; }

        /// <summary>Noi dung mau khi khong phai file (text, pair, list).</summary>
        public RawJson? SampleContent { get; init; }

        /// <summary>Thong tin mau — xuat COCO can kich thuoc anh, doan cat can moc thoi gian.</summary>
        public required RawJson SampleMetadata { get; init; }

        /// <summary>Cau tra loi da kiem dinh dang. null = labeler bo trong cau nay (tinh la khong giong da so).</summary>
        public LabelPayload? LabelPayload { get; init; }
    }

    /// <summary>
    /// Labeler NOP CA BAI (docs 3.18): moi cau that cua de + ket qua cau vang. annotation-svc luu
    /// nhan (chua co gia tri tien), quality-svc giu de cham khi du an ket thuc.
    ///
    /// Vi sao nop o task-svc: kiem "bai con hieu luc, chua nop" va "danh dau da nop" phai NGUYEN TU
    /// tren CUNG mot dong (VD-T-01) — bai lam la du lieu cua task-svc.
    ///
    /// Namespace la Crowd.Contracts.Tasking chu khong phai ".Task": chu "Task" se
    /// che System.Threading.Tasks.Task — cung ly do voi assembly Crowd.Tasking.
    /// </summary>
    public sealed record AttemptSubmitted : IEventPayload
    {
        public static string EventType
        {
            get { return "attempt.submitted"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency cua moi consumer: moi bai nop xu ly DUNG MOT lan.</summary>
        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required IReadOnlyList<AttemptAnswer> Answers { get; init; }

        /// <summary>So cau vang trong de cua labeler nay.</summary>
        public required int GoldTotal { get; init; }

        /// <summary>So cau vang tra loi dung (task-svc cham bang Crowd.Labeling, nguong theo setting).</summary>
        public required int GoldCorrect { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset SubmittedAt { get; init; }

        /// <summary>IP cua nguoi nop (gateway chuyen qua X-Forwarded-For). quality-svc phat hien nhieu tai khoan chung IP (FM-07).</summary>
        public string? ClientIp { get; init; }

        /// <summary>Ma thiet bi frontend gui o header X-Device-Id (tuy chon). Dung nhu ClientIp.</summary>
        public string? DeviceId { get; init; }
    }

    /// <summary>Mot bai da nop, liet ke trong project.collection_finished.</summary>
    public sealed record SubmittedAttemptRef
    {
        public required Guid AttemptId { get; init; }

        public required Guid LabelerId { get; init; }

        public required int GoldTotal { get; init; }

        public required int GoldCorrect { get; init; }
    }

    /// <summary>
    /// Du an DONG THU BAI: du suat, het han chot, hoac doanh nghiep ket thuc som. quality-svc cham
    /// khi da nhan du attempt.submitted cua MOI bai liet ke (hai event di hai queue khac nhau, bai
    /// nop co the toi sau). project-svc chuyen du an sang "Dang cham".
    /// </summary>
    public sealed record ProjectCollectionFinished : IEventPayload
    {
        public static string EventType
        {
            get { return "project.collection_finished"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        /// <summary>du_suat | het_han | ket_thuc_som</summary>
        public required string Reason { get; init; }

        public required IReadOnlyList<SubmittedAttemptRef> Attempts { get; init; }

        /// <summary>Mau bi loai giua chung (bao loi, chu du an xac nhan) — khong cham cau nay.</summary>
        public required IReadOnlyList<Guid> ExcludedSampleIds { get; init; }

        public required DateTimeOffset FinishedAt { get; init; }
    }

    /// <summary>
    /// Labeler TRUOT khieu nai ket qua ca bai (trong task.appeal_window). admin-svc dua vao hang
    /// doi duyet chung; notification bao admin.
    /// </summary>
    public sealed record AttemptAppealOpened : IEventPayload
    {
        public static string EventType
        {
            get { return "attempt.appeal_opened"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required string Message { get; init; }

        public required int AgreementPercent { get; init; }

        public required int PassThresholdPercent { get; init; }
    }

    /// <summary>
    /// Admin phan xu khieu nai ca bai. Accepted = doi thanh DAT: ledger tra thuong, annotation-svc
    /// nhan cac nhan cua bai vao ket qua, identity cong cap do.
    /// </summary>
    public sealed record AttemptAppealResolved : IEventPayload
    {
        public static string EventType
        {
            get { return "attempt.appeal_resolved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required bool Accepted { get; init; }

        /// <summary>Accepted VA con suat thuong luc phan xu (so bai duoc thuong &lt; so suat) → ledger tra thuong.</summary>
        public required bool Rewarded { get; init; }

        public string? Note { get; init; }

        /// <summary>So cau that cua bai (identity cong vao so nhan dat khi Accepted).</summary>
        public required int AnsweredCount { get; init; }
    }

    /// <summary>
    /// Het han khieu nai va khong con khieu nai mo: so nguoi dat da CHOT. project-svc chuyen du an
    /// sang Hoan thanh va phat project.completed {PassedAttemptCount} cho ledger hoan ky quy.
    /// </summary>
    public sealed record ProjectSettled : IEventPayload
    {
        public static string EventType
        {
            get { return "project.settled"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required int PassedAttemptCount { get; init; }
    }
}
