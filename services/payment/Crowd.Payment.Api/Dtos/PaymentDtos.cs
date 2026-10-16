using System;
using System.Collections.Generic;
using Crowd.Payment.Domain.Deposits;

namespace Crowd.Payment.Api.Dtos
{
    public sealed class CreateDepositRequest
    {
        public long AmountVnd { get; init; }

        /// <summary>sandbox | vnpay | momo (bo trong = sandbox). Chuyen khoan thu cong di /deposits/manual.</summary>
        public string? Provider { get; init; }
    }

    public sealed class PaymentProvidersResponse
    {
        /// <summary>Cong nap dang mo (co cau hinh + bat trong setting), kem manual_transfer neu bat.</summary>
        public required IReadOnlyList<string> Providers { get; init; }
    }

    public sealed class DepositResponse
    {
        public required Guid IntentId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>sandbox | vnpay | momo (cong) hoac manual_transfer (chuyen khoan thu cong).</summary>
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

        /// <summary>That bai vi sao: cong_{ma cua cong} | cong_loi (tao lenh loi) | het_han.</summary>
        public string? FailureCode { get; init; }

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
}
