using System;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Link;
using Crowd.Link.Api.Entities;

namespace Crowd.Link.Api.Services
{
    /// <summary>Xep event vao outbox voi producer link-svc. Chi Enqueue, khong SaveChanges.</summary>
    public sealed class LinkEventPublisher
    {
        public const string Producer = "link-svc";

        private readonly IOutboxWriter _outbox;

        public LinkEventPublisher(IOutboxWriter outbox)
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

        /// <summary>link.activated — gate-svc bat dau phuc vu (hoac cap nhat tuy chon) link nay.</summary>
        public void PhatKichHoat(ShortLink l, Caller caller)
        {
            if (l == null)
            {
                throw new ArgumentNullException(nameof(l));
            }

            Phat(caller, new LinkActivated
            {
                LinkId = l.Id,
                Code = l.Code,
                OwnerId = l.OwnerId,
                DestinationUrl = l.DestinationUrl,
                PasswordHash = l.PasswordHash,
                ExpiresAt = l.ExpiresAt,
                CampaignId = l.CampaignId,
                CreatorIpHash = l.CreatorIpHash,
            });
        }

        public void PhatVoHieu(ShortLink l, bool giuDoanhThu, Caller caller)
        {
            if (l == null)
            {
                throw new ArgumentNullException(nameof(l));
            }

            Phat(caller, new LinkDisabled
            {
                LinkId = l.Id,
                OwnerId = l.OwnerId,
                Reason = l.StatusReason ?? string.Empty,
                WithholdRevenue = giuDoanhThu,
            });
        }
    }
}
