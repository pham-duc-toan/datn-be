using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Crowd.Identity.Api.Services;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Seeding
{
    /// <summary>
    /// Seed TAI KHOAN cua kich ban (KichBanSeed.Users) — gom ca admin, thu
    /// khong the tao qua /auth/register (form dang ky chan tu cap quyen admin).
    ///
    /// Di qua dung User.Tao + MatKhauService.Bam nhu luong dang ky that, chi
    /// khac: ID tat dinh (de cac service khac khop) va KHONG phat user.registered
    /// (khong co email that nao de gui).
    /// </summary>
    public sealed class IdentitySeeder
    {
        private readonly IdentityDbContext _db;
        private readonly MatKhauService _matKhau;
        private readonly TimeProvider _clock;
        private readonly ILogger<IdentitySeeder> _logger;

        public IdentitySeeder(IdentityDbContext db, MatKhauService matKhau, TimeProvider clock, ILogger<IdentitySeeder> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (matKhau == null)
            {
                throw new ArgumentNullException(nameof(matKhau));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _matKhau = matKhau;
            _clock = clock;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Dau moc: admin seed da co → da seed roi, khong lam gi (chay lai an toan).
            Guid adminId = KichBanSeed.User("admin").Id;
            if (await _db.Users.AnyAsync(u => u.Id == adminId, ct))
            {
                _logger.LogInformation("Seed identity: da co du lieu seed — bo qua");
                return;
            }

            DateTimeOffset lucTao = _clock.GetUtcNow() - TimeSpan.FromDays(10);
            int soTao = 0;

            foreach (SeedUser su in KichBanSeed.Users)
            {
                string email = User.ChuanHoaEmail(su.Email);

                // Email da bi dang ky tay tu truoc (khac ID) → UNIQUE email se chan.
                // Bo qua tai khoan nay thay vi lam hong ca lan seed.
                if (await _db.Users.AnyAsync(u => u.Email == email, ct))
                {
                    _logger.LogWarning("Seed identity: email {Email} da ton tai voi ID khac — bo qua tai khoan nay", email);
                    continue;
                }

                User user = User.Tao(email, su.DisplayName, su.Roles, lucTao);
                SeedIds.GanId(user, su.Id);
                user.DatPasswordHash(_matKhau.Bam(user, KichBanSeed.MatKhauChung));

                _db.Users.Add(user);
                soTao = soTao + 1;
            }

            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Seed identity: tao {SoTao} tai khoan, mat khau chung '{MatKhau}'",
                soTao,
                KichBanSeed.MatKhauChung);
        }
    }
}
