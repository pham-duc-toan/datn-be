using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Quality
{
    // Event quality-svc (Python) phat. Phia Python dinh nghia lai DUNG cac truong nay
    // (services/quality/app/contracts.py) — doi o day thi phai doi o do, va
    // contracts/payloads.snapshot.txt se bao.

    /// <summary>Ket qua cham mot bai (docs 3.18).</summary>
    public sealed record GradedAttempt
    {
        public required Guid AttemptId { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>So cau SO DUOC: co dap an da so, khong phai cau vang / cau tra loi mo.</summary>
        public required int ComparableCount { get; init; }

        /// <summary>So cau giong dap an da so (moi cong cu so duoc cua cau deu khop).</summary>
        public required int MatchedCount { get; init; }

        /// <summary>floor(100 x Matched / Comparable); Comparable = 0 → 100 (khong co gi de so).</summary>
        public required int AgreementPercent { get; init; }

        public required int GoldTotal { get; init; }

        public required int GoldCorrect { get; init; }

        /// <summary>AgreementPercent &gt;= nguong VA GoldCorrect == GoldTotal.</summary>
        public required bool Passed { get; init; }

        /// <summary>So cau that labeler da tra loi (identity cong vao so nhan dat).</summary>
        public required int AnsweredCount { get; init; }
    }

    /// <summary>
    /// Du an DA CHAM (sau project.collection_finished, khi du attempt.submitted cua moi bai):
    /// moi cau lay dap an da so cua cac bai da nop, moi bai co ti le cau giong da so. ledger tra
    /// thuong cho bai dat; task-svc mo han khieu nai; annotation-svc chot nhan vao ket qua;
    /// identity cong cap do; notification bao tung labeler.
    ///
    /// Duoi 3 bai nop thi khong co "da so": chi xet cau vang (MajorityUsed = false).
    /// </summary>
    public sealed record ProjectGraded : IEventPayload
    {
        public static string EventType
        {
            get { return "project.graded"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required int PassThresholdPercent { get; init; }

        public required bool MajorityUsed { get; init; }

        public required IReadOnlyList<GradedAttempt> Attempts { get; init; }

        public required DateTimeOffset GradedAt { get; init; }
    }
}
