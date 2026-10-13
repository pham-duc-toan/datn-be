using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Ma dung MOT LAN gui qua email: xac thuc email (FC-01) hoac dat lai mat khau (FC-03).
    /// Giong refresh token: client giu ma goc, database chi giu SHA-256 — lo database khong lo ma.
    /// </summary>
    public sealed class MaTaiKhoan
    {
        private MaTaiKhoan()
        {
            TokenHash = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public MucDichMa Purpose { get; private set; }

        public string TokenHash { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        /// <summary>Luc da dung (hoac bi thay bang ma moi cung muc dich); null = con dung duoc.</summary>
        public DateTimeOffset? UsedAt { get; private set; }

        public static MaTaiKhoan Tao(Guid userId, MucDichMa mucDich, string tokenHash, DateTimeOffset luc, TimeSpan song)
        {
            MaTaiKhoan m = new MaTaiKhoan();
            m.Id = Guid.CreateVersion7();
            m.UserId = userId;
            m.Purpose = mucDich;
            m.TokenHash = tokenHash;
            m.CreatedAt = luc;
            m.ExpiresAt = luc + song;
            return m;
        }

        public bool DungDuoc(DateTimeOffset luc)
        {
            return UsedAt == null && ExpiresAt > luc;
        }

        /// <summary>Danh dau da dung (hoac bi ma moi thay the) — tu day khong dung lai duoc.</summary>
        public void Dung(DateTimeOffset luc)
        {
            if (UsedAt == null)
            {
                UsedAt = luc;
            }
        }
    }

    public enum MucDichMa
    {
        VerifyEmail,
        ResetPassword,
    }
}
