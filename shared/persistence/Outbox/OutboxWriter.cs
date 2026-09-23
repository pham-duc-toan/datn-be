using System;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Hiện thực IOutboxWriter trên một DbContext bất kỳ.
    /// Không ràng buộc vào DbContext cụ thể nào, nên cả 13 service dùng chung
    /// một bản.
    ///
    /// Đăng ký trong DI theo vòng đời Scoped, CÙNG vòng đời với DbContext —
    /// nếu khác nhau thì Enqueue sẽ ghi vào một change tracker khác với cái mà
    /// code nghiệp vụ đang dùng, và event sẽ rơi ra ngoài transaction.
    /// </summary>
    public sealed class OutboxWriter : IOutboxWriter
    {
        private readonly DbContext _db;

        public OutboxWriter(DbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public void Enqueue<TPayload>(EventEnvelope<TPayload> envelope)
            where TPayload : class, IEventPayload
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            OutboxMessage dong = OutboxMessage.From(envelope);

            // Chỉ Add vào change tracker của EF. KHÔNG gọi SaveChanges —
            // xem giao kèo giải thích ở IOutboxWriter.
            _db.Set<OutboxMessage>().Add(dong);
        }
    }
}
