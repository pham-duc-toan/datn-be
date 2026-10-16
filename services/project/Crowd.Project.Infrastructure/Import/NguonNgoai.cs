using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Crowd.Project.Infrastructure.Import
{
    /// <summary>Mot muc can tai (URL, hoac doi tuong S3).</summary>
    public sealed class MucNguon
    {
        public required string Ten { get; init; }

        /// <summary>URL (nguon urls / Google Drive) — null voi S3.</summary>
        public string? Url { get; init; }

        /// <summary>Khoa doi tuong S3 — null voi URL.</summary>
        public string? KhoaS3 { get; init; }

        public long? KichThuoc { get; init; }
    }

    /// <summary>Thong so S3 nguon (AWS S3 / MinIO / bat ky dich vu tuong thich S3).</summary>
    public sealed class ThongSoS3
    {
        /// <summary>Rong = AWS S3 that (theo Region).</summary>
        public string? Endpoint { get; init; }

        public string? Region { get; init; }

        public required string Bucket { get; init; }

        public string? Prefix { get; init; }

        public required string AccessKey { get; init; }

        public required string SecretKey { get; init; }
    }

    /// <summary>
    /// Doc tu bucket S3 cua doanh nghiep (FB-11): liet ke theo prefix roi tai tung doi tuong. Ket noi qua
    /// handler ChanSsrf (endpoint do nguoi dung nhap cung la mot URL).
    /// </summary>
    public sealed class NguonS3 : IDisposable
    {
        private readonly AmazonS3Client _client;
        private readonly string _bucket;
        private readonly string _prefix;

        public NguonS3(ThongSoS3 s, IReadOnlyCollection<string> hostChoPhep)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            AmazonS3Config cfg = new AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(s.Endpoint))
            {
                Uri u = TaiVeAnToan.KiemUrl(s.Endpoint);
                cfg.ServiceURL = u.ToString().TrimEnd('/');
                cfg.ForcePathStyle = true;
                if (!string.IsNullOrWhiteSpace(s.Region))
                {
                    cfg.AuthenticationRegion = s.Region;
                }
            }
            else
            {
                cfg.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(string.IsNullOrWhiteSpace(s.Region) ? "us-east-1" : s.Region);
            }

            cfg.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            cfg.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
            cfg.HttpClientFactory = new HttpClientChanSsrf(hostChoPhep);
            cfg.MaxErrorRetry = 1;
            cfg.Timeout = TimeSpan.FromSeconds(60);

            _client = new AmazonS3Client(new BasicAWSCredentials(s.AccessKey, s.SecretKey), cfg);
            _bucket = s.Bucket;
            _prefix = s.Prefix ?? string.Empty;
        }

        /// <summary>Liet ke toi da toiDa doi tuong (bo "thu muc" rong).</summary>
        public async Task<List<MucNguon>> LietKeAsync(int toiDa, CancellationToken ct)
        {
            List<MucNguon> ds = new List<MucNguon>();
            ListObjectsV2Request req = new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix, MaxKeys = 1000 };
            try
            {
                while (ds.Count < toiDa)
                {
                    ListObjectsV2Response r = await _client.ListObjectsV2Async(req, ct).ConfigureAwait(false);
                    if (r.S3Objects != null)
                    {
                        foreach (S3Object o in r.S3Objects)
                        {
                            if (o.Key.EndsWith('/') || o.Size == 0)
                            {
                                continue;
                            }

                            ds.Add(new MucNguon { Ten = Path.GetFileName(o.Key), KhoaS3 = o.Key, KichThuoc = o.Size });
                            if (ds.Count >= toiDa)
                            {
                                break;
                            }
                        }
                    }

                    if (r.IsTruncated != true || string.IsNullOrEmpty(r.NextContinuationToken))
                    {
                        break;
                    }

                    req.ContinuationToken = r.NextContinuationToken;
                }
            }
            catch (AmazonS3Exception ex)
            {
                throw new NguonNgoaiException("s3_tu_choi", "S3 tu choi: " + ex.ErrorCode + " " + ex.Message);
            }
            catch (HttpRequestException ex) when (ex.InnerException is NguonNgoaiException)
            {
                throw (NguonNgoaiException)ex.InnerException;
            }
            catch (AmazonClientException ex) when (LoiSsrfBenTrong(ex) != null)
            {
                throw LoiSsrfBenTrong(ex)!;
            }

            return ds;
        }

        public async Task<TepTaiVe> TaiAsync(MucNguon muc, long toiDaByte, CancellationToken ct)
        {
            if (muc == null || muc.KhoaS3 == null)
            {
                throw new ArgumentException("muc khong phai doi tuong S3", nameof(muc));
            }

            if (muc.KichThuoc.HasValue && muc.KichThuoc.Value > toiDaByte)
            {
                throw new NguonNgoaiException("file_qua_lon", "File " + muc.KichThuoc.Value + " byte, toi da " + toiDaByte + ".");
            }

            string tam = Path.Combine(Path.GetTempPath(), "crowd-import-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (GetObjectResponse r = await _client.GetObjectAsync(_bucket, muc.KhoaS3, ct).ConfigureAwait(false))
                using (FileStream ra = new FileStream(tam, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] dem = new byte[81920];
                    long daDoc = 0;
                    int n;
                    while ((n = await r.ResponseStream.ReadAsync(dem.AsMemory(), ct).ConfigureAwait(false)) > 0)
                    {
                        daDoc += n;
                        if (daDoc > toiDaByte)
                        {
                            throw new NguonNgoaiException("file_qua_lon", "File vuot " + toiDaByte + " byte.");
                        }

                        await ra.WriteAsync(dem.AsMemory(0, n), ct).ConfigureAwait(false);
                    }

                    return new TepTaiVe(tam, daDoc, r.Headers.ContentType, muc.Ten);
                }
            }
            catch (AmazonS3Exception ex)
            {
                File.Delete(tam);
                throw new NguonNgoaiException("s3_tu_choi", "S3 tu choi: " + ex.ErrorCode);
            }
            catch
            {
                File.Delete(tam);
                throw;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        /// <summary>AWSSDK boc loi ket noi trong nhieu lop: tim NguonNgoaiException (chan SSRF) ben trong.</summary>
        private static NguonNgoaiException? LoiSsrfBenTrong(Exception ex)
        {
            Exception? e = ex;
            while (e != null)
            {
                NguonNgoaiException? n = e as NguonNgoaiException;
                if (n != null)
                {
                    return n;
                }

                e = e.InnerException;
            }

            return null;
        }

        /// <summary>AWSSDK dung HttpClient tao tu day — moi ket noi di qua ConnectCallback chong SSRF.</summary>
        private sealed class HttpClientChanSsrf : HttpClientFactory
        {
            private readonly IReadOnlyCollection<string> _hostChoPhep;

            public HttpClientChanSsrf(IReadOnlyCollection<string> hostChoPhep)
            {
                _hostChoPhep = hostChoPhep;
            }

            public override HttpClient CreateHttpClient(IClientConfig clientConfig)
            {
                return new HttpClient(ChanSsrf.TaoHandler(_hostChoPhep), true);
            }
        }
    }

    /// <summary>
    /// Google Drive cong khai (FB-11):
    ///   - link FILE (".../file/d/{id}/view", "...open?id={id}", "...uc?id={id}") → tai qua Drive API v3
    ///     (alt=media, can khoa API) hoac duong uc?export=download (khong khoa, file nho);
    ///   - link THU MUC (".../drive/folders/{id}") → liet ke bang Drive API v3 — BAT BUOC co GoogleApiKey.
    /// Moi lan tai deu qua TaiVeAnToan (googleapis.com la dia chi cong khai — cua chong SSRF van ap).
    /// </summary>
    public static class GoogleDrive
    {
        private static readonly Regex File1 = new Regex(@"/file/d/([A-Za-z0-9_-]{10,})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex FileId = new Regex(@"[?&]id=([A-Za-z0-9_-]{10,})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Folder = new Regex(@"/folders/([A-Za-z0-9_-]{10,})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        /// <summary>Ma file tu link chia se; khong nhan ra → null.</summary>
        public static string? MaFile(string link)
        {
            if (string.IsNullOrWhiteSpace(link) || !link.Contains("google.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Match m = File1.Match(link);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }

            m = FileId.Match(link);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string? MaThuMuc(string link)
        {
            if (string.IsNullOrWhiteSpace(link) || !link.Contains("google.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Match m = Folder.Match(link);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string LinkTai(string maFile, string? khoaApi)
        {
            if (string.IsNullOrWhiteSpace(khoaApi))
            {
                return "https://drive.google.com/uc?export=download&id=" + Uri.EscapeDataString(maFile);
            }

            return "https://www.googleapis.com/drive/v3/files/" + Uri.EscapeDataString(maFile) + "?alt=media&key=" + Uri.EscapeDataString(khoaApi);
        }

        /// <summary>Liet ke file (khong phai thu muc con) trong thu muc cong khai.</summary>
        public static async Task<List<MucNguon>> LietKeThuMucAsync(HttpClient http, string maThuMuc, string khoaApi, int toiDa, CancellationToken ct)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            List<MucNguon> ds = new List<MucNguon>();
            string? trang = null;
            do
            {
                string url = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString("'" + maThuMuc + "' in parents and trashed = false")
                    + "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,size,mimeType)")
                    + "&pageSize=1000&key=" + Uri.EscapeDataString(khoaApi)
                    + (trang == null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(trang));
                string body;
                try
                {
                    body = await http.GetStringAsync(url, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new NguonNgoaiException("drive_tu_choi", "Google Drive tu choi liet ke thu muc: " + ex.Message);
                }

                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement goc = doc.RootElement;
                    JsonElement files;
                    if (goc.TryGetProperty("files", out files))
                    {
                        foreach (JsonElement f in files.EnumerateArray())
                        {
                            JsonElement mt;
                            JsonElement sz;
                            JsonElement n;
                            long so;
                            string loai = f.TryGetProperty("mimeType", out mt) ? mt.GetString() ?? string.Empty : string.Empty;
                            if (loai == "application/vnd.google-apps.folder")
                            {
                                continue;
                            }

                            string id = f.GetProperty("id").GetString()!;
                            long? co = null;
                            if (f.TryGetProperty("size", out sz) && long.TryParse(sz.GetString(), out so))
                            {
                                co = so;
                            }

                            string ten = f.TryGetProperty("name", out n) ? n.GetString() ?? id : id;
                            ds.Add(new MucNguon { Ten = ten, Url = LinkTai(id, khoaApi), KichThuoc = co });
                            if (ds.Count >= toiDa)
                            {
                                return ds;
                            }
                        }
                    }

                    JsonElement tk;
                    trang = goc.TryGetProperty("nextPageToken", out tk) ? tk.GetString() : null;
                }
            }
            while (trang != null);

            return ds;
        }
    }
}
