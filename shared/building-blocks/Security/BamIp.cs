using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Crowd.BuildingBlocks.Security
{
    /// <summary>
    /// Bam dia chi IP co muoi bi mat — khong luu IP tho (du lieu ca nhan, VD-P).
    /// link-svc va gate-svc dung CUNG muoi (cau hinh Privacy:IpSalt) de so sanh
    /// "nguoi vuot link co phai chinh nguoi tao link" (VD-L-04).
    /// </summary>
    public static class BamIp
    {
        /// <summary>sha256(muoi | ip), hex thuong.</summary>
        public static string Bam(string ip, string muoi)
        {
            if (ip == null)
            {
                throw new ArgumentNullException(nameof(ip));
            }

            if (string.IsNullOrEmpty(muoi))
            {
                throw new ArgumentException("Thieu muoi bam IP (Privacy:IpSalt).", nameof(muoi));
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(muoi + "|" + ip)));
        }

        /// <summary>
        /// Ban theo NGAY (UTC) — dung cho "moi IP mot luot moi ngay": cung IP khac ngay
        /// cho ma khac nhau, nen khong ghep duoc lich su di lai cua mot nguoi qua nhieu ngay.
        /// </summary>
        public static string BamTheoNgay(string ip, string muoi, DateTimeOffset luc)
        {
            return Bam(ip, muoi + "|" + luc.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }
    }
}
