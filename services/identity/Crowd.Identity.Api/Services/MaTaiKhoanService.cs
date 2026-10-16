using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Phat va tra cuu ma dung mot lan gui qua email (xac thuc email, dat lai mat khau).
    /// Ma goc chi nam trong link gui di; database giu SHA-256 (cung cach voi refresh token).
    /// Phat ma moi thi ma cu cung muc dich het hieu luc — chi link moi nhat bam duoc.
    /// Khong SaveChanges — noi goi quyet dinh transaction.
    /// </summary>
    public sealed class MaTaiKhoanService
    {
        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public MaTaiKhoanService(IdentityDbContext db, IOutboxWriter outbox, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
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
            _outbox = outbox;
            _settings = settings;
            _clock = clock;
        }

        /// <summary>Phat ma xac thuc email + event de notification gui email.</summary>
        public async Task GuiXacThucEmailAsync(User user, Caller caller, CancellationToken ct)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            TimeSpan song = _settings.ThoiGian(SettingKeys.IdentityEmailVerifyTtl);
            string maGoc = await PhatMaAsync(user.Id, MucDichMa.VerifyEmail, song, ct);
            Phat(caller, new EmailVerificationRequested
            {
                UserId = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                VerifyUrl = TaoLink("xac-thuc-email", maGoc),
                ExpiresAt = _clock.GetUtcNow() + song,
            });
        }

        /// <summary>Phat ma dat lai mat khau + event de notification gui email.</summary>
        public async Task GuiDatLaiMatKhauAsync(User user, Caller caller, CancellationToken ct)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            TimeSpan song = _settings.ThoiGian(SettingKeys.IdentityPasswordResetTtl);
            string maGoc = await PhatMaAsync(user.Id, MucDichMa.ResetPassword, song, ct);
            Phat(caller, new PasswordResetRequested
            {
                UserId = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                ResetUrl = TaoLink("dat-lai-mat-khau", maGoc),
                ExpiresAt = _clock.GetUtcNow() + song,
            });
        }

        /// <summary>
        /// Phat ma thu thach dang nhap 2FA (FC-04): client da nhap dung mat khau, cam ma nay de gui kem
        /// ma 6 so. Thu thach cu cua user bi huy (moi lan dang nhap mot thu thach).
        /// </summary>
        public Task<string> PhatThuThachHaiLopAsync(Guid userId, TimeSpan song, CancellationToken ct)
        {
            return PhatMaAsync(userId, MucDichMa.TwoFactorLogin, song, ct);
        }

        /// <summary>Lan phat ma gan nhat cua muc dich nay (chong spam "gui lai").</summary>
        public Task<DateTimeOffset?> LanPhatGanNhatAsync(Guid userId, MucDichMa mucDich, CancellationToken ct)
        {
            return _db.MaTaiKhoans
                .Where(m => m.UserId == userId && m.Purpose == mucDich)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (DateTimeOffset?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>Ma con dung duoc (dung muc dich, chua dung, chua het han); khong thi null.</summary>
        public async Task<MaTaiKhoan?> TimMaHopLeAsync(string? maGoc, MucDichMa mucDich, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(maGoc) || maGoc.Length > 200)
            {
                return null;
            }

            string hash = TokenIssuer.BamRefreshToken(maGoc);
            MaTaiKhoan? m = await _db.MaTaiKhoans.FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == mucDich, ct);
            if (m == null || !m.DungDuoc(_clock.GetUtcNow()))
            {
                return null;
            }

            return m;
        }

        private async Task<string> PhatMaAsync(Guid userId, MucDichMa mucDich, TimeSpan song, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset? daDung = bayGio;
            await _db.MaTaiKhoans
                .Where(m => m.UserId == userId && m.Purpose == mucDich && m.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.UsedAt, daDung), ct);

            string maGoc = TokenIssuer.TaoRefreshTokenGoc();
            _db.MaTaiKhoans.Add(MaTaiKhoan.Tao(userId, mucDich, TokenIssuer.BamRefreshToken(maGoc), bayGio, song));
            return maGoc;
        }

        private string TaoLink(string duongDan, string maGoc)
        {
            string goc = _settings.Chuoi(SettingKeys.IdentityFrontendUrl).TrimEnd('/');
            return goc + "/" + duongDan + "?token=" + Uri.EscapeDataString(maGoc);
        }

        private void Phat<TPayload>(Caller caller, TPayload payload)
            where TPayload : class, IEventPayload
        {
            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: payload,
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }
    }
}
