using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Ma dung mot lan theo thoi gian (TOTP — RFC 6238), loai ma Google Authenticator / Microsoft
    /// Authenticator sinh ra (FC-04).
    ///
    ///   buoc   = floor(unix_giay / 30)
    ///   ma     = HMAC-SHA1(bi_mat, buoc dang 8 byte big-endian) → cat dong (RFC 4226) → 6 chu so
    ///
    /// Server va dien thoai cung bi mat + cung dong ho thi ra cung ma; khong ai phai gui gi cho ai.
    /// Chap nhan lech +-1 buoc (30 giay) vi dong ho dien thoai co the nhanh / cham.
    /// </summary>
    public static class Totp
    {
        public const int SoChuSo = 6;
        public const int GiayMoiBuoc = 30;

        /// <summary>So buoc lech cho phep moi phia (1 = chap nhan ma cua 30 giay truoc / sau).</summary>
        public const int BuocLechChoPhep = 1;

        /// <summary>20 byte = 160 bit, dung do dai khoa cua HMAC-SHA1 (RFC 4226 khuyen nghi).</summary>
        public static byte[] TaoBiMat()
        {
            return RandomNumberGenerator.GetBytes(20);
        }

        public static long BuocHienTai(DateTimeOffset luc)
        {
            return luc.ToUnixTimeSeconds() / GiayMoiBuoc;
        }

        /// <summary>Ma 6 chu so cua mot buoc thoi gian.</summary>
        public static string TinhMa(byte[] biMat, long buoc)
        {
            if (biMat == null)
            {
                throw new ArgumentNullException(nameof(biMat));
            }

            byte[] boDem = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                boDem[i] = (byte)(buoc & 0xFF);
                buoc = buoc >> 8;
            }

            // HMAC-SHA1 la thuat toan RFC 6238 quy dinh va la thu DUY NHAT moi ung dung xac thuc ho tro.
            // Diem yeu va cham cua SHA-1 khong anh huong HMAC (can khoa bi mat), nen tat canh bao CA5350.
            byte[] bam;
#pragma warning disable CA5350
            using (HMACSHA1 hmac = new HMACSHA1(biMat))
            {
                bam = hmac.ComputeHash(boDem);
            }
#pragma warning restore CA5350

            // Cat dong (dynamic truncation): 4 bit cuoi chon vi tri, lay 31 bit tu do.
            int viTri = bam[bam.Length - 1] & 0x0F;
            int soNhiPhan = ((bam[viTri] & 0x7F) << 24)
                | (bam[viTri + 1] << 16)
                | (bam[viTri + 2] << 8)
                | bam[viTri + 3];

            int ma = soNhiPhan % 1000000;
            return ma.ToString("D6", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Kiem ma nguoi dung nhap. Tra buoc khop (de chong dung lai), hoac -1 neu sai.
        /// buocDaDungCuoi: buoc cua lan dang nhap thanh cong gan nhat — ma cua buoc do va cac buoc
        /// truoc KHONG duoc chap nhan nua (ke dung trom ma vua nhap trong 30 giay khong dung lai duoc).
        /// </summary>
        public static long Kiem(byte[] biMat, string? maNhap, DateTimeOffset luc, long buocDaDungCuoi)
        {
            string? ma = ChuanHoaMa(maNhap);
            if (ma == null)
            {
                return -1;
            }

            long hienTai = BuocHienTai(luc);
            for (long lech = -BuocLechChoPhep; lech <= BuocLechChoPhep; lech++)
            {
                long buoc = hienTai + lech;
                if (buoc <= buocDaDungCuoi)
                {
                    continue;
                }

                // So sanh thoi gian hang so: khong lo ma dung bao nhieu chu so dau qua do tre.
                byte[] a = Encoding.ASCII.GetBytes(TinhMa(biMat, buoc));
                byte[] b = Encoding.ASCII.GetBytes(ma);
                if (CryptographicOperations.FixedTimeEquals(a, b))
                {
                    return buoc;
                }
            }

            return -1;
        }

        /// <summary>Bo khoang trang / gach ngang ("123 456"); khong dung 6 chu so thi null.</summary>
        public static string? ChuanHoaMa(string? maNhap)
        {
            if (maNhap == null)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            foreach (char c in maNhap)
            {
                if (c == ' ' || c == '-')
                {
                    continue;
                }

                if (c < '0' || c > '9')
                {
                    return null;
                }

                sb.Append(c);
            }

            return sb.Length == SoChuSo ? sb.ToString() : null;
        }

        /// <summary>
        /// Link otpauth:// de ung dung xac thuc quet (frontend ve thanh ma QR). Dinh dang theo
        /// "Key Uri Format" cua Google Authenticator.
        /// </summary>
        public static string TaoLinkOtpAuth(string nhaPhatHanh, string tenTaiKhoan, byte[] biMat)
        {
            string nhan = Uri.EscapeDataString(nhaPhatHanh) + ":" + Uri.EscapeDataString(tenTaiKhoan);
            return "otpauth://totp/" + nhan
                + "?secret=" + Base32.Ma(biMat)
                + "&issuer=" + Uri.EscapeDataString(nhaPhatHanh)
                + "&algorithm=SHA1&digits=" + SoChuSo + "&period=" + GiayMoiBuoc;
        }
    }

    /// <summary>Base32 (RFC 4648, khong dau '='): dang bi mat TOTP ma ung dung xac thuc nhan khi nhap tay.</summary>
    public static class Base32
    {
        private const string BangChu = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Ma(byte[] duLieu)
        {
            if (duLieu == null)
            {
                throw new ArgumentNullException(nameof(duLieu));
            }

            StringBuilder sb = new StringBuilder();
            int boDem = 0;
            int soBit = 0;
            foreach (byte b in duLieu)
            {
                boDem = (boDem << 8) | b;
                soBit += 8;
                while (soBit >= 5)
                {
                    sb.Append(BangChu[(boDem >> (soBit - 5)) & 31]);
                    soBit -= 5;
                }
            }

            if (soBit > 0)
            {
                sb.Append(BangChu[(boDem << (5 - soBit)) & 31]);
            }

            return sb.ToString();
        }

        public static byte[] Giai(string chuoi)
        {
            if (chuoi == null)
            {
                throw new ArgumentNullException(nameof(chuoi));
            }

            string sach = chuoi.Trim().TrimEnd('=').ToUpperInvariant();
            byte[] ketQua = new byte[sach.Length * 5 / 8];
            int boDem = 0;
            int soBit = 0;
            int viTri = 0;
            foreach (char c in sach)
            {
                int giaTri = BangChu.IndexOf(c);
                if (giaTri < 0)
                {
                    throw new FormatException("Ky tu base32 khong hop le: " + c);
                }

                boDem = (boDem << 5) | giaTri;
                soBit += 5;
                if (soBit >= 8)
                {
                    ketQua[viTri] = (byte)((boDem >> (soBit - 8)) & 0xFF);
                    viTri++;
                    soBit -= 8;
                }
            }

            return ketQua;
        }
    }
}
