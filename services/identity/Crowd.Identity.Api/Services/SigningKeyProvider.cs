using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Giu cap khoa RSA dung de ky token.
    ///
    /// VI SAO RSA (RS256) CHU KHONG PHAI MOT SECRET CHUNG (HS256):
    ///
    /// Voi HS256, cung mot secret vua KY vua KIEM. 16 service can kiem token thi
    /// ca 16 phai giu secret do — va service nao giu secret cung TU PHAT TOKEN
    /// duoc. Mot service bi xam nhap la gia mao duoc admin.
    ///
    /// Voi RS256, khoa RIENG chi nam o day, dung de ky. Khoa CONG KHAI phat cho
    /// ai cung duoc, chi dung de kiem. Lo khoa cong khai khong sao ca.
    /// </summary>
    public sealed class SigningKeyProvider : IDisposable
    {
        private readonly RSA _rsa;

        public SigningKeyProvider(RSA rsa)
        {
            if (rsa == null)
            {
                throw new ArgumentNullException(nameof(rsa));
            }

            _rsa = rsa;

            RsaSecurityKey khoa = new RsaSecurityKey(rsa);

            // kid = dau van tay cua khoa cong khai. On dinh qua cac lan khoi dong
            // lai (cung khoa thi cung kid), va doi khi xoay sang khoa moi — nho vay
            // service kiem token biet phai tai lai JWKS khi gap kid la.
            khoa.KeyId = Base64UrlEncoder.Encode(khoa.ComputeJwkThumbprint());

            KhoaKy = khoa;
            KeyId = khoa.KeyId;

            // CHI xuat phan cong khai. Neu xuat ca phan rieng thi JWKS se lo khoa
            // ky ra Internet — va ai cung phat duoc token cho minh.
            RSAParameters phanCongKhai = rsa.ExportParameters(includePrivateParameters: false);
            Modulus = Base64UrlEncoder.Encode(phanCongKhai.Modulus);
            Exponent = Base64UrlEncoder.Encode(phanCongKhai.Exponent);
        }

        /// <summary>Khoa dung de KY. Chi TokenIssuer dung.</summary>
        public RsaSecurityKey KhoaKy { get; }

        public string KeyId { get; }

        /// <summary>Tham so "n" cua JWK — phan cong khai.</summary>
        public string Modulus { get; }

        /// <summary>Tham so "e" cua JWK — phan cong khai.</summary>
        public string Exponent { get; }

        /// <summary>
        /// Nap khoa tu file; chua co thi sinh moi va ghi xuong.
        ///
        /// Phai LUU chu khong sinh moi moi lan khoi dong: sinh moi thi khoi dong
        /// lai la moi token dang luu hanh mat hieu luc, va hai ban sao identity-svc
        /// se ky bang hai khoa khac nhau.
        ///
        /// Production KHONG duoc dung cach nay — khoa phai lay tu secret manager.
        /// </summary>
        public static SigningKeyProvider NapHoacSinh(string duongDan, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(duongDan))
            {
                throw new ArgumentException("duong dan khong duoc rong", nameof(duongDan));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            RSA rsa = RSA.Create();

            if (File.Exists(duongDan))
            {
                rsa.ImportFromPem(File.ReadAllText(duongDan));
                logger.LogInformation("Da nap khoa ky tu {DuongDan}", duongDan);
            }
            else
            {
                rsa.KeySize = 2048;

                string? thuMuc = Path.GetDirectoryName(duongDan);
                if (!string.IsNullOrEmpty(thuMuc))
                {
                    Directory.CreateDirectory(thuMuc);
                }

                File.WriteAllText(duongDan, rsa.ExportPkcs8PrivateKeyPem());

                logger.LogWarning(
                    "Chua co khoa ky — vua SINH MOI va ghi vao {DuongDan}. " +
                    "Chi chap nhan o moi truong dev.",
                    duongDan);
            }

            return new SigningKeyProvider(rsa);
        }

        public void Dispose()
        {
            _rsa.Dispose();
        }
    }
}
