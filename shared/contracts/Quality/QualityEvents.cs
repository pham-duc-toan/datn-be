using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Quality
{
    // Event qua lai giua task-svc va quality-svc (Python). Phia Python dinh nghia
    // lai DUNG cac truong nay (services/quality/app/contracts.py) — doi o day thi
    // phai doi o do, va contracts/payloads.snapshot.txt se bao.

    /// <summary>
    /// task-svc → quality-svc: labeler vua tra loi mot CAU VANG KIEM TRA (qualityCheck)
    /// tron trong luong task. task-svc tu cham bang Crowd.Labeling (cung luat voi bai
    /// test dau vao) — quality chi cong don do chinh xac, khong lap lai luat cham.
    /// </summary>
    public sealed record GoldAnswered : IEventPayload
    {
        public static string EventType
        {
            get { return "gold.answered"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency phia quality: moi luot tra loi tinh MOT lan.</summary>
        public required Guid AssignmentId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>true = khop dap an o MOI cong cu cua tap nhan.</summary>
        public required bool Correct { get; init; }

        public required DateTimeOffset AnsweredAt { get; init; }
    }

    /// <summary>
    /// quality-svc → task-svc: mau tranh chap, xin them nguoi gan (VONG LAP THICH UNG,
    /// docs 3.7). task-svc la chu so redundancy: chi nang khi NewRedundancy lon hon so
    /// hien tai va khong vuot tran cua du an.
    /// </summary>
    public sealed record RedundancyIncreaseRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "redundancy.increase_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>So nguoi gan MOI (tuyet doi, khong phai do chenh) — giao lai hai lan van dung.</summary>
        public required int NewRedundancy { get; init; }

        public required string Reason { get; init; }
    }

    /// <summary>task-svc: redundancy cua mot task vua doi — vet kiem toan cho FQ-03.</summary>
    public sealed record TaskRedundancyChanged : IEventPayload
    {
        public static string EventType
        {
            get { return "task.redundancy_changed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required int OldRedundancy { get; init; }

        public required int NewRedundancy { get; init; }
    }

    public enum ConsensusStatus
    {
        /// <summary>Moi cong cu gop duoc deu co lua chon qua ban.</summary>
        Agreed,

        /// <summary>Co cong cu khong lua chon nao qua ban, va da het tran redundancy.</summary>
        Disputed,

        /// <summary>Tap nhan khong co cong cu nao gop tu dong duoc (khung, chep loi...).</summary>
        NotApplicable,
    }

    /// <summary>Mot nhan co khop ket qua dong thuan khong.</summary>
    public sealed record ConsensusVote
    {
        public required Guid AnnotationId { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>null khi trang thai NotApplicable.</summary>
        public bool? Agrees { get; init; }
    }

    /// <summary>
    /// quality-svc → annotation-svc: ket qua dong thuan cua MOT task sau khi du nguoi
    /// gan. Chi la GOI Y cho nguoi duyet (khong tu duyet, khong tu chi tien).
    /// Mot task co the nhan nhieu lan neu redundancy tang — ban moi hon (occurredAt)
    /// ghi de ban cu.
    /// </summary>
    public sealed record ConsensusReached : IEventPayload
    {
        public static string EventType
        {
            get { return "consensus.reached"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid TaskId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required ConsensusStatus Status { get; init; }

        /// <summary>
        /// Ket qua chot theo tung cong cu gop duoc, vd {"loai":{"labelIds":["xe"]}}.
        /// Cong cu tranh chap mang gia tri null. null ca khoi khi NotApplicable.
        /// </summary>
        public RawJson? Final { get; init; }

        public required IReadOnlyList<ConsensusVote> Votes { get; init; }
    }
}
