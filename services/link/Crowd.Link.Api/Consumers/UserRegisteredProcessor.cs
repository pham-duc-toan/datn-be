using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Link.Api.Consumers
{
    /// <summary>user.registered: ghi luc dang ky — de gioi han cua so nhap ma gioi thieu (FS-08).</summary>
    public sealed class UserRegisteredProcessor : IEventProcessor<UserRegistered>
    {
        private readonly LinkDbContext _db;

        public UserRegisteredProcessor(LinkDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<UserRegistered> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            Guid id = envelope.Payload.UserId;
            if (await _db.KnownUsers.AnyAsync(u => u.UserId == id, ct))
            {
                return;
            }

            _db.KnownUsers.Add(KnownUser.Tao(id, envelope.OccurredAt));
        }
    }
}
