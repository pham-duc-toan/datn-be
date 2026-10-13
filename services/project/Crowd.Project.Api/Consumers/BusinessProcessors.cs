using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Project.Domain.Businesses;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// business.verified → ban sao "doanh nghiep da xac minh": tu day publish du an duoc.
    /// Giao lai → da co thi thoi (idempotent).
    /// </summary>
    public sealed class BusinessVerifiedProcessor : IEventProcessor<BusinessVerified>
    {
        private readonly ProjectDbContext _db;

        public BusinessVerifiedProcessor(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<BusinessVerified> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            BusinessVerified p = envelope.Payload;
            if (!await _db.VerifiedBusinesses.AnyAsync(x => x.BusinessId == p.BusinessId, ct))
            {
                _db.VerifiedBusinesses.Add(VerifiedBusiness.Tao(p.BusinessId, p.VerifiedAt));
            }
        }
    }

    /// <summary>user.registered → tai khoan moi CHUA xac thuc email (chua publish duoc). Da co dong thi giu nguyen.</summary>
    public sealed class UserRegisteredEmailProcessor : IEventProcessor<UserRegistered>
    {
        private readonly ProjectDbContext _db;

        public UserRegisteredEmailProcessor(ProjectDbContext db)
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

            Guid uid = envelope.Payload.UserId;
            if (!await _db.EmailStatuses.AnyAsync(e => e.UserId == uid, ct))
            {
                _db.EmailStatuses.Add(EmailStatus.ChuaXacThuc(uid));
            }
        }
    }

    /// <summary>user.email_verified → publish duoc (neu da KYB).</summary>
    public sealed class UserEmailVerifiedProcessor : IEventProcessor<UserEmailVerified>
    {
        private readonly ProjectDbContext _db;

        public UserEmailVerifiedProcessor(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<UserEmailVerified> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            Guid uid = envelope.Payload.UserId;
            EmailStatus? e = await _db.EmailStatuses.FirstOrDefaultAsync(x => x.UserId == uid, ct);
            if (e == null)
            {
                e = EmailStatus.ChuaXacThuc(uid);
                _db.EmailStatuses.Add(e);
            }

            e.XacThuc();
        }
    }
}
