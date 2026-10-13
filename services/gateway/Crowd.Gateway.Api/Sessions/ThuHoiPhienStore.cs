using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.Gateway.Api.Sessions
{
    /// <summary>
    /// Moc thu hoi phien cua tung nguoi dung, trong BO NHO (gateway khong co database).
    /// Token cua user co iat TRUOC moc → bi tu choi (VD-S-11).
    ///
    /// Chi giu moc con tac dung: moc cu hon tuoi access token thi moi token phat truoc no da tu
    /// het han — xoa di cho bo nho khong phinh.
    /// </summary>
    public sealed class ThuHoiPhienStore
    {
        private readonly ConcurrentDictionary<Guid, DateTimeOffset> _moc = new ConcurrentDictionary<Guid, DateTimeOffset>();
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public ThuHoiPhienStore(ISettings settings, TimeProvider clock)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _settings = settings;
            _clock = clock;
        }

        public int SoMoc
        {
            get { return _moc.Count; }
        }

        /// <summary>Ghi moc moi; giu moc MUON nhat (event den lech thu tu van dung).</summary>
        public void Ghi(Guid userId, DateTimeOffset revokedAt)
        {
            _moc.AddOrUpdate(userId, revokedAt, (khoa, cu) => revokedAt > cu ? revokedAt : cu);
        }

        /// <summary>Token phat luc <paramref name="issuedAt"/> cua user nay da bi thu hoi chua.</summary>
        public bool DaThuHoi(Guid userId, DateTimeOffset issuedAt)
        {
            DateTimeOffset moc;
            if (!_moc.TryGetValue(userId, out moc))
            {
                return false;
            }

            return issuedAt < moc;
        }

        /// <summary>Bo cac moc da het tac dung (cu hon tuoi access token).</summary>
        public int DonMocCu()
        {
            DateTimeOffset nguong = _clock.GetUtcNow() - _settings.ThoiGian(SettingKeys.IdentityAccessTokenLifetime);
            List<Guid> xoa = new List<Guid>();
            foreach (KeyValuePair<Guid, DateTimeOffset> kv in _moc)
            {
                if (kv.Value < nguong)
                {
                    xoa.Add(kv.Key);
                }
            }

            foreach (Guid id in xoa)
            {
                DateTimeOffset boQua;
                _moc.TryRemove(id, out boQua);
            }

            return xoa.Count;
        }
    }
}
