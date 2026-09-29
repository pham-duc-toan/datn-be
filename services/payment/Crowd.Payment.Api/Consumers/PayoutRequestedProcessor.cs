using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Ledger;
using Crowd.Payment.Domain.Payouts;
using Crowd.Payment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Payment.Api.Consumers
{
    /// <summary>
    /// payout.requested (ledger): CHI ghi lenh chuyen (pha 1 cua VD-M-09). Viec goi
    /// cong la cua PayoutWorker, NGOAI transaction nay.
    /// </summary>
    public sealed class PayoutRequestedProcessor : IEventProcessor<PayoutRequested>
    {
        private readonly PaymentDbContext _db;
        private readonly TimeProvider _clock;

        public PayoutRequestedProcessor(PaymentDbContext db, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<PayoutRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            PayoutRequested p = envelope.Payload;

            // Id lenh chuyen = WithdrawalId: event giao lai khong tao lenh thu hai.
            if (await _db.Payouts.AnyAsync(x => x.Id == p.WithdrawalId, ct))
            {
                return;
            }

            _db.Payouts.Add(Payout.Tao(p.WithdrawalId, p.LabelerId, p.NetAmountVnd, p.BankAccount, _clock.GetUtcNow()));
        }
    }
}
