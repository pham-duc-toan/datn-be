using System;
using System.Collections.Generic;

namespace Crowd.Ledger.Api.Dtos
{
    // ---------------- thu nhap (FL-11) ----------------

    public sealed class EarningsResponse
    {
        public required DateOnly From { get; init; }

        public required DateOnly To { get; init; }

        /// <summary>day | project</summary>
        public required string GroupBy { get; init; }

        public required long TotalVnd { get; init; }

        /// <summary>Theo nguon: projectReward (thuong bai dat), gateClick (luot vuot link), referral (hoa hong); annotation = du lieu cu.</summary>
        public required IReadOnlyDictionary<string, long> ByKindVnd { get; init; }

        public required IReadOnlyList<EarningsItem> Items { get; init; }
    }

    public sealed class EarningsItem
    {
        /// <summary>Ngay (yyyy-MM-dd, gio Viet Nam) hoac projectId — theo groupBy.</summary>
        public required string Key { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>So khoan thu (moi bai dat / moi lo vuot link / moi hoa hong mot khoan).</summary>
        public required int Count { get; init; }
    }

    // ---------------- chi phi du an (FB-20) ----------------

    public sealed class ProjectCostResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>active | closing | closed</summary>
        public required string EscrowState { get; init; }

        /// <summary>Da ky quy luc publish.</summary>
        public required long ReservedVnd { get; init; }

        /// <summary>Con lai trong ky quy.</summary>
        public required long BalanceVnd { get; init; }

        /// <summary>Da tra thuong cho bai dat (thuong labeler + phi nen tang).</summary>
        public required long SpentRewardsVnd { get; init; }

        /// <summary>Da chi cho cong vuot link (nguoi chia se + nen tang).</summary>
        public required long SpentGateVnd { get; init; }

        /// <summary>Ky quy con du da tra ve vi doanh nghiep khi ket thuc.</summary>
        public required long ReturnedVnd { get; init; }

        /// <summary>So suat (toi da bai dat duoc thuong).</summary>
        public required int Slots { get; init; }

        public required long RewardPerAttemptVnd { get; init; }

        public required long PlatformFeePerAttemptVnd { get; init; }

        /// <summary>So bai dat da tra thuong.</summary>
        public required int PaidAttempts { get; init; }

        /// <summary>Thuong dang treo (ledger.reward_hold &gt; 0) — chua rut duoc.</summary>
        public required long HeldVnd { get; init; }
    }
}
