using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Crowd.BuildingBlocks.Settings;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Storage
{
    /// <summary>Thong so ket noi MinIO. Doc tu muc "ObjectStorage".</summary>
    public sealed class ObjectStorageOptions
    {
        public const string SectionName = "ObjectStorage";

        /// <summary>Dia chi service dung de doc / ghi file. Vd http://localhost:9000, trong docker: http://minio:9000.</summary>
        public string ServiceUrl { get; set; } = "http://localhost:9000";

        /// <summary>
        /// Dia chi NGUOI DUNG (trinh duyet, cong cu) toi duoc, dung de KY link xem / tai len.
        /// Rong = dung ServiceUrl (chay truc tiep tren may). Trong docker compose service noi
        /// MinIO qua "minio:9000" nhung trinh duyet chi toi duoc "localhost:19000" — chu ky
        /// presigned gan voi host nen phai ky bang dia chi cong khai.
        /// </summary>
        public string PublicUrl { get; set; } = string.Empty;

        public string AccessKey { get; set; } = string.Empty;

        public string SecretKey { get; set; } = string.Empty;

        /// <summary>Bucket PRIVATE (minio-init da `anonymous set none`).</summary>
        public string Bucket { get; set; } = "datasets";

        /// <summary>Tuoi cua link xem anh. S-07: toi da 5 phut.</summary>
        public TimeSpan LinkTtl { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Tuoi cua link UPLOAD (PUT) phat cho chu du an. Dai hon link xem vi file
        /// video lon can thoi gian tai len; van chi phat cho chu du an dang Nhap.
        /// </summary>
        public TimeSpan UploadLinkTtl { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Kho file. Code nghiep vu chi biet interface nay, khong biet MinIO/S3 —
    /// test thay bang ban gia trong bo nho.
    /// </summary>
    public interface IObjectStorage
    {
        Task LuuAsync(string key, byte[] noiDung, string contentType, CancellationToken ct);

        Task XoaAsync(string key, CancellationToken ct);

        /// <summary>
        /// Link xem co han (presigned URL). Ai cam link deu xem duoc cho toi khi
        /// het han, nen han phai ngan va chi phat cho nguoi co quyen.
        /// </summary>
        Task<string> TaoLinkXemAsync(string key);

        /// <summary>
        /// Link UPLOAD co han (presigned PUT): client tai file THANG len kho, khong
        /// di qua service — video vai GB khong lam nghen service.
        /// </summary>
        Task<string> TaoLinkTaiLenAsync(string key);

        /// <summary>
        /// Link xem NOI BO cho tien trinh chay canh service (ffprobe) — ky bang dia chi service
        /// (ServiceUrl), khong phai dia chi cong khai. Khong bao gio tra cho nguoi dung.
        /// </summary>
        Task<string> TaoLinkNoiBoAsync(string key);

        /// <summary>Thong tin file; null neu khong ton tai.</summary>
        Task<ThongTinFile?> ThongTinAsync(string key, CancellationToken ct);

        /// <summary>Mo luong doc file — nguoi goi Dispose. Doc tung khuc, khong nap ca file vao RAM.</summary>
        Task<Stream> MoDocAsync(string key, CancellationToken ct);
    }

    /// <summary>Thong tin co ban cua mot file trong kho.</summary>
    public sealed class ThongTinFile
    {
        public required long SizeBytes { get; init; }

        public string? ContentType { get; init; }
    }

    /// <summary>
    /// Noi MinIO qua giao thuc S3 bang AWSSDK — MinIO noi dung giao thuc S3,
    /// nen doi sang S3 that chi can doi ServiceUrl.
    ///
    /// Dang ky SINGLETON: AmazonS3Client giu pool ket noi HTTP, an toan khi
    /// nhieu luong dung chung.
    /// </summary>
    public sealed class S3ObjectStorage : IObjectStorage, IDisposable
    {
        private readonly ObjectStorageOptions _options;
        private readonly AmazonS3Client _client;

        /// <summary>Chi de KY link cho nguoi dung (PublicUrl); trung _client neu khong cau hinh PublicUrl.</summary>
        private readonly AmazonS3Client _clientCongKhai;

        private readonly string _urlCongKhai;

        /// <summary>Setting he thong (thoi han link) — null khi service khong dang ky (test, cong cu).</summary>
        private readonly ISettings? _settings;

        public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
            : this(options, null)
        {
        }

        public S3ObjectStorage(IOptions<ObjectStorageOptions> options, ISettings? settings)
        {
            _settings = settings;
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Value;

            BasicAWSCredentials khoa = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
            _client = new AmazonS3Client(khoa, TaoCauHinh(_options.ServiceUrl));

            if (string.IsNullOrWhiteSpace(_options.PublicUrl))
            {
                _clientCongKhai = _client;
                _urlCongKhai = _options.ServiceUrl;
            }
            else
            {
                // Ky presigned khong goi mang: client nay khong bao gio ket noi toi PublicUrl.
                _clientCongKhai = new AmazonS3Client(khoa, TaoCauHinh(_options.PublicUrl));
                _urlCongKhai = _options.PublicUrl;
            }
        }

        private static AmazonS3Config TaoCauHinh(string url)
        {
            AmazonS3Config cauHinh = new AmazonS3Config();
            cauHinh.ServiceURL = url;

            // MinIO dung dia chi dang http://host/bucket/key. Mac dinh AWS dung
            // http://bucket.host/key — voi localhost se khong phan giai duoc.
            cauHinh.ForcePathStyle = true;

            // AWSSDK v4 mac dinh gui checksum CRC32 moi request. Chi bat khi bat
            // buoc de khong phu thuoc phien ban MinIO ho tro hay khong.
            cauHinh.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            cauHinh.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
            return cauHinh;
        }

        private Task<string> KyLinkAsync(AmazonS3Client client, string url, string key, HttpVerb verb, TimeSpan han)
        {
            GetPreSignedUrlRequest yeuCau = new GetPreSignedUrlRequest();
            yeuCau.BucketName = _options.Bucket;
            yeuCau.Key = key;
            yeuCau.Verb = verb;
            yeuCau.Expires = DateTime.UtcNow.Add(han);

            // KHONG ky kem Content-Type (link PUT): client (curl, trinh duyet) gui header nao
            // cung duoc. Loai file that duoc worker kiem lai bang noi dung.
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                yeuCau.Protocol = Protocol.HTTP;
            }

            return client.GetPreSignedURLAsync(yeuCau);
        }

        private TimeSpan HanXem()
        {
            return _settings == null ? _options.LinkTtl : _settings.ThoiGian(SettingKeys.StorageViewLinkTtl);
        }

        public async Task LuuAsync(string key, byte[] noiDung, string contentType, CancellationToken ct)
        {
            using (MemoryStream luong = new MemoryStream(noiDung, false))
            {
                PutObjectRequest yeuCau = new PutObjectRequest();
                yeuCau.BucketName = _options.Bucket;
                yeuCau.Key = key;
                yeuCau.InputStream = luong;
                yeuCau.ContentType = contentType;
                yeuCau.AutoCloseStream = false;

                await _client.PutObjectAsync(yeuCau, ct).ConfigureAwait(false);
            }
        }

        public async Task XoaAsync(string key, CancellationToken ct)
        {
            DeleteObjectRequest yeuCau = new DeleteObjectRequest();
            yeuCau.BucketName = _options.Bucket;
            yeuCau.Key = key;

            await _client.DeleteObjectAsync(yeuCau, ct).ConfigureAwait(false);
        }

        public Task<string> TaoLinkXemAsync(string key)
        {
            return KyLinkAsync(_clientCongKhai, _urlCongKhai, key, HttpVerb.GET, HanXem());
        }

        public Task<string> TaoLinkTaiLenAsync(string key)
        {
            TimeSpan han = _settings == null ? _options.UploadLinkTtl : _settings.ThoiGian(SettingKeys.UploadLinkTtl);
            return KyLinkAsync(_clientCongKhai, _urlCongKhai, key, HttpVerb.PUT, han);
        }

        public Task<string> TaoLinkNoiBoAsync(string key)
        {
            return KyLinkAsync(_client, _options.ServiceUrl, key, HttpVerb.GET, HanXem());
        }

        public async Task<ThongTinFile?> ThongTinAsync(string key, CancellationToken ct)
        {
            try
            {
                GetObjectMetadataResponse r = await _client.GetObjectMetadataAsync(_options.Bucket, key, ct).ConfigureAwait(false);
                return new ThongTinFile { SizeBytes = r.ContentLength, ContentType = r.Headers.ContentType };
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<Stream> MoDocAsync(string key, CancellationToken ct)
        {
            GetObjectResponse r = await _client.GetObjectAsync(_options.Bucket, key, ct).ConfigureAwait(false);
            return r.ResponseStream;
        }

        public void Dispose()
        {
            if (!ReferenceEquals(_clientCongKhai, _client))
            {
                _clientCongKhai.Dispose();
            }

            _client.Dispose();
        }
    }
}
