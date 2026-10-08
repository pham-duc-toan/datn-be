using System;
using System.Collections.Generic;
using Crowd.Payment.Domain.Deposits;

namespace Crowd.Payment.Api.Dtos
{
    public sealed class CreateDepositRequest
    {
        public long AmountVnd { get; init; }
    }

    public sealed class DepositResponse
    {
        public required Guid IntentId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>"sandbox" (cong) hoac "manual_transfer" (chuyen khoan thu cong).</summary>
        public required string Provider { get; init; }

        public required PaymentIntentStatus Status { get; init; }

        /// <summary>Cong thanh toan: mo link nay de tra tien. Chuyen khoan thu cong: null.</summary>
        public string? CheckoutUrl { get; init; }

        /// <summary>Chuyen khoan thu cong: noi dung BAT BUOC ghi khi chuyen khoan.</summary>
        public string? TransferCode { get; init; }

        /// <summary>Chuyen khoan thu cong: thong tin tai khoan nhan (setting payment.manual_transfer_bank_info).</summary>
        public string? BankInfo { get; init; }

        public DateTimeOffset? TransferredAt { get; init; }

        public string? RejectReason { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }
    }

    public sealed class ApproveDepositRequest
    {
        /// <summary>Ma giao dich tren sao ke ngan hang (tuy chon, khuyen nghi ghi de doi soat).</summary>
        public string? BankTxnRef { get; init; }
    }

    public sealed class RejectDepositRequest
    {
        public string? Reason { get; init; }
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    /// <summary>
    /// Than webhook cua cong sandbox. Chi TIN sau khi kiem chu ky tren CHINH chuoi
    /// byte goc cua body — khong kiem tren object da parse (thu tu truong, khoang
    /// trang khac nhau la chu ky lech).
    /// </summary>
    public sealed class SandboxWebhookBody
    {
        public Guid IntentId { get; init; }

        public long AmountVnd { get; init; }

        /// <summary>"success" | "failed"</summary>
        public string? Status { get; init; }

        public string? ProviderTxnId { get; init; }
    }
}
