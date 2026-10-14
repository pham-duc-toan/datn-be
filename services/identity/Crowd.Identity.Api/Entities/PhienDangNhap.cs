using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Mot PHIEN dang nhap (mot thiet bi / trinh duyet) — giu refresh token HIEN TAI cua phien do.
    ///
    /// Truoc day moi lan lam moi token them mot dong (token cu giu toi het han de phat hien dung lai):
    /// nguoi dung hoat dong deu sinh ~400 dong / 14 ngay. Bay gio MOT dong moi phien, xoay tai cho.
    ///
    /// BA NGUYEN TAC van giu:
    ///
    /// 1. CHI LUU HASH cua token hien tai, khong luu token goc — lo database khong dung de dang nhap duoc.
    ///
    /// 2. XOAY VONG: moi lan lam moi, phien doi sang token moi (hash moi, the he + 1); token cu het dung.
    ///
    /// 3. PHAT HIEN DUNG LAI: refresh token mang MA PHIEN + bi mat. Token dung ma phien nhung hash KHAC
    ///    token hien tai = mot token CU cua phien bi dem ra dung — it nhat hai ben dang giu (nguoi that va
    ///    ke trom). Khong biet ai la ai nen thu hoi ca phien, ca hai phai dang nhap lai.
    /// </summary>
    public sealed class PhienDangNhap
    {
        private PhienDangNhap()
        {
            TokenHash = string.Empty;
        }

        /// <summary>Ma phien — nam trong refresh token de tim dong nay bang khoa chinh.</summary>
        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>SHA-256 (hex) cua refresh token HIEN TAI.</summary>
        public string TokenHash { get; private set; }

        /// <summary>So lan da xoay — de dieu tra, khong dung de xac thuc.</summary>
        public int Generation { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset LastRotatedAt { get; private set; }

        /// <summary>Truot: moi lan lam moi lui them mot tuoi tho refresh token.</summary>
        public DateTimeOffset ExpiresAt { get; private set; }

        /// <summary>null = con hieu luc.</summary>
        public DateTimeOffset? RevokedAt { get; private set; }

        public static PhienDangNhap Mo(Guid id, Guid userId, string tokenHash, DateTimeOffset luc, TimeSpan tuoiTho)
        {
            PhienDangNhap p = new PhienDangNhap();
            p.Id = id;
            p.UserId = userId;
            p.TokenHash = tokenHash;
            p.Generation = 0;
            p.CreatedAt = luc;
            p.LastRotatedAt = luc;
            p.ExpiresAt = luc + tuoiTho;
            p.RevokedAt = null;
            return p;
        }

        public bool ConDung(DateTimeOffset luc)
        {
            return RevokedAt == null && ExpiresAt > luc;
        }

        /// <summary>Token dua len co phai token HIEN TAI cua phien khong (so hash, thoi gian hang).</summary>
        public bool LaTokenHienTai(string tokenHash)
        {
            if (tokenHash == null || tokenHash.Length != TokenHash.Length)
            {
                return false;
            }

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(tokenHash), System.Text.Encoding.ASCII.GetBytes(TokenHash));
        }

        /// <summary>Lam moi: doi sang token moi, gia han.</summary>
        public void Xoay(string tokenHashMoi, DateTimeOffset luc, TimeSpan tuoiTho)
        {
            TokenHash = tokenHashMoi;
            Generation = Generation + 1;
            LastRotatedAt = luc;
            ExpiresAt = luc + tuoiTho;
        }

        public void ThuHoi(DateTimeOffset luc)
        {
            if (RevokedAt == null)
            {
                RevokedAt = luc;
            }
        }
    }
}
