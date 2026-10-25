using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Import;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Services
{
    /// <summary>Ket qua tai mot lo tu nguon ngoai.</summary>
    public sealed class KetQuaNhapNgoai
    {
        /// <summary>Manifest {"file": khoa MinIO, "name": ten goc} cho cac file tai duoc.</summary>
        public required JsonArray Dong { get; init; }

        /// <summary>Loi tung muc (khong tai duoc, qua lon, dia chi noi bo...).</summary>
        public required List<string> Loi { get; init; }

        public required int SoBoQua { get; init; }

        /// <summary>Thong so nguon da BO bi mat — luu lai de xem lich su.</summary>
        public required string SpecKhongBiMat { get; init; }
    }

    /// <summary>
    /// Nhap du lieu tu nguon ngoai (FB-11): danh sach URL, bucket S3 (AWS / MinIO), Google Drive cong khai.
    ///
    ///   API (DatasetService) kiem thong so, ma hoa bi mat S3, tao lo Pending.
    ///   Worker (DatasetIngestor) goi TaiVeAsync: liet ke → tai tung file qua cua chong SSRF (ChanSsrf) vao
    ///   tep tam co gioi han kich thuoc / thoi gian → day vao MinIO {du an}/import/{lo}/... → tra manifest.
    ///   Sau do lo nap y het manifest thuong (kiem loai THAT theo noi dung, ffprobe, cat doan, chong trung).
    /// Chi du an anh / am thanh / video — text / cap van ban dung manifest JSON.
    /// </summary>
    public sealed class NhapNguonNgoaiService
    {
        private readonly IObjectStorage _storage;
        private readonly IHttpClientFactory _http;
        private readonly BiMatNguon _biMat;
        private readonly NhapNgoaiOptions _options;
        private readonly ISettings _settings;
        private readonly ILogger<NhapNguonNgoaiService> _logger;

        public NhapNguonNgoaiService(
            IObjectStorage storage,
            IHttpClientFactory http,
            BiMatNguon biMat,
            IOptions<NhapNgoaiOptions> options,
            ISettings settings,
            ILogger<NhapNguonNgoaiService> logger)
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (biMat == null)
            {
                throw new ArgumentNullException(nameof(biMat));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _storage = storage;
            _http = http;
            _biMat = biMat;
            _options = options.Value;
            _settings = settings;
            _logger = logger;
        }

        public const string TenHttpClient = "nhap-nguon-ngoai";

        // =====================================================================
        // API: kiem thong so → spec JSON
        // =====================================================================

        /// <summary>Kiem request, tra (nguon, spec JSON da ma hoa bi mat). Sai → InvalidValueException.</summary>
        public KeyValuePair<string, string> TaoSpec(ImportDatasetRequest body, LabelingProject duAn)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (duAn.Modality != Modalities.Image && duAn.Modality != Modalities.Audio && duAn.Modality != Modalities.Video)
            {
                throw new InvalidValueException("nhap_ngoai_chi_cho_file", "Nhập từ nguồn ngoài chỉ cho dự án ảnh / âm thanh / video. Văn bản dùng manifest JSON.");
            }

            string nguon = (body.Source ?? string.Empty).Trim().ToLowerInvariant();
            int toiDa = _settings.SoNguyen(SettingKeys.DatasetImportMaxFiles);
            JsonObject spec = new JsonObject();
            spec["source"] = nguon;

            if (nguon == "urls")
            {
                List<string> urls = (body.Urls ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (urls.Count == 0 || urls.Count > toiDa)
                {
                    throw new InvalidValueException("thieu_url", "Cần 1–" + toiDa + " URL.");
                }

                foreach (string u in urls)
                {
                    KiemUrl(u);
                }

                spec["urls"] = new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
            }
            else if (nguon == "s3")
            {
                ImportS3Request? s3 = body.S3;
                if (s3 == null || string.IsNullOrWhiteSpace(s3.Bucket) || string.IsNullOrWhiteSpace(s3.AccessKey) || string.IsNullOrWhiteSpace(s3.SecretKey))
                {
                    throw new InvalidValueException("thieu_thong_so_s3", "S3 cần bucket, accessKey, secretKey.");
                }

                if (!string.IsNullOrWhiteSpace(s3.Endpoint))
                {
                    KiemUrl(s3.Endpoint);
                }

                spec["endpoint"] = string.IsNullOrWhiteSpace(s3.Endpoint) ? null : s3.Endpoint.Trim();
                spec["region"] = string.IsNullOrWhiteSpace(s3.Region) ? null : s3.Region.Trim();
                spec["bucket"] = s3.Bucket.Trim();
                spec["prefix"] = s3.Prefix ?? string.Empty;
                spec["accessKey"] = s3.AccessKey.Trim();
                spec["secretCipher"] = _biMat.MaHoa(s3.SecretKey);
            }
            else if (nguon == "gdrive")
            {
                List<string> links = (body.GoogleDrive == null || body.GoogleDrive.Links == null ? new List<string>() : body.GoogleDrive.Links.ToList())
                    .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
                if (links.Count == 0 || links.Count > toiDa)
                {
                    throw new InvalidValueException("thieu_link_drive", "Cần 1–" + toiDa + " link Google Drive.");
                }

                foreach (string l in links)
                {
                    bool laThuMuc = GoogleDrive.MaThuMuc(l) != null;
                    if (GoogleDrive.MaFile(l) == null && !laThuMuc)
                    {
                        throw new InvalidValueException("link_drive_khong_hop_le", "Không nhận ra link Google Drive: " + l);
                    }

                    if (laThuMuc && string.IsNullOrWhiteSpace(_options.GoogleApiKey))
                    {
                        throw new InvalidValueException("chua_cau_hinh_google_drive", "Thư mục Drive cần khoá API Google (DatasetImport:GoogleApiKey) — dán link từng file thay thế.");
                    }
                }

                spec["links"] = new JsonArray(links.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
            }
            else
            {
                throw new InvalidValueException("nguon_khong_ho_tro", "Nguồn: urls | s3 | gdrive.");
            }

            return new KeyValuePair<string, string>(nguon, spec.ToJsonString());
        }

        // =====================================================================
        // WORKER: tai ve MinIO → manifest
        // =====================================================================

        public async Task<KetQuaNhapNgoai> TaiVeAsync(Dataset lo, LabelingProject duAn, CancellationToken ct)
        {
            if (lo == null)
            {
                throw new ArgumentNullException(nameof(lo));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            JsonObject spec = (JsonObject)JsonNode.Parse(lo.ImportSpec!)!;
            string nguon = (string)spec["source"]!;
            int toiDa = _settings.SoNguyen(SettingKeys.DatasetImportMaxFiles);
            long toiDaByte = duAn.Modality == Modalities.Image
                ? _settings.SoLon(SettingKeys.DatasetImageMaxBytes)
                : _settings.SoLon(SettingKeys.DatasetImportFileMaxBytes);
            TimeSpan gio = _settings.ThoiGian(SettingKeys.DatasetImportTimeout);

            JsonArray dong = new JsonArray();
            List<string> loi = new List<string>();
            int boQua = 0;
            HttpClient http = _http.CreateClient(TenHttpClient);
            TaiVeAnToan taiVe = new TaiVeAnToan(http);

            List<MucNguon> muc;
            NguonS3? s3 = null;
            try
            {
                if (nguon == "urls")
                {
                    muc = ((JsonArray)spec["urls"]!).Select(u => new MucNguon { Ten = TenTuUrl((string)u!), Url = (string)u! }).ToList();
                }
                else if (nguon == "s3")
                {
                    s3 = new NguonS3(new ThongSoS3
                    {
                        Endpoint = (string?)spec["endpoint"],
                        Region = (string?)spec["region"],
                        Bucket = (string)spec["bucket"]!,
                        Prefix = (string?)spec["prefix"],
                        AccessKey = (string)spec["accessKey"]!,
                        SecretKey = _biMat.GiaiMa((string)spec["secretCipher"]!),
                    }, _options.AllowedPrivateHosts);
                    muc = await s3.LietKeAsync(toiDa, ct);
                }
                else
                {
                    muc = new List<MucNguon>();
                    foreach (JsonNode? l in (JsonArray)spec["links"]!)
                    {
                        string link = (string)l!;
                        string? thuMuc = GoogleDrive.MaThuMuc(link);
                        if (thuMuc != null)
                        {
                            muc.AddRange(await GoogleDrive.LietKeThuMucAsync(http, thuMuc, _options.GoogleApiKey, toiDa - muc.Count, ct));
                        }
                        else
                        {
                            string id = GoogleDrive.MaFile(link)!;
                            muc.Add(new MucNguon { Ten = id, Url = GoogleDrive.LinkTai(id, _options.GoogleApiKey) });
                        }
                    }
                }
            }
            catch (NguonNgoaiException ex)
            {
                if (s3 != null)
                {
                    s3.Dispose();
                }

                loi.Add("nguồn: " + ex.Message);
                return new KetQuaNhapNgoai { Dong = dong, Loi = loi, SoBoQua = 1, SpecKhongBiMat = BoBiMat(spec) };
            }

            try
            {
                if (muc.Count > toiDa)
                {
                    boQua += muc.Count - toiDa;
                    loi.Add("nguồn: chỉ lấy " + toiDa + " file đầu (dataset.import_max_files).");
                    muc = muc.Take(toiDa).ToList();
                }

                for (int i = 0; i < muc.Count; i++)
                {
                    MucNguon m = muc[i];
                    try
                    {
                        using (TepTaiVe tep = s3 != null && m.KhoaS3 != null
                            ? await s3.TaiAsync(m, toiDaByte, ct)
                            : await taiVe.TaiAsync(m.Url!, toiDaByte, gio, null, ct))
                        {
                            string ten = string.IsNullOrWhiteSpace(tep.Ten) || tep.Ten == "file" ? m.Ten : tep.Ten;
                            string khoa = duAn.Id + "/import/" + lo.Id + "/" + i.ToString("D5", CultureInfo.InvariantCulture) + DuoiAnToan(ten);
                            await _storage.LuuTepAsync(khoa, tep.DuongDan, tep.ContentType ?? "application/octet-stream", ct);

                            JsonObject o = new JsonObject();
                            o["file"] = khoa;
                            o["name"] = ten.Length > 400 ? ten.Substring(0, 400) : ten;
                            dong.Add(o);
                        }
                    }
                    catch (NguonNgoaiException ex)
                    {
                        boQua++;
                        if (loi.Count < _settings.SoNguyen(SettingKeys.DatasetErrorSampleCount))
                        {
                            loi.Add("nguồn " + m.Ten + ": " + ex.Message);
                        }
                    }
                }
            }
            finally
            {
                if (s3 != null)
                {
                    s3.Dispose();
                }
            }

            _logger.LogInformation("Lo {DatasetId} nguon {Nguon}: tai {SoFile} file, bo qua {BoQua}", lo.Id, nguon, dong.Count, boQua);
            return new KetQuaNhapNgoai { Dong = dong, Loi = loi, SoBoQua = boQua, SpecKhongBiMat = BoBiMat(spec) };
        }

        private static void KiemUrl(string u)
        {
            try
            {
                TaiVeAnToan.KiemUrl(u);
            }
            catch (NguonNgoaiException ex)
            {
                throw new InvalidValueException(ex.Code, ex.Message + " (" + u + ")");
            }
        }

        private static string BoBiMat(JsonObject spec)
        {
            JsonObject sach = (JsonObject)spec.DeepClone();
            sach.Remove("secretCipher");
            return sach.ToJsonString();
        }

        private static string TenTuUrl(string url)
        {
            Uri? u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u))
            {
                return "file";
            }

            string ten = Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath));
            return ten.Length == 0 ? "file" : ten;
        }

        /// <summary>Duoi file chi gom chu / so, toi da 8 ky tu (khoa MinIO khong mang ky tu la tu ten nguoi dung).</summary>
        private static string DuoiAnToan(string ten)
        {
            string duoi = Path.GetExtension(ten ?? string.Empty).ToLowerInvariant();
            if (duoi.Length < 2 || duoi.Length > 8)
            {
                return string.Empty;
            }

            for (int i = 1; i < duoi.Length; i++)
            {
                if (!char.IsAsciiLetterOrDigit(duoi[i]))
                {
                    return string.Empty;
                }
            }

            return duoi;
        }
    }
}
