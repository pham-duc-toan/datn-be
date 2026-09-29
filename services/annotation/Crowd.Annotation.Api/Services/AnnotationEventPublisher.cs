using System;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>Xep event vao outbox voi producer annotation-svc. Chi Enqueue, khong SaveChanges.</summary>
    public sealed class AnnotationEventPublisher
    {
        public const string Producer = "annotation-svc";

        private readonly IOutboxWriter _outbox;

        public AnnotationEventPublisher(IOutboxWriter outbox)
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
