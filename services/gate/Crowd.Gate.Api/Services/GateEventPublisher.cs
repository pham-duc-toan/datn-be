using System;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;

namespace Crowd.Gate.Api.Services
{
    /// <summary>Xep event vao outbox voi producer gate-svc. Chi Enqueue, khong SaveChanges.</summary>
    public sealed class GateEventPublisher
    {
        public const string Producer = "gate-svc";

        private readonly IOutboxWriter _outbox;

        public GateEventPublisher(IOutboxWriter outbox)
        {
            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            _outbox = outbox;
        }

        /// <summary>Event cua khach vang lai: actor = null (he thong), moi luot mot correlationId.</summary>
        public void PhatHeThong<TPayload>(TPayload payload)
            where TPayload : class, IEventPayload
        {
            _outbox.Enqueue(EventEnvelope.Create(
                producer: Producer,
                correlationId: Guid.CreateVersion7(),
                payload: payload,
                causationId: null,
                actor: null));
        }
    }
}
