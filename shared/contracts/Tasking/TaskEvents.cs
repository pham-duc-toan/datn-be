using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Tasking
{
    /// <summary>
    /// Labeler NOP CA BAI (docs 3.18). project-svc da luu nhan cung transaction (chua co gia tri tien).
    ///
    /// v2 (kien truc 3.22): KHONG con mang cau tra loi + thong tin mau. quality-svc doc de bai, bai nop va
    /// nhan thang tu project-svc qua gRPC (contracts/grpc/project/v1/quality_data.proto) luc can — event chi
    /// la TIN HIEU "co bai moi" (xet gian lan, danh dau du an can tinh lai DS, xep hang nhung cau tra loi mo).
    ///
    /// Vi sao nop o project-svc: kiem "bai con hieu luc, chua nop" va "danh dau da nop" phai NGUYEN TU
    /// tren CUNG mot dong (VD-T-01) — bai lam la du lieu cua project-svc (truoc la task-svc).
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
            get { return 2; }
        }

        /// <summary>Khoa idempotency cua moi consumer: moi bai nop xu ly DUNG MOT lan.</summary>
        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>So cau THAT da tra loi (= so nhan cua bai). Chong gian lan chi xet bai du dai.</summary>
        public required int AnsweredCount { get; init; }

        /// <summary>So cau vang trong de cua labeler nay.</summary>
        public required int GoldTotal { get; init; }

        /// <summary>So cau vang tra loi dung (project-svc cham bang Crowd.Labeling, nguong theo setting).</summary>
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
    /// Du an DONG THU BAI: du suat, het han chot, hoac doanh nghiep ket thuc som. project-svc chuyen du an
    /// sang "Dang cham" cung transaction; quality-svc cham ngay (doc bai + nhan qua gRPC — moi bai trong danh
    /// sach da commit truoc event nay, khong con phai doi attempt.submitted).
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
    /// Admin phan xu khieu nai ca bai. Accepted = doi thanh DAT: ledger tra thuong, project-svc
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
}
