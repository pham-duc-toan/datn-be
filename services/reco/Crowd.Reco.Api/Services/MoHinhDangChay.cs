using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Storage;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Reco.Api.Services
{
    /// <summary>Hai mo hinh dang chay + nhom A/B cua nguoi xem.</summary>
    public sealed class MoHinhDaChon
    {
        public required IMoHinhGoiY MoHinh { get; init; }

        /// <summary>control | treatment</summary>
        public required string Nhom { get; init; }
    }

    /// <summary>
    /// Mo hinh dang dung de cham feed (kien truc 3.39 muc 8):
    ///   production — ban dang chay cho moi nguoi (mac dinh G1 — cong thuc tay, khi chua co hoc may);
    ///   candidate  — ban MOI ml-svc vua hoc, chay thu A/B cho reco.ab_treatment_percent % labeler.
    /// ml-svc ghi file mo hinh + manifest vao MinIO ({prefix}manifest.json: { production, candidate } = khoa file).
    /// reco-svc doc manifest moi reco.model_check_interval; file loi / ml-svc sap → giu mo hinh dang co.
    /// </summary>
    public sealed class MoHinhDangChay
    {
        private readonly IObjectStorage _kho;
        private readonly RecoOptions _options;
        private readonly ILogger<MoHinhDangChay> _logger;
        private IMoHinhGoiY _production = MoHinhG1.Instance;
        private IMoHinhGoiY? _candidate;
        private string? _khoaProduction;
        private string? _khoaCandidate;

        public MoHinhDangChay(IObjectStorage kho, IOptions<RecoOptions> options, ILogger<MoHinhDangChay> logger)
        {
            if (kho == null)
            {
                throw new ArgumentNullException(nameof(kho));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _kho = kho;
            _options = options.Value;
            _logger = logger;
        }

        public IMoHinhGoiY Production
        {
            get { return Volatile.Read(ref _production); }
        }

        public IMoHinhGoiY? Candidate
        {
            get { return Volatile.Read(ref _candidate); }
        }

        /// <summary>Mo hinh cho mot labeler: co ban thu nghiem va nguoi nay o nhom thu → ban thu; con lai → ban dang chay.</summary>
        public MoHinhDaChon Chon(Guid userId, int tiLeThuNghiem)
        {
            IMoHinhGoiY? thu = Candidate;
            if (thu != null && NhomAb.Chon(userId, tiLeThuNghiem) == NhomAb.ThuNghiem)
            {
                return new MoHinhDaChon { MoHinh = thu, Nhom = NhomAb.ThuNghiem };
            }

            return new MoHinhDaChon { MoHinh = Production, Nhom = NhomAb.DangChay };
        }

        /// <summary>Doc manifest; doi mo hinh neu khoa file doi. Tra ve true neu co doi.</summary>
        public async Task<bool> KiemTraAsync(CancellationToken ct)
        {
            string khoaManifest = _options.ModelPrefix + "manifest.json";
            if (await _kho.ThongTinAsync(khoaManifest, ct) == null)
            {
                return false;
            }

            string? khoaP;
            string? khoaC;
            using (Stream s = await _kho.MoDocAsync(khoaManifest, ct))
            using (JsonDocument doc = await JsonDocument.ParseAsync(s, default(JsonDocumentOptions), ct))
            {
                khoaP = Chuoi(doc.RootElement, "production");
                khoaC = Chuoi(doc.RootElement, "candidate");
            }

            bool doi = false;
            if (!string.Equals(khoaP, _khoaProduction, StringComparison.Ordinal))
            {
                IMoHinhGoiY moi = khoaP == null ? MoHinhG1.Instance : await NapAsync(khoaP, ct);
                Volatile.Write(ref _production, moi);
                _khoaProduction = khoaP;
                doi = true;
                _logger.LogInformation("Mo hinh goi y dang chay: {PhienBan} ({Loai})", moi.PhienBan, moi.Loai);
            }

            if (!string.Equals(khoaC, _khoaCandidate, StringComparison.Ordinal))
            {
                IMoHinhGoiY? moi = khoaC == null ? null : await NapAsync(khoaC, ct);
                Volatile.Write(ref _candidate, moi);
                _khoaCandidate = khoaC;
                doi = true;
                _logger.LogInformation("Mo hinh goi y chay thu A/B: {PhienBan}", moi == null ? "(khong co)" : moi.PhienBan);
            }

            return doi;
        }

        public string? KhoaProduction
        {
            get { return _khoaProduction; }
        }

        public string? KhoaCandidate
        {
            get { return _khoaCandidate; }
        }

        private async Task<IMoHinhGoiY> NapAsync(string khoa, CancellationToken ct)
        {
            using (Stream s = await _kho.MoDocAsync(khoa, ct))
            using (StreamReader r = new StreamReader(s))
            {
                string json = await r.ReadToEndAsync(ct);
                return MoHinhHocMay.Doc(json);
            }
        }

        private static string? Chuoi(JsonElement e, string ten)
        {
            JsonElement v;
            if (e.TryGetProperty(ten, out v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
            {
                return v.GetString();
            }

            return null;
        }
    }
}
