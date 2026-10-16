using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Crowd.Identity.Api.Services
{
    /// <summary>Cau hinh "IdentityData": khoa goc 32 byte (base64) — dev trong appsettings, demo trong .env.</summary>
    public sealed class KhoaDuLieuOptions
    {
        public const string SectionName = "IdentityData";

        public string Key { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bao ve du lieu nhay cam identity phai GIU LAI duoc (khac mat khau — chi can bam mot chieu):
    ///   - bi mat TOTP (FC-04): server phai doc lai de tinh ma → MA HOA AES-256-GCM. Lo database ma
    ///     khong lo khoa thi ke tan cong khong sinh duoc ma 2FA;
    ///   - so giay to tuy than (xac minh danh tinh): chi can so khop "giay to nay da dung cho tai
    ///     khoan khac chua" → HMAC-SHA256 co khoa. Khong dung SHA-256 tran: CCCD chi 12 chu so
    ///     (10^12 kha nang), bam tran thi do nguoc trong vai gio.
    ///
    /// Hai khoa con tach tu MOT khoa goc bang HKDF (moi muc dich mot khoa, lo mot cai khong keo cai kia).
    /// </summary>
    public sealed class KhoaDuLieu
    {
        private const int DoDaiNonce = 12;
        private const int DoDaiTag = 16;

        private readonly byte[] _khoaMaHoa;
        private readonly byte[] _khoaBam;

        public KhoaDuLieu(IOptions<KhoaDuLieuOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            byte[] goc;
            try
            {
                goc = Convert.FromBase64String(options.Value.Key ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("IdentityData:Key phai la base64 cua 32 byte.");
            }

            if (goc.Length != 32)
            {
                throw new InvalidOperationException(
                    "Thieu / sai IdentityData:Key (can 32 byte base64) — dev: appsettings.Development.json, demo: IDENTITY_DATA_KEY trong .env.");
            }

            _khoaMaHoa = HKDF.DeriveKey(HashAlgorithmName.SHA256, goc, 32, null, Encoding.ASCII.GetBytes("crowd-identity/totp-secret"));
            _khoaBam = HKDF.DeriveKey(HashAlgorithmName.SHA256, goc, 32, null, Encoding.ASCII.GetBytes("crowd-identity/id-number"));
        }

        /// <summary>Ma hoa → base64(nonce 12 | ban ma | tag 16).</summary>
        public string MaHoa(byte[] banRo)
        {
            if (banRo == null)
            {
                throw new ArgumentNullException(nameof(banRo));
            }

            byte[] nonce = RandomNumberGenerator.GetBytes(DoDaiNonce);
            byte[] banMa = new byte[banRo.Length];
            byte[] tag = new byte[DoDaiTag];
            using (AesGcm aes = new AesGcm(_khoaMaHoa, DoDaiTag))
            {
                aes.Encrypt(nonce, banRo, banMa, tag);
            }

            byte[] tatCa = new byte[DoDaiNonce + banMa.Length + DoDaiTag];
            Buffer.BlockCopy(nonce, 0, tatCa, 0, DoDaiNonce);
            Buffer.BlockCopy(banMa, 0, tatCa, DoDaiNonce, banMa.Length);
            Buffer.BlockCopy(tag, 0, tatCa, DoDaiNonce + banMa.Length, DoDaiTag);
            return Convert.ToBase64String(tatCa);
        }

        /// <summary>Giai ma. Ban ma bi sua (hoac sai khoa) → CryptographicException.</summary>
        public byte[] GiaiMa(string banMaBase64)
        {
            byte[] tatCa = Convert.FromBase64String(banMaBase64);
            if (tatCa.Length < DoDaiNonce + DoDaiTag)
            {
                throw new CryptographicException("Ban ma qua ngan.");
            }

            int doDai = tatCa.Length - DoDaiNonce - DoDaiTag;
            byte[] nonce = new byte[DoDaiNonce];
            byte[] banMa = new byte[doDai];
            byte[] tag = new byte[DoDaiTag];
            Buffer.BlockCopy(tatCa, 0, nonce, 0, DoDaiNonce);
            Buffer.BlockCopy(tatCa, DoDaiNonce, banMa, 0, doDai);
            Buffer.BlockCopy(tatCa, DoDaiNonce + doDai, tag, 0, DoDaiTag);

            byte[] banRo = new byte[doDai];
            using (AesGcm aes = new AesGcm(_khoaMaHoa, DoDaiTag))
            {
                aes.Decrypt(nonce, banMa, tag, banRo);
            }

            return banRo;
        }

        /// <summary>HMAC-SHA256 dang hex cua chuoi da chuan hoa (so giay to).</summary>
        public string BamCoKhoa(string giaTri)
        {
            if (giaTri == null)
            {
                throw new ArgumentNullException(nameof(giaTri));
            }

            using (HMACSHA256 hmac = new HMACSHA256(_khoaBam))
            {
                return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(giaTri)));
            }
        }
    }
}
