using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Project;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Datasets;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Nap dataset tu file ZIP anh (FB-11) va xem mau.
    ///
    /// Thu tu: doc ZIP → day tung anh len MinIO → ghi cac dong samples + event
    /// dataset.ingested trong MOT SaveChanges. Anh len MinIO TRUOC khi commit:
    /// neu commit hong thi don anh da day (khong de file mo coi).
    /// </summary>
    public sealed class DatasetService
    {
        /// <summary>So mau moi lo dataset.ingested — giu message nho (vai chuc KB).</summary>
        public const int CoLo = 500;

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

                        Sample mau = Sample.Tao(
                            projectId,
                            dataset.Id,
                            anh.OriginalName,
                            anh.ContentType,
                            anh.Extension,
                            anh.Content.Length,
                            anh.Sha256,
                            bayGio);

                        await _storage.LuuAsync(mau.StorageKey, anh.Content, anh.ContentType, ct);
                        khoaDaDay.Add(mau.StorageKey);
                        mauMoi.Add(mau);
                    },
                    ct);

                dataset.GhiNhanKetQuaNap(mauMoi.Count, soBoQua + soTrung);

                _db.Datasets.Add(dataset);
                _db.Samples.AddRange(mauMoi);
                PhatCacLo(projectId, dataset.Id, mauMoi, caller);

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
        /// Mau kem link xem anh. CHI chu du an: link cho phep xem anh goc, va anh
        /// goc la tai san cua doanh nghiep (P-01).
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
                    OriginalName = s.OriginalName,
                    SizeBytes = s.SizeBytes,
                    ImageUrl = await _storage.TaoLinkXemAsync(s.StorageKey),
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

        private void PhatCacLo(Guid projectId, Guid datasetId, List<Sample> mau, Caller caller)
        {
            int soLo = (mau.Count + CoLo - 1) / CoLo;

            for (int lo = 0; lo < soLo; lo++)
            {
                List<IngestedSample> trongLo = new List<IngestedSample>();
                foreach (Sample s in mau.Skip(lo * CoLo).Take(CoLo))
                {
                    trongLo.Add(new IngestedSample { SampleId = s.Id, StorageKey = s.StorageKey });
                }

                _events.Phat(caller, new DatasetIngested
                {
                    ProjectId = projectId,
                    DatasetId = datasetId,
                    BatchIndex = lo,
                    BatchCount = soLo,
                    Samples = trongLo,
                });
            }
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

        private static DatasetResponse TaoResponse(Dataset d)
        {
            return new DatasetResponse
            {
                Id = d.Id,
                Name = d.Name,
                SampleCount = d.SampleCount,
                SkippedCount = d.SkippedCount,
                CreatedAt = d.CreatedAt,
            };
        }
    }
}
