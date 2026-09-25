using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Mot refresh token da phat. Dung de doi lay access token moi khi cai cu
    /// het han (15 phut), ma khong bat nguoi dung dang nhap lai.
    ///
    /// BA NGUYEN TAC:
    ///
    /// 1. CHI LUU HASH, khong luu token goc. Lo database thi ke tan cong co
    ///    trong tay mot dong hash, khong dung de dang nhap duoc. Dung SHA-256
    ///    chu khong can PBKDF2 nhu mat khau: token la 256 bit ngau nhien, doan
    ///    mo la bat kha thi nen khong can lam cham viec bam.
    ///
    /// 2. XOAY VONG: moi lan dung la het han ngay, doi lay mot cai moi. Token
    ///    bi danh cap chi dung duoc mot lan.
    ///
    /// 3. PHAT HIEN DUNG LAI: token DA XOAY ma bi dem ra dung lan nua thi chac
    ///    chan co hai ben dang giu no — nguoi dung that va ke trom. Khong biet
    ///    ai la ai, nen thu hoi CA HO (FamilyId), bat ca hai dang nhap lai.
    /// </summary>
    public sealed class RefreshToken
    {
        private RefreshToken()
        {
            TokenHash = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>SHA-256 cua token goc, dang hex.</summary>
        public string TokenHash { get; private set; }

        /// <summary>
        /// Moi lan dang nhap sinh mot ho moi. Moi token xoay ra tu do deu cung
        /// ho. Phat hien dung lai thi thu hoi ca ho bang mot cau UPDATE.
        /// </summary>
        public Guid FamilyId { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        /// <summary>null = con hieu luc.</summary>
        public DateTimeOffset? RevokedAt { get; private set; }

        /// <summary>Token moi da thay the no — de lan theo chuoi xoay khi dieu tra.</summary>
        public Guid? ReplacedById { get; private set; }

        public static RefreshToken Tao(
            Guid userId,
            string tokenHash,
            Guid familyId,
            DateTimeOffset luc,
            TimeSpan tuoiTho)
        {
            RefreshToken token = new RefreshToken();

            token.Id = Guid.CreateVersion7();
            token.UserId = userId;
            token.TokenHash = tokenHash;
            token.FamilyId = familyId;
            token.CreatedAt = luc;
            token.ExpiresAt = luc + tuoiTho;
            token.RevokedAt = null;
            token.ReplacedById = null;

            return token;
        }
    }
}
