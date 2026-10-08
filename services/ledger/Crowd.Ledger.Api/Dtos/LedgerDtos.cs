using System;
using System.Collections.Generic;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;

namespace Crowd.Ledger.Api.Dtos
{
    /// <summary>
    /// Vi cua nguoi dung (FB-04, FL-10). Mot tai khoan co the vua la doanh nghiep
    /// vua la labeler nen tra ca hai nhom so.
    /// </summary>
    public sealed class BalanceResponse
    {
        // ---- Doanh nghiep ----

        public required long BusinessAvailableVnd { get; init; }

        /// <summary>Tong ky quy dang giu cua cac du an cua ban.</summary>
        public required long EscrowVnd { get; init; }

        // ---- Labeler ----

        /// <summary>Da duyet nhung con treo (cho doi soat).</summary>
        public required long PendingVnd { get; init; }

        /// <summary>Rut duoc ngay.</summary>
        public required long AvailableVnd { get; init; }
    }

    /// <summary>Mot dong lich su giao dich (FB-05, FL-11).</summary>
    public sealed class TransactionResponse
    {
        public required long Seq { get; init; }

        public required JournalEntryType Type { get; init; }

        public required string Reference { get; init; }

        public required string Description { get; init; }

        public required string AccountCode { get; init; }

        /// <summary>Duong = tien vao, am = tien ra.</summary>
        public required long AmountVnd { get; init; }

        public required DateTimeOffset At { get; init; }
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    public sealed class WithdrawRequest
    {
        public long AmountVnd { get; init; }

        public string? BankAccount { get; init; }
    }

    public sealed class WithdrawalResponse
    {
        public required Guid Id { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>Thue TNCN khau tru (10% neu tu 2.000.000d).</summary>
        public required long TaxVnd { get; init; }

        public required long NetVnd { get; init; }

        public required WithdrawalState State { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>Ly do that bai (cong) hoac ly do admin tu choi.</summary>
        public string? FailureReason { get; init; }

        /// <summary>Luc duyet / tu choi (null = con cho duyet).</summary>
        public DateTimeOffset? ReviewedAt { get; init; }
    }

    /// <summary>Lenh rut trong hang doi duyet cua admin.</summary>
    public sealed class AdminWithdrawalResponse
    {
        public required Guid Id { get; init; }

        public required Guid LabelerId { get; init; }

        public required long AmountVnd { get; init; }

        public required long TaxVnd { get; init; }

        public required long NetVnd { get; init; }

        public required string BankAccount { get; init; }

        public required WithdrawalState State { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public Guid? ReviewedBy { get; init; }

        public DateTimeOffset? ReviewedAt { get; init; }

        public string? FailureReason { get; init; }
    }

    public sealed class RejectWithdrawalRequest
    {
        public string? Reason { get; init; }
    }

    /// <summary>Ket qua doi soat (FM-05). Rong o moi danh sach = so cai lanh.</summary>
    public sealed class ReconciliationResponse
    {
        public required bool Healthy { get; init; }

        public required int EntryCount { get; init; }

        /// <summary>Tong MOI dong but toan cua ca he thong — phai bang 0.</summary>
        public required long GrandTotal { get; init; }

        /// <summary>But toan co tong khac 0 (khong the xay ra neu domain dung).</summary>
        public required IReadOnlyList<string> UnbalancedEntries { get; init; }

        /// <summary>Tai khoan co balance khac SUM(dong but toan).</summary>
        public required IReadOnlyList<string> BalanceMismatches { get; init; }

        /// <summary>Tai khoan (khong phai cong) bi am.</summary>
        public required IReadOnlyList<string> NegativeAccounts { get; init; }

        /// <summary>Seq cua but toan co hash sai / dut chuoi — dau hieu bi sua tay.</summary>
        public required IReadOnlyList<long> BrokenChain { get; init; }
    }
}
