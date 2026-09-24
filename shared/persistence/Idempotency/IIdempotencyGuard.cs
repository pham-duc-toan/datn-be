using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crowd.BuildingBlocks.Persistence.Idempotency
{
    /// <summary>
    /// Cach duy nhat de xu ly mot event den tu bus.
    ///
    /// Bao boc toan bo nghiep vu trong mot transaction, cong them mot dong
    /// processed_events. Hai thu commit CUNG NHAU, nen khong the xay ra canh
    /// but toan da ghi ma he thong tuong chua xu ly (VD-D-02 diem 4).
    ///
    /// Cach dung:
    ///
    ///     await guard.XuLyMotLanAsync(
    ///         envelope.EventId,
    ///         "ChiTraNhanDuocDuyet",
    ///         async ct =&gt;
    ///         {
    ///             db.JournalEntries.Add(...);   // nghiep vu
    ///             outbox.Enqueue(...);          // event phat sinh
    ///         },
    ///         ct);
    ///
    /// Ben trong khong goi SaveChanges rieng cho tung thu — guard goi MOT lan
    /// cho ca nghiep vu lan dau vet, roi commit.
    /// </summary>
    public interface IIdempotencyGuard
    {
        /// <summary>
        /// Chay <paramref name="nghiepVu"/> dung mot lan cho cap
        /// (eventId, handler). Lan thu hai tro di la khong lam gi.
        /// </summary>
        /// <returns>
        /// true neu vua thuc su xu ly; false neu da xu ly tu truoc va lan nay
        /// bi bo qua. Consumer dung gia tri nay de ghi log, con ca hai truong
        /// hop deu coi la THANH CONG va ack message tren bus.
        /// </returns>
        Task<bool> XuLyMotLanAsync(
            Guid eventId,
            string handler,
            Func<CancellationToken, Task> nghiepVu,
            CancellationToken ct);
    }
}
