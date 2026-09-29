using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Ledger
{
    // Event ledger-svc phat ra NGOAI escrow.reserved / escrow.rejected (xem
    // EscrowEvents.cs). Tien luon la long, so nguyen dong (VD-M-07).

    /// <summary>Nhan duoc duyet: tien vao so du CHO cua labeler, treo HoldDays ngay.</summary>
    public sealed record FundsHeld : IEventPayload
    {
        public static string EventType
        {
            get { return "funds.held"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid HoldId { get; init; }

        public required Guid AnnotationId { get; init; }

        public required Guid LabelerId { get; init; }

        public required long AmountVnd { get; init; }

        public required DateTimeOffset ReleaseAt { get; init; }
    }

    /// <summary>Het thoi gian treo: tien chuyen sang so du kha dung, rut duoc.</summary>
    public sealed record HoldExpired : IEventPayload
    {
        public static string EventType
        {
            get { return "hold.expired"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid HoldId { get; init; }

        public required Guid LabelerId { get; init; }

        public required long AmountVnd { get; init; }
    }

    /// <summary>Du an ket thuc binh thuong: phan ky quy con du tra ve doanh nghiep.</summary>
    public sealed record EscrowReleased : IEventPayload
    {
        public static string EventType
        {
            get { return "escrow.released"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        public required long AmountVnd { get; init; }
    }

    /// <summary>Du an bi huy: hoan phan ky quy chua dung (VD-M-10).</summary>
    public sealed record RefundIssued : IEventPayload
    {
        public static string EventType
        {
            get { return "refund.issued"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required Guid OwnerId { get; init; }

        public required long AmountVnd { get; init; }
    }

    /// <summary>
    /// Labeler xin rut: ledger DA tru so du kha dung (tien nam o tai khoan
    /// "dang chuyen"). payment-svc thuc hien chuyen khoan qua cong.
    /// </summary>
    public sealed record PayoutRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "payout.requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency o payment-svc va o cong thanh toan.</summary>
        public required Guid WithdrawalId { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>So tien THUC CHUYEN sau khi khau tru thue (VD-M-11).</summary>
        public required long NetAmountVnd { get; init; }

        public required string BankAccount { get; init; }
    }
}
