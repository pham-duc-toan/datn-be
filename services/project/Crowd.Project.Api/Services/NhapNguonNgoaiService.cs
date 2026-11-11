using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Import;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Services
{
    /// <summary>Mot file o nguon ngoai da liet ke xong: ten goc + link GET de worker tai.</summary>
    public sealed class MucCanTai
    {
        public required string Ten { get; init; }

        /// <summary>Link tai: URL cong khai, link Drive, hoac link S3 da ky san (het han) — KHONG chua khoa bi mat.</summary>
        public required string Nguon { get; init; }
    }

    /// <summary>Ket qua liet ke nguon ngoai.</summary>
    public sealed class KetQuaLietKe
    {
        public required List<MucCanTai> Muc { get; init; }

        /// <summary>So muc bi bo ngay luc liet ke (vuot gioi han so file...).</summary>
        public required int BoQua { get; init; }

        public required List<string> Loi { get; init; }
    }

    /// <summary>
    /// Nhap du lieu tu nguon ngoai (FB-11): danh sach URL, bucket S3 (AWS / MinIO), Google Drive cong khai.
    ///
    ///   LUC NHAN YEU CAU (API, LietKeAsync): kiem thong so, liet ke cac file cua nguon (S3: ky san link GET cho tung
    ///   file; Drive: mo thu muc ra tung file; URL: dung nguyen danh sach). Moi file thanh MOT mau "cho xu ly" kem link tai.
    ///   Khoa bi mat cua doanh nghiep chi dung trong request nay — khong luu vao database.
    ///
    ///   LUC XU LY (worker, TaiVeAsync): tai tung file qua cua chong SSRF (ChanSsrf) vao tep tam co gioi han kich thuoc /
    ///   thoi gian → day vao MinIO tai khoa cua mau → mau di tiep nhu file da upload (kiem loai THAT theo noi dung, ffprobe,
    ///   cat doan, chong trung).
    /// Chi du an anh / am thanh / video — text / cap / danh sach van dung dong JSON.
    /// </summary>
    public sealed class NhapNguonNgoaiService
    {
        /// <summary>Link S3 ky san song du lau de hang doi xu ly het (toi da cua chu ky SigV4 la 7 ngay).</summary>
        private static readonly TimeSpan HanLinkS3 = TimeSpan.FromDays(6);

        private readonly IObjectStorage _storage;
        private readonly IHttpClientFactory _http;
        private readonly NhapNgoaiOptions _options;
        private readonly ISettings _settings;
        private readonly ILogger<NhapNguonNgoaiService> _logger;

        public NhapNguonNgoaiService(
            IObjectStorage storage,
            IHttpClientFactory http,
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
            _options = options.Value;
            _settings = settings;
            _logger = logger;
        }

        public const string TenHttpClient = "nhap-nguon-ngoai";

        // =====================================================================
        // API: kiem thong so + liet ke
        // =====================================================================

        /// <summary>Kiem request va liet ke file cua nguon. Sai thong so / nguon khong doc duoc → InvalidValueException.</summary>
        public async Task<KetQuaLietKe> LietKeAsync(ImportMauRequest body, LabelingProject duAn, CancellationToken ct)
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
                throw new InvalidValueException("nhap_ngoai_chi_cho_file", "Nhập từ nguồn ngoài chỉ cho dự án ảnh / âm thanh / video. Văn bản dùng dòng JSON.");
            }

            string nguon = (body.Source ?? string.Empty).Trim().ToLowerInvariant();
            int toiDa = _settings.SoNguyen(SettingKeys.DatasetImportMaxFiles);
            List<MucCanTai> muc = new List<MucCanTai>();
            List<string> loi = new List<string>();
            int boQua = 0;

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
                    muc.Add(new MucCanTai { Ten = TenTuUrl(u), Nguon = u });
                }
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

                try
                {
                    using (NguonS3 nguonS3 = new NguonS3(new ThongSoS3
                    {
                        Endpoint = string.IsNullOrWhiteSpace(s3.Endpoint) ? null : s3.Endpoint.Trim(),
                        Region = string.IsNullOrWhiteSpace(s3.Region) ? null : s3.Region.Trim(),
                        Bucket = s3.Bucket.Trim(),
                        Prefix = s3.Prefix ?? string.Empty,
                        AccessKey = s3.AccessKey.Trim(),
                        SecretKey = s3.SecretKey,
                    }, _options.AllowedPrivateHosts))
                    {
                        List<MucNguon> ds = await nguonS3.LietKeAsync(toiDa + 1, ct);
                        foreach (MucNguon m in ds.Take(toiDa))
                        {
                            muc.Add(new MucCanTai { Ten = m.Ten, Nguon = nguonS3.TaoLinkTai(m, HanLinkS3) });
                        }

                        if (ds.Count > toiDa)
                        {
                            boQua = 1;
                            loi.Add("nguồn: chỉ lấy " + toiDa + " file đầu (dataset.import_max_files).");
                        }
                    }
                }
                catch (NguonNgoaiException ex)
                {
                    throw new InvalidValueException(ex.Code, ex.Message);
                }
            }
            else if (nguon == "gdrive")
            {
                List<string> links = (body.GoogleDrive == null || body.GoogleDrive.Links == null ? new List<string>() : body.GoogleDrive.Links.ToList())
                    .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
                if (links.Count == 0 || links.Count > toiDa)
                {
                    throw new InvalidValueException("thieu_link_drive", "Cần 1–" + toiDa + " link Google Drive.");
                }

                HttpClient http = _http.CreateClient(TenHttpClient);
                foreach (string l in links)
                {
                    string? thuMuc = GoogleDrive.MaThuMuc(l);
                    string? maFile = GoogleDrive.MaFile(l);
                    if (maFile == null && thuMuc == null)
                    {
                        throw new InvalidValueException("link_drive_khong_hop_le", "Không nhận ra link Google Drive: " + l);
                    }

                    if (thuMuc != null)
                    {
                        if (string.IsNullOrWhiteSpace(_options.GoogleApiKey))
                        {
                            throw new InvalidValueException("chua_cau_hinh_google_drive", "Thư mục Drive cần khoá API Google (DatasetImport:GoogleApiKey) — dán link từng file thay thế.");
                        }

                        try
                        {
                            List<MucNguon> ds = await GoogleDrive.LietKeThuMucAsync(http, thuMuc, _options.GoogleApiKey, toiDa - muc.Count, ct);
                            foreach (MucNguon m in ds)
                            {
                                muc.Add(new MucCanTai { Ten = m.Ten, Nguon = m.Url! });
                            }
                        }
                        catch (NguonNgoaiException ex)
                        {
                            throw new InvalidValueException(ex.Code, ex.Message);
                        }
                    }
                    else
                    {
                        muc.Add(new MucCanTai { Ten = maFile!, Nguon = GoogleDrive.LinkTai(maFile!, _options.GoogleApiKey) });
                    }
                }

                if (muc.Count > toiDa)
                {
                    boQua = muc.Count - toiDa;
                    loi.Add("nguồn: chỉ lấy " + toiDa + " file đầu (dataset.import_max_files).");
                    muc = muc.Take(toiDa).ToList();
                }
            }
            else
            {
                throw new InvalidValueException("nguon_khong_ho_tro", "Nguồn: urls | s3 | gdrive.");
            }

            if (muc.Count == 0)
            {
                throw new InvalidValueException("nguon_trong", "Nguồn không có file nào để nhập.");
            }

            _logger.LogInformation("Du an {ProjectId}: liet ke {SoFile} file tu nguon {Nguon}", duAn.Id, muc.Count, nguon);
            return new KetQuaLietKe { Muc = muc, BoQua = boQua, Loi = loi };
        }

        // =====================================================================
        // WORKER: tai MOT file ve MinIO
        // =====================================================================

        /// <summary>
        /// Tai file cua mau (Sample.IngestSource) vao Sample.StorageKey trong MinIO. Loi nguon (qua lon, dia chi noi bo,
        /// het gio, S3 tu choi...) nem NguonNgoaiException — worker ghi vao mau.
        /// </summary>
        public async Task TaiVeAsync(Sample mau, CancellationToken ct)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (mau.IngestSource == null || mau.StorageKey == null)
            {
                throw new InvalidOperationException("Mau khong co nguon de tai.");
            }

            long toiDaByte = mau.Modality == Modalities.Image
                ? _settings.SoLon(SettingKeys.DatasetImageMaxBytes)
                : _settings.SoLon(SettingKeys.DatasetImportFileMaxBytes);
            TimeSpan gio = _settings.ThoiGian(SettingKeys.DatasetImportTimeout);

            HttpClient http = _http.CreateClient(TenHttpClient);
            TaiVeAnToan taiVe = new TaiVeAnToan(http);
            using (TepTaiVe tep = await taiVe.TaiAsync(mau.IngestSource, toiDaByte, gio, null, ct))
            {
                await _storage.LuuTepAsync(mau.StorageKey, tep.DuongDan, tep.ContentType ?? "application/octet-stream", ct);
            }
        }

        /// <summary>Duoi file chi gom chu / so, toi da 8 ky tu (khoa MinIO khong mang ky tu la tu ten nguoi dung).</summary>
        public static string DuoiAnToan(string ten)
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
    }
}
