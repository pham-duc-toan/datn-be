using System;
using System.Security.Cryptography;
using System.Text;
using Crowd.BuildingBlocks.Storage;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Ma hoa bi mat S3 cua doanh nghiep (FB-11) trong thoi gian ngan cho worker tai ve: lo nhap nam o
    /// database tu luc tao toi luc worker xu ly xong, roi bi XOA. Khoa AES-256-GCM tach (HKDF, muc dich
    /// rieng) tu khoa MediaLinks:GrantKey san co cua service — khong them bi mat moi phai quan ly.
    /// </summary>
    public sealed class BiMatNguon
    {
        private const int DoDaiNonce = 12;
        private const int DoDaiTag = 16;

        private readonly byte[] _khoa;

        public BiMatNguon(IOptions<MediaLinksOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            byte[] goc;
            try
            {
                goc = Convert.FromBase64String(options.Value.GrantKey ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("MediaLinks:GrantKey phai la base64 32 byte.");
            }

            if (goc.Length != 32)
            {
                throw new InvalidOperationException("MediaLinks:GrantKey phai la base64 32 byte.");
            }

            _khoa = HKDF.DeriveKey(HashAlgorithmName.SHA256, goc, 32, null, Encoding.ASCII.GetBytes("crowd-project/import-secret"));
        }

        public string MaHoa(string banRo)
        {
            byte[] du = Encoding.UTF8.GetBytes(banRo ?? string.Empty);
            byte[] nonce = RandomNumberGenerator.GetBytes(DoDaiNonce);
            byte[] banMa = new byte[du.Length];
            byte[] tag = new byte[DoDaiTag];
            using (AesGcm aes = new AesGcm(_khoa, DoDaiTag))
            {
                aes.Encrypt(nonce, du, banMa, tag);
            }

            byte[] tatCa = new byte[DoDaiNonce + banMa.Length + DoDaiTag];
            Buffer.BlockCopy(nonce, 0, tatCa, 0, DoDaiNonce);
            Buffer.BlockCopy(banMa, 0, tatCa, DoDaiNonce, banMa.Length);
            Buffer.BlockCopy(tag, 0, tatCa, DoDaiNonce + banMa.Length, DoDaiTag);
            return Convert.ToBase64String(tatCa);
        }

        public string GiaiMa(string banMaBase64)
        {
            byte[] tatCa = Convert.FromBase64String(banMaBase64);
            int doDai = tatCa.Length - DoDaiNonce - DoDaiTag;
            if (doDai < 0)
            {
                throw new CryptographicException("Ban ma qua ngan.");
            }

            byte[] nonce = new byte[DoDaiNonce];
            byte[] banMa = new byte[doDai];
            byte[] tag = new byte[DoDaiTag];
            Buffer.BlockCopy(tatCa, 0, nonce, 0, DoDaiNonce);
            Buffer.BlockCopy(tatCa, DoDaiNonce, banMa, 0, doDai);
            Buffer.BlockCopy(tatCa, DoDaiNonce + doDai, tag, 0, DoDaiTag);
            byte[] banRo = new byte[doDai];
            using (AesGcm aes = new AesGcm(_khoa, DoDaiTag))
            {
                aes.Decrypt(nonce, banMa, tag, banRo);
            }

            return Encoding.UTF8.GetString(banRo);
        }
    }
}
