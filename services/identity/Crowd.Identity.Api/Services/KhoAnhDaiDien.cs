using System;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Microsoft.Extensions.Options;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Kho anh dai dien: bucket RIENG "avatars" (private), tach khoi giay phep KYB. Cung thong tin
    /// ket noi voi ObjectStorage cua identity, chi doi bucket. Xem anh bang link ky (storage.view_link_ttl).
    /// </summary>
    public sealed class KhoAnhDaiDien
    {
        public const string BucketMacDinh = "avatars";

        public KhoAnhDaiDien(IOptions<ObjectStorageOptions> options, ISettings settings)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ObjectStorageOptions goc = options.Value;
            ObjectStorageOptions rieng = new ObjectStorageOptions
            {
                ServiceUrl = goc.ServiceUrl,
                PublicUrl = goc.PublicUrl,
                AccessKey = goc.AccessKey,
                SecretKey = goc.SecretKey,
                Bucket = BucketMacDinh,
            };
            Kho = new S3ObjectStorage(Options.Create(rieng), settings);
        }

        public IObjectStorage Kho { get; }
    }
}
