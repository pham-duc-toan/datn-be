using System;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Ledger;
using Crowd.Ledger.Domain.Withdrawals;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>Xep event vao outbox voi producer ledger-svc. Chi Enqueue, khong SaveChanges.</summary>
    public sealed class LedgerEventPublisher
    {
        public const string Producer = "ledger-svc";

        private readonly IOutboxWriter _outbox;

        public LedgerEventPublisher(IOutboxWriter outbox)
        {
            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            _outbox = outbox;
        }

        public void Phat<TPayload>(Caller caller, TPayload payload)
            where TPayload : class, IEventPayload
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }

            _outbox.Enqueue(EventEnvelope.Create(
                producer: Producer,
                correlationId: caller.CorrelationId,
                payload: payload,
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }

        /// <summary>payout.requested — lenh rut DA DUYET, payment-svc chuyen khoan so thuc nhan.</summary>
        public void PhatYeuCauChi(Withdrawal w, Caller caller)
        {
            if (w == null)
            {
                throw new ArgumentNullException(nameof(w));
            }

            Phat(caller, new PayoutRequested
            {
                WithdrawalId = w.Id,
                LabelerId = w.LabelerId,
                NetAmountVnd = w.NetVnd,
                BankAccount = w.BankAccount,
            });
        }
    }
}
