using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Payment
{
    /// <summary>
    /// Cong thanh toan XAC NHAN doanh nghiep da tra tien (webhook da kiem chu ky).
    /// ledger ghi so: gateway → business:available.
    /// </summary>
    public sealed record DepositConfirmed : IEventPayload
    {
        public static string EventType
        {
            get { return "deposit.confirmed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency cua ledger: mot lan nap ghi so DUNG MOT lan.</summary>
        public required Guid IntentId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>"sandbox" | "vnpay" | "momo"</summary>
        public required string Provider { get; init; }

        public required string ProviderTxnId { get; init; }
    }

    /// <summary>Tien da ROI he thong toi tai khoan ngan hang cua labeler.</summary>
    public sealed record PayoutCompleted : IEventPayload
    {
        public static string EventType
        {
            get { return "payout.completed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid WithdrawalId { get; init; }

        public required string Provider { get; init; }

        public required string ProviderTxnId { get; init; }
    }

    /// <summary>Cong tu choi chuyen khoan: ledger hoan TOAN BO ve so du kha dung.</summary>
    public sealed record PayoutFailed : IEventPayload
    {
        public static string EventType
        {
            get { return "payout.failed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid WithdrawalId { get; init; }

        public required string Reason { get; init; }
    }
}
