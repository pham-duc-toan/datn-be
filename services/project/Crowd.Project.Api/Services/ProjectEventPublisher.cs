using System;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Project.Api.Helpers;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Mot cho duy nhat bien payload thanh envelope va xep vao outbox, voi day du
    /// producer, correlationId, causationId va actor lay tu Caller.
    ///
    /// Chi Enqueue (them vao change tracker) — KHONG SaveChanges. Event commit
    /// cung transaction voi thay doi nghiep vu ma service luu ngay sau do.
    /// </summary>
    public sealed class ProjectEventPublisher
    {
        public const string Producer = "project-svc";

        private readonly IOutboxWriter _outbox;

        public ProjectEventPublisher(IOutboxWriter outbox)
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
