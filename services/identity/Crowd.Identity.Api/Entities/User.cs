using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Mot tai khoan. identity_db la noi DUY NHAT trong he thong chua hash mat
    /// khau — do chinh la ly do identity-svc tach thanh service rieng.
    /// </summary>
    public sealed class User
    {
        /// <summary>
        /// Vai tro luu trong truong private, chi lo ra ngoai dang CHI DOC.
        /// Code ben ngoai khong the users.Roles.Add("admin") — muon doi vai tro
        /// phai di qua mot phuong thuc co kiem soat.
        /// </summary>
        private List<string> _roles;

        /// <summary>EF Core can constructor khong tham so de dung lai tu database.</summary>
        private User()
        {
            Email = string.Empty;
            PasswordHash = string.Empty;
            DisplayName = string.Empty;
            _roles = new List<string>();
        }

        public Guid Id { get; private set; }

        /// <summary>
        /// Da chuan hoa: cat khoang trang, chu thuong. Nho vay "A@x.com" va
        /// " a@x.com" khong thanh hai tai khoan khac nhau.
        /// </summary>
        public string Email { get; private set; }

        /// <summary>
        /// Ket qua cua PasswordHasher (PBKDF2, 100.000 vong, muoi ngau nhien).
        /// Khong bao gio ra khoi service nay — khong nam trong event, khong nam
        /// trong response, khong nam trong log.
        /// </summary>
        public string PasswordHash { get; private set; }

        public string DisplayName { get; private set; }

        public IReadOnlyList<string> Roles
        {
            get { return _roles; }
        }

        public UserStatus Status { get; private set; }

        /// <summary>Luc bi admin khoa (FM-01); null = dang hoat dong.</summary>
        public DateTimeOffset? BlockedAt { get; private set; }

        public string? BlockedReason { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static User Tao(
            string emailDaChuanHoa,
            string displayName,
            IEnumerable<string> roles,
            DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(emailDaChuanHoa))
            {
                throw new ArgumentException("email khong duoc rong", nameof(emailDaChuanHoa));
            }

            if (roles == null)
            {
                throw new ArgumentNullException(nameof(roles));
            }

            User user = new User();

            user.Id = Guid.CreateVersion7();
            user.Email = emailDaChuanHoa;
            user.DisplayName = displayName;
            user._roles = new List<string>(roles);
            user.Status = UserStatus.Active;
            user.CreatedAt = luc;

            return user;
        }

        /// <summary>Admin khoa tai khoan. Tra ve false neu da bi khoa tu truoc (khong phat event lan hai).</summary>
        public bool Khoa(string lyDo, DateTimeOffset luc)
        {
            if (Status == UserStatus.Blocked)
            {
                return false;
            }

            Status = UserStatus.Blocked;
            BlockedAt = luc;
            BlockedReason = lyDo;
            return true;
        }

        /// <summary>Admin mo khoa. Tra ve false neu dang hoat dong san.</summary>
        public bool MoKhoa()
        {
            if (Status != UserStatus.Blocked)
            {
                return false;
            }

            Status = UserStatus.Active;
            BlockedAt = null;
            BlockedReason = null;
            return true;
        }

        public bool LaDoanhNghiep()
        {
            return _roles.Contains("business");
        }

        public void DatPasswordHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                throw new ArgumentException("hash khong duoc rong", nameof(hash));
            }

            PasswordHash = hash;
        }

        public static string ChuanHoaEmail(string email)
        {
            if (email == null)
            {
                throw new ArgumentNullException(nameof(email));
            }

            return email.Trim().ToLowerInvariant();
        }
    }

    public enum UserStatus
    {
        Active,
        Blocked,
    }
}
