using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Ledger
{
    // Ledger-svc (P1) PHAT hai event nay; project-svc NHAN. Khai truoc o day
    // de project-svc dung duoc ngay — hop dong nam giua hai ben, khong thuoc
    // rieng ben nao.

    /// <summary>SAGA BUOC 2 (docs 3.4): du tien, da giu ky quy.</summary>
    public sealed record EscrowReserved : IEventPayload
    {
        public static string EventType
        {
            get { return "escrow.reserved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        /// <summary>So tien da giu, so nguyen dong.</summary>
        public required long AmountVnd { get; init; }
    }

    /// <summary>SAGA BUOC 2 — nhanh that bai: khong du tien, du an ve Nhap.</summary>
    public sealed record EscrowRejected : IEventPayload
    {
        public static string EventType
        {
            get { return "escrow.rejected"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        /// <summary>Hien cho doanh nghiep, vd "So du kha dung 2.000.000d, can 5.000.000d".</summary>
        public required string Reason { get; init; }
    }
}
