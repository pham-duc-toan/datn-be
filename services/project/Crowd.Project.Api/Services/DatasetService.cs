using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Datasets;
using Crowd.Project.Infrastructure.Media;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Nap dataset (FB-11) va xem mau. Hai duong:
    ///   - ZIP anh: xu ly NGAY trong request (chi du an image, anh nho).
    ///   - Manifest: tao lo Pending, DatasetIngestor xu ly nen (moi loai du lieu).
    ///
    /// Duong ZIP:
    /// Thu tu: doc ZIP → day tung anh len MinIO → ghi cac dong samples + event
    /// dataset.ingested trong MOT SaveChanges. Anh len MinIO TRUOC khi commit:
    /// neu commit hong thi don anh da day (khong de file mo coi).
    /// </summary>
    public sealed class DatasetService
    {
        /// <summary>So dong toi da gui kem request — nhieu hon thi dung file manifest trong MinIO.</summary>
        public const int SoDongGuiKemToiDa = 1000;

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;
        private readonly ILogger<DatasetService> _logger;

        public DatasetService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            IObjectStorage storage,
            TimeProvider clock,
            ILogger<DatasetService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _access = access;
            _events = events;
            _storage = storage;
            _clock = clock;
            _logger = logger;
        }

        public async Task<DatasetResponse> NapZipAsync(
            Guid projectId,
            string name,
            Stream zip,
            Caller caller,
            CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            if (duAn.Modality != Modalities.Image)
            {
                throw new RuleViolationException(
                    "zip_chi_cho_anh",
                    "Upload ZIP chi danh cho du an anh. Du lieu " + duAn.Modality + " dung upload thang + manifest.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Dataset dataset = Dataset.Tao(projectId, name, bayGio);

            // Dau van tay cac anh DA CO trong du an — bo qua anh trung ngay khi doc,
            // khong day len MinIO lan nua.
            List<string> daCoList = await _db.Samples
                .Where(s => s.ProjectId == projectId)
                .Select(s => s.Sha256)
                .ToListAsync(ct);
            HashSet<string> daCo = new HashSet<string>(daCoList, StringComparer.Ordinal);

            List<Sample> mauMoi = new List<Sample>();
            List<string> khoaDaDay = new List<string>();
            int soTrung = 0;

            try
            {
                int soBoQua = await ZipImageReader.DuyetAsync(
                    zip,
                    new ZipLimits(),
                    async anh =>
                    {
                        if (!daCo.Add(anh.Sha256))
                        {
                            soTrung = soTrung + 1;
                            return;
                        }

                        // Kich thuoc anh — de kiem khung (bbox / polygon) nam trong anh.
                        (int Width, int Height)? kichThuoc = ImageHeader.DocKichThuoc(anh.Content);
                        SampleMetadata md = kichThuoc.HasValue
                            ? new SampleMetadata { Width = kichThuoc.Value.Width, Height = kichThuoc.Value.Height }
                            : SampleMetadata.Rong;

                        Sample mau = Sample.TaoAnhTrongZip(
                            projectId,
                            dataset.Id,
                            anh.OriginalName,
                            anh.ContentType,
                            anh.Extension,
                            anh.Content.Length,
                            anh.Sha256,
                            md,
                            bayGio);

                        string khoa = mau.StorageKey!;
                        await _storage.LuuAsync(khoa, anh.Content, anh.ContentType, ct);
                        khoaDaDay.Add(khoa);
                        mauMoi.Add(mau);
                    },
                    ct);

                dataset.GhiNhanKetQuaNap(mauMoi.Count, soBoQua + soTrung);

                _db.Datasets.Add(dataset);
                _db.Samples.AddRange(mauMoi);
                DatasetEvents.PhatCacLo(_events, projectId, dataset.Id, mauMoi, caller);

                await _db.SaveChangesAsync(ct);
            }
            catch
            {
                await DonAnhMoCoiAsync(khoaDaDay);
                throw;
            }

            _logger.LogInformation(
                "Da nap dataset {DatasetId} cho du an {ProjectId}: {SoMau} mau, bo qua {SoBoQua}",
                dataset.Id,
                projectId,
                dataset.SampleCount,
                dataset.SkippedCount);

            return TaoResponse(dataset);
        }

        public async Task<IReadOnlyList<DatasetResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            List<Dataset> ds = await _db.Datasets
                .AsNoTracking()
                .Where(d => d.ProjectId == projectId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(ct);

            List<DatasetResponse> ketQua = new List<DatasetResponse>();
            foreach (Dataset d in ds)
            {
                ketQua.Add(TaoResponse(d));
            }

            return ketQua;
        }

        /// <summary>
        /// Lo MANIFEST — moi loai du lieu. Chi TAO lo Pending; DatasetIngestor xu ly
        /// nen (doc file tu MinIO, do thoi luong, cat doan) vi file co the vai GB.
        /// </summary>
        public async Task<DatasetResponse> TaoTuManifestAsync(
            Guid projectId, CreateManifestDatasetRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            RawJson? dong = null;
            if (body.Rows.HasValue && body.Rows.Value.ValueKind != JsonValueKind.Null)
            {
                JsonElement r = body.Rows.Value;
                if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() == 0 || r.GetArrayLength() > SoDongGuiKemToiDa)
                {
                    throw new InvalidValueException(
                        "manifest_khong_hop_le",
                        "Truong rows phai la mang 1-" + SoDongGuiKemToiDa + " dong. Nhieu hon thi upload file .jsonl va dung manifestKey.");
                }

                dong = RawJson.Tu(r);
            }

            Dataset lo = Dataset.TaoTuManifest(projectId, body.Name ?? string.Empty, dong, body.ManifestKey, _clock.GetUtcNow());
            _db.Datasets.Add(lo);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Tao lo manifest {DatasetId} cho du an {ProjectId} — cho worker xu ly", lo.Id, projectId);
            return TaoResponse(lo);
        }

        /// <summary>
        /// Mau kem link xem file. CHI chu du an: link cho phep xem du lieu goc, va du
        /// lieu goc la tai san cua doanh nghiep (P-01).
        /// </summary>
        public async Task<PagedResponse<SampleResponse>> DanhSachMauAsync(
            Guid projectId, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            IQueryable<Sample> q = _db.Samples.AsNoTracking().Where(s => s.ProjectId == projectId);
            int tong = await q.CountAsync(ct);

            List<Sample> trang = await q
                .OrderBy(s => s.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            List<SampleResponse> items = new List<SampleResponse>();
            foreach (Sample s in trang)
            {
                items.Add(new SampleResponse
                {
                    Id = s.Id,
                    DatasetId = s.DatasetId,
                    Modality = s.Modality,
                    OriginalName = s.OriginalName,
                    SizeBytes = s.SizeBytes,
                    FileUrl = s.StorageKey == null ? null : await _storage.TaoLinkXemAsync(s.StorageKey),
                    Content = s.Content,
                    Metadata = s.Metadata,
                });
            }

            return new PagedResponse<SampleResponse>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                Total = tong,
            };
        }

        private async Task DonAnhMoCoiAsync(List<string> khoa)
        {
            foreach (string k in khoa)
            {
                try
                {
                    // CancellationToken.None: request da hong/huy, nhung don dep
                    // van phai chay xong.
                    await _storage.XoaAsync(k, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Khong xoa duoc anh mo coi {Key}", k);
                }
            }
        }

        public static DatasetResponse TaoResponse(Dataset d)
        {
            if (d == null)
            {
                throw new ArgumentNullException(nameof(d));
            }

            return new DatasetResponse
            {
                Id = d.Id,
                Name = d.Name,
                Status = d.Status,
                SampleCount = d.SampleCount,
                SkippedCount = d.SkippedCount,
                ErrorSummary = d.ErrorSummary,
                CreatedAt = d.CreatedAt,
                FinishedAt = d.FinishedAt,
            };
        }
    }
}
