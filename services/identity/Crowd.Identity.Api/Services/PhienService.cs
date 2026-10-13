using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Thu hoi MOI phien dang nhap cua mot nguoi dung (VD-S-11): khoa tai khoan, doi / dat lai mat
    /// khau, dang xuat moi thiet bi.
    ///   - refresh token con han: thu hoi ngay (khong lam moi duoc nua);
    ///   - access token dang song: khong thu hoi duoc tai cho — ghi MOC thu hoi va phat
    ///     user.sessions_revoked; gateway tu choi moi access token phat truoc moc.
    ///
    /// Moc lam tron xuong GIAY: iat cua JWT tinh bang giay. Token phat ngay sau moc trong cung
    /// giay (vd cap token moi tra ve khi doi mat khau) co iat == moc va VAN dung duoc.
    ///
    /// Khong SaveChanges — noi goi quyet dinh transaction (ExecuteUpdate chay ngay, nam trong
    /// transaction cua noi goi neu co).
    /// </summary>
    public sealed class PhienService
    {
        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;

        public PhienService(IdentityDbContext db, IOutboxWriter outbox)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            _db = db;
            _outbox = outbox;
        }

        public async Task<DateTimeOffset> ThuHoiTatCaAsync(Guid userId, string lyDo, Caller caller, DateTimeOffset luc, CancellationToken ct)
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }

            DateTimeOffset moc = LamTronGiay(luc);
            DateTimeOffset? thuHoiLuc = moc;

            // Chi dong CON HAN — dong het han de job don xoa, khong tranh khoa (RefreshTokenCleanupService).
            await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > moc)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, thuHoiLuc), ct);

            MocThuHoiPhien? cu = await _db.MocThuHoiPhiens.FirstOrDefaultAsync(m => m.UserId == userId, ct);
            if (cu == null)
            {
                _db.MocThuHoiPhiens.Add(MocThuHoiPhien.Tao(userId, moc));
            }
            else
            {
                cu.DoiMoc(moc);
            }

            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: new SessionsRevoked { UserId = userId, RevokedAt = moc, Reason = lyDo },
                causationId: caller.CausationId,
                actor: caller.TaoActor()));

            return moc;
        }

        public static DateTimeOffset LamTronGiay(DateTimeOffset luc)
        {
            return DateTimeOffset.FromUnixTimeSeconds(luc.ToUnixTimeSeconds());
        }
    }
}
