using System;

namespace Crowd.BuildingBlocks.Persistence.Idempotency
{
    /// <summary>
    /// Dau vet "event nay da duoc handler nay xu ly roi".
    ///
    /// LY DO TON TAI (VD-D-02, VD-M-02): RabbitMQ la at-least-once. Message SE
    /// toi hai lan — dispatcher gui xong nhung chet truoc khi kip danh dau
    /// published_at, vong sau no gui lai. Khong co bang nay thi ledger-svc ghi
    /// but toan hai lan va labeler duoc tra tien hai lan.
    ///
    /// DOI XUNG VOI OUTBOX: outbox dam bao event ROI KHOI service gui; bang
    /// nay dam bao event chi CO HIEU LUC MOT LAN o service nhan. Ca hai deu
    /// dua tren cung mot meo — nam trong CUNG transaction voi thay doi nghiep
    /// vu, nen khong can transaction phan tan.
    ///
    /// Khoa chinh la cap (EventId, Handler) chu khong phai rieng EventId: mot
    /// event co the co NHIEU handler cung nghe. `annotation.approved` duoc ca
    /// ledger-svc (chi tra) lan identity-svc (cong uy tin) xu ly, va hai ben
    /// phai xu ly doc lap nhau.
    /// </summary>
    public sealed class ProcessedEvent
    {
        /// <summary>EF Core can constructor khong tham so de dung lai tu database.</summary>
        private ProcessedEvent()
        {
            Handler = string.Empty;
        }

        /// <summary>EventId cua envelope. UUIDv7 nen INSERT ghi tuan tu vao cuoi B-tree.</summary>
        public Guid EventId { get; private set; }

        /// <summary>
        /// Ten handler da xu ly, vd "ChiTraNhanDuocDuyet".
        ///
        /// Phai la ten ON DINH, khong duoc dung typeof(...).FullName roi doi
        /// ten lop sau nay — doi ten la moi event cu bong nhien "chua xu ly"
        /// va bi lam lai tu dau.
        /// </summary>
        public string Handler { get; private set; }

        /// <summary>De do do tre va don bang khi qua cu.</summary>
        public DateTimeOffset ProcessedAt { get; private set; }

        public static ProcessedEvent Tao(Guid eventId, string handler, DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(handler))
            {
                throw new ArgumentException("handler khong duoc rong", nameof(handler));
            }

            ProcessedEvent dong = new ProcessedEvent();

            dong.EventId = eventId;
            dong.Handler = handler;
            dong.ProcessedAt = luc;

            return dong;
        }
    }
}
