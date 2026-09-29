using System;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;

namespace Crowd.Payment.Api.Services
{
    /// <summary>Xep event vao outbox voi producer payment-svc. Chi Enqueue, khong SaveChanges.</summary>
    public sealed class PaymentEventPublisher
    {
        public const string Producer = "payment-svc";

        private readonly IOutboxWriter _outbox;

        public PaymentEventPublisher(IOutboxWriter outbox)
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
    }
}
