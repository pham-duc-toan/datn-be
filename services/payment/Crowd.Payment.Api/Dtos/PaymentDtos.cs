using System;
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

        public required long AmountVnd { get; init; }

        public required PaymentIntentStatus Status { get; init; }

        /// <summary>Mo link nay de thanh toan tren trang cua cong.</summary>
        public required string CheckoutUrl { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
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
