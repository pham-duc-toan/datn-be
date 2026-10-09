using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Crowd.BuildingBlocks.Security
{
    /// <summary>
    /// Mat khau cua link rut gon (FS-06): link-svc bam, gate-svc kiem — nen dinh dang
    /// phai la hop dong chung giua hai service:
    ///     pbkdf2-sha256$&lt;so vong&gt;$&lt;muoi base64&gt;$&lt;bam base64&gt;
    /// So sanh bang FixedTimeEquals (khong lo do dai khop qua thoi gian phan hoi).
    /// </summary>
    public static class MatKhauLink
    {
        private const string TienTo = "pbkdf2-sha256";
        private const int SoVong = 100000;
        private const int DoDaiMuoi = 16;
        private const int DoDaiBam = 32;

        public static string Bam(string matKhau)
        {
            if (matKhau == null)
            {
                throw new ArgumentNullException(nameof(matKhau));
            }

            byte[] muoi = RandomNumberGenerator.GetBytes(DoDaiMuoi);
            byte[] bam = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(matKhau), muoi, SoVong, HashAlgorithmName.SHA256, DoDaiBam);
            return TienTo + "$" + SoVong.ToString(CultureInfo.InvariantCulture) + "$" + Convert.ToBase64String(muoi) + "$" + Convert.ToBase64String(bam);
        }

        /// <summary>Chuoi bam hong (sai dinh dang) thi coi nhu sai mat khau, khong nem loi.</summary>
        public static bool Kiem(string matKhau, string chuoiBam)
        {
            if (matKhau == null || string.IsNullOrEmpty(chuoiBam))
            {
                return false;
            }

            string[] phan = chuoiBam.Split('$');
            if (phan.Length != 4 || phan[0] != TienTo)
            {
                return false;
            }

            int soVong;
            if (!int.TryParse(phan[1], NumberStyles.None, CultureInfo.InvariantCulture, out soVong) || soVong < 1)
            {
                return false;
            }

            byte[] muoi;
            byte[] mongDoi;
            try
            {
                muoi = Convert.FromBase64String(phan[2]);
                mongDoi = Convert.FromBase64String(phan[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] bam = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(matKhau), muoi, soVong, HashAlgorithmName.SHA256, mongDoi.Length);
            return CryptographicOperations.FixedTimeEquals(bam, mongDoi);
        }
    }
}
