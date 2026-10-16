using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Ma dung MOT LAN: xac thuc email (FC-01), dat lai mat khau (FC-03) — gui qua email; hoac
    /// ma thu thach dang nhap 2FA (FC-04) — tra cho client sau khi nhap dung mat khau.
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

        /// <summary>So lan nhap sai ma 2FA voi thu thach nay (chi dung cho TwoFactorLogin).</summary>
        public int FailedAttempts { get; private set; }

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

        /// <summary>
        /// Ghi mot lan nhap sai. Du toiDa lan thi thu thach het hieu luc (phai dang nhap lai bang mat
        /// khau): ma 6 so chi co 10^6 kha nang, khong gioi han thi do duoc. Tra true neu vua het luot.
        /// </summary>
        public bool GhiSai(int toiDa, DateTimeOffset luc)
        {
            FailedAttempts++;
            if (FailedAttempts >= toiDa)
            {
                Dung(luc);
                return true;
            }

            return false;
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

        /// <summary>Da qua buoc mat khau, cho ma 2FA (FC-04).</summary>
        TwoFactorLogin,
    }
}
