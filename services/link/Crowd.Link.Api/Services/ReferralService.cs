using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Link;
using Crowd.Link.Api.Dtos;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Link.Api.Services
{
    /// <summary>
    /// Gioi thieu (FS-08). link-svc giu QUAN HE gioi thieu; tien hoa hong do ledger tinh
    /// (nghe referral.registered) va chi tra khi nguoi duoc moi da tu kiem vuot nguong
    /// (VD-L-05 — chong tai khoan clone tu moi nhau).
    ///
    /// Chi tai khoan MOI (trong cua so link.referral_claim_window ke tu luc dang ky) moi
    /// nhap duoc ma: khong cho nguoi cu "gan" minh vao ai do sau khi da kiem tien.
    /// </summary>
    public sealed class ReferralService
    {
        private const int DoDaiMa = 8;

        private readonly LinkDbContext _db;
        private readonly LinkEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public ReferralService(LinkDbContext db, LinkEventPublisher events, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _events = events;
            _settings = settings;
            _clock = clock;
        }

        /// <summary>Ma gioi thieu cua toi (tao lan dau) + so nguoi da moi.</summary>
        public async Task<ReferralInfoResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            ReferralCode? ma = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, ct);
            if (ma == null)
            {
                ma = ReferralCode.Tao(userId, UrlRules.SinhMa(DoDaiMa), _clock.GetUtcNow());
                _db.ReferralCodes.Add(ma);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
                {
                    // Hai request cung luc (hoac trung ma rat hiem): doc lai ban da luu.
                    _db.ChangeTracker.Clear();
                    ma = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, ct);
                    if (ma == null)
                    {
                        throw new LinkException(409, "xung_dot_dong_thoi", "Thu lai sau it giay.");
                    }
                }
            }

            int soNguoi = await _db.Referrals.CountAsync(r => r.ReferrerId == userId, ct);
            Referral? cuaToi = await _db.Referrals.AsNoTracking().FirstOrDefaultAsync(r => r.ReferredId == userId, ct);
            return new ReferralInfoResponse { Code = ma.Code, ReferredCount = soNguoi, ReferredBy = cuaToi == null ? null : cuaToi.ReferrerId };
        }

        public async Task NhapMaAsync(ClaimReferralRequest body, Caller caller, CancellationToken ct)
        {
            string ma = body == null || body.Code == null ? string.Empty : body.Code.Trim();
            Guid toi = caller.LayUserId();

            ReferralCode? chu = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.Code == ma, ct);
            if (chu == null)
            {
                throw new LinkException(404, "ma_gioi_thieu_sai", "Ma gioi thieu khong ton tai.");
            }

            if (chu.UserId == toi)
            {
                throw new LinkException(409, "tu_gioi_thieu", "Khong nhap ma gioi thieu cua chinh minh.");
            }

            KnownUser? u = await _db.KnownUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == toi, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (u == null || bayGio - u.RegisteredAt > _settings.ThoiGian(SettingKeys.LinkReferralClaimWindow))
            {
                throw new LinkException(409, "qua_han_nhap_ma", "Chi tai khoan moi dang ky moi nhap duoc ma gioi thieu.");
            }

            // Vong A moi B, B moi A: chan.
            bool vong = await _db.Referrals.AnyAsync(r => r.ReferrerId == toi && r.ReferredId == chu.UserId, ct);
            if (vong)
            {
                throw new LinkException(409, "gioi_thieu_vong", "Hai tai khoan khong the gioi thieu lan nhau.");
            }

            _db.Referrals.Add(Referral.Tao(chu.UserId, toi, bayGio));
            _events.Phat(caller, new ReferralRegistered { ReferrerId = chu.UserId, ReferredId = toi, RegisteredAt = bayGio });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "PK_referrals"))
            {
                throw new LinkException(409, "da_co_nguoi_gioi_thieu", "Ban da nhap ma gioi thieu roi.");
            }
        }
    }
}
