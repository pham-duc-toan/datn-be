using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Notification.Api.Seeding
{
    /// <summary>
    /// Dia chi lien lac cua tai khoan seed: identity seed KHONG phat user.registered (seed bo event
    /// chua gui), nen ban sao o day tu them. Idempotent theo tung tai khoan.
    /// </summary>
    public sealed class NotificationSeeder
    {
        private readonly NotificationDbContext _db;
        private readonly ILogger<NotificationSeeder> _logger;

        public NotificationSeeder(NotificationDbContext db, ILogger<NotificationSeeder> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            int so = 0;
            foreach (SeedUser su in KichBanSeed.Users)
            {
                LienHe? lh = await _db.LienHes.FirstOrDefaultAsync(x => x.UserId == su.Id, ct);
                if (lh == null)
                {
                    _db.LienHes.Add(LienHe.Tao(su.Id, su.Email.Trim().ToLowerInvariant(), su.DisplayName, su.Roles));
                    so = so + 1;
                }
                else if (lh.Roles.Count == 0)
                {
                    // DB cu (truoc khi luu vai tro): bo sung de thong bao theo doi tuong toi duoc.
                    lh.DatVaiTro(su.Roles);
                    so = so + 1;
                }
            }

            if (so > 0)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Seed notification: {So} dia chi lien lac", so);
            }
        }
    }
}
