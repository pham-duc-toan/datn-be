using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Exports;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Project.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Tao file ket qua CHAY NEN (kien truc 3.41) — Outbox + RabbitMQ:
    ///
    ///   POST .../exports {format}  → kiem quyen + kiem dinh dang hop du an (400 ngay) → dong export_jobs "queued" +
    ///                                event export.requested CUNG transaction → 202.
    ///   consumer project-svc.export-requested (TaoFileAsync)
    ///                              → processing → gop ket qua → dung file → luu MinIO → ready | failed.
    ///   GET .../exports[/{id}]     → trang thai (dang cho qua export.job_timeout thi bao failed).
    ///   GET .../exports/{id}/file  → tai file (qua service, chi chu du an — khong phat link MinIO).
    ///
    /// Thoi gian gop ket qua / dung ZIP khong con nam trong request: du an lon bao nhieu thi request van tra ngay.
    /// </summary>
    public sealed class ExportService
    {
        /// <summary>So lan tao file tra ve trong danh sach (moi dinh dang chi giu lan moi nhat nen it khi cham).</summary>
        private const int SoLanToiDa = 50;

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly AnnotationService _ketQua;
        private readonly IObjectStorage _storage;
        private readonly IServiceScopeFactory _scopes;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<ExportService> _logger;

        public ExportService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            AnnotationService ketQua,
            IObjectStorage storage,
            IServiceScopeFactory scopes,
            ISettings settings,
            TimeProvider clock,
            ILogger<ExportService> logger)
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

            if (ketQua == null)
            {
                throw new ArgumentNullException(nameof(ketQua));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (scopes == null)
            {
                throw new ArgumentNullException(nameof(scopes));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
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
            _ketQua = ketQua;
            _storage = storage;
            _scopes = scopes;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // API
        // =====================================================================

        /// <summary>
        /// Doanh nghiep bam tao file. Dang co lan cung dinh dang chua xong → tra lai lan do (bam hai lan khong gop hai
        /// lan). Lan hong / qua han cu cua dinh dang nay bi xoa: moi dinh dang chi giu lan moi nhat.
        /// </summary>
        public async Task<ExportJobResponse> YeuCauAsync(Guid projectId, TaoFileXuatRequest? body, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayChoChuAsync(projectId, caller, ct);
            if (duAn.LabelSchema == null)
            {
                throw new RuleViolationException("chua_co_tap_nhan", "Dự án chưa có tập nhãn.");
            }

            string dinhDang = XuatKetQua.ChuanHoa(body == null ? null : body.Format);
            XuatKetQua.KiemHopDuAn(duAn.LabelSchema, dinhDang);

            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan han = HanTao();

            List<ExportJob> cungDinhDang = await _db.ExportJobs
                .Where(j => j.ProjectId == projectId && j.Format == dinhDang && j.State != TrangThaiXuat.SanSang)
                .ToListAsync(ct);

            ExportJob? dangChay = cungDinhDang
                .Where(j => j.DangCho() && !j.QuaHan(bayGio, han))
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefault();
            if (dangChay != null)
            {
                return TaoResponse(dangChay, bayGio, han);
            }

            // Lan hong / dang cho qua han: bo. Message cua lan qua han (neu con trong hang doi) toi sau se thay job
            // khong con va bo qua.
            _db.ExportJobs.RemoveRange(cungDinhDang);

            ExportJob job = ExportJob.Tao(projectId, dinhDang, caller.UserId, bayGio);
            _db.ExportJobs.Add(job);
            _events.Phat(caller, new ExportRequested
            {
                JobId = job.Id,
                ProjectId = projectId,
                Format = dinhDang,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, TenRangBuoc.XuatDangCho))
            {
                // Hai lan bam cung luc: lan kia vua tao xong — tra lai lan do.
                _db.ChangeTracker.Clear();
                ExportJob? kia = await _db.ExportJobs.AsNoTracking()
                    .Where(j => j.ProjectId == projectId && j.Format == dinhDang
                                && (j.State == TrangThaiXuat.ChoXuLy || j.State == TrangThaiXuat.DangXuLy))
                    .FirstOrDefaultAsync(ct);
                if (kia == null)
                {
                    throw;
                }

                return TaoResponse(kia, bayGio, han);
            }

            _logger.LogInformation("Yeu cau tao file {Format} cho du an {ProjectId}: job {JobId}", dinhDang, projectId, job.Id);
            return TaoResponse(job, bayGio, han);
        }

        /// <summary>Cac lan tao file cua du an, moi nhat truoc.</summary>
        public async Task<IReadOnlyList<ExportJobResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayChoChuAsync(projectId, caller, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan han = HanTao();

            List<ExportJob> ds = await _db.ExportJobs.AsNoTracking()
                .Where(j => j.ProjectId == projectId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(SoLanToiDa)
                .ToListAsync(ct);

            return ds.Select(j => TaoResponse(j, bayGio, han)).ToList();
        }

        public async Task<ExportJobResponse> ChiTietAsync(Guid projectId, Guid jobId, Caller caller, CancellationToken ct)
        {
            await _access.LayChoChuAsync(projectId, caller, ct);
            ExportJob job = await LayJobAsync(projectId, jobId, ct);
            return TaoResponse(job, _clock.GetUtcNow(), HanTao());
        }

        /// <summary>Mo luong doc file da tao — controller tra thang ve client (khong nap ca file vao RAM).</summary>
        public async Task<FileXuatDangMo> MoFileAsync(Guid projectId, Guid jobId, Caller caller, CancellationToken ct)
        {
            await _access.LayChoChuAsync(projectId, caller, ct);
            ExportJob job = await LayJobAsync(projectId, jobId, ct);
            if (job.State != TrangThaiXuat.SanSang || job.StorageKey == null)
            {
                throw new RuleViolationException("file_chua_san_sang", "File chưa tạo xong (" + job.TrangThaiHienThi(_clock.GetUtcNow(), HanTao()) + ").");
            }

            ThongTinFile? tt = await _storage.ThongTinAsync(job.StorageKey, ct);
            if (tt == null)
            {
                // Du an dong qua han giu file (doanh nghiep dong y xoa file) — kho da don.
                throw new RuleViolationException("file_khong_con", "File không còn trong kho. Bấm tạo lại để có file mới.");
            }

            Stream luong = await _storage.MoDocAsync(job.StorageKey, ct);
            return new FileXuatDangMo(luong, job.ContentType ?? "application/octet-stream", job.FileName ?? ("ket-qua-" + projectId + ".bin"), tt.SizeBytes);
        }

        // =====================================================================
        // CONSUMER
        // =====================================================================

        /// <summary>
        /// Consumer export.requested goi. Chay TRONG transaction cua IdempotencyGuard (processed_events + ket qua job
        /// commit cung luc) — o day chi DOC cho toi buoc cuoi, khong giu khoa dong nao.
        ///
        /// Ket cuc:
        ///   - job da xoa / da xong (giao lai) → BoQuaEventException (loai 1).
        ///   - loi nghiep vu (du an mat tap nhan, dinh dang khong con hop) → job failed, ack.
        ///   - loi ky thuat (MinIO / quality-svc tam sap) → nem tiep: consumer thu lai voi gian cach, het so lan thi
        ///     DLQ; job dang cho qua export.job_timeout thi API bao failed.
        /// </summary>
        public async Task TaoFileAsync(ExportRequested yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            ExportJob? job = await _db.ExportJobs.FirstOrDefaultAsync(j => j.Id == yeuCau.JobId && j.ProjectId == yeuCau.ProjectId, ct);
            if (job == null)
            {
                throw new BoQuaEventException("lan tao file " + yeuCau.JobId + " khong con (du an da xoa / doanh nghiep da tao lai)");
            }

            if (job.DaXong())
            {
                throw new BoQuaEventException("lan tao file " + yeuCau.JobId + " da ket thuc (" + job.State + ")");
            }

            DateTimeOffset batDau = _clock.GetUtcNow();
            await DanhDauDangTaoAsync(job.Id, batDau, ct);
            job.BatDau(batDau);

            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == job.ProjectId, ct);
            if (duAn == null)
            {
                throw new BoQuaEventException("du an " + job.ProjectId + " da xoa");
            }

            ProjectResultsResponse kq;
            ExportFile file;
            try
            {
                kq = await _ketQua.KetQuaCuaDuAnAsync(duAn, ct);
                file = XuatKetQua.Dung(kq, LabelSchema.Doc(kq.LabelSchema), job.Format);
            }
            catch (DomainException ex)
            {
                job.Loi(ex.Message, _clock.GetUtcNow());
                _logger.LogWarning("Khong tao duoc file {Format} cho du an {ProjectId}: {LyDo}", job.Format, job.ProjectId, ex.Message);
                return;
            }
            catch (LabelFormatException ex)
            {
                job.Loi(ex.Message, _clock.GetUtcNow());
                _logger.LogWarning("Khong tao duoc file {Format} cho du an {ProjectId}: {LyDo}", job.Format, job.ProjectId, ex.Message);
                return;
            }

            string dauVanTay = XuatKetQua.DauVanTay(kq);

            // Ket qua chua doi tu lan truoc (cung dinh dang) va file cu con trong kho → dung lai, khong luu ban moi.
            ExportJob? truoc = await _db.ExportJobs
                .Where(j => j.ProjectId == job.ProjectId && j.Format == job.Format && j.Id != job.Id && j.State == TrangThaiXuat.SanSang)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(ct);

            string khoa;
            long kichThuoc;
            if (truoc != null && truoc.ResultFingerprint == dauVanTay && truoc.StorageKey != null
                && await _storage.ThongTinAsync(truoc.StorageKey, ct) != null)
            {
                khoa = truoc.StorageKey;
                kichThuoc = truoc.SizeBytes ?? file.NoiDung.LongLength;
            }
            else
            {
                khoa = job.KhoaFileMoi(XuatKetQua.Duoi(file.TenFile, job.ProjectId));
                // Giao lai sau khi da luu ma chua commit: ghi de cung khoa — vo hai.
                await _storage.LuuAsync(khoa, file.NoiDung, file.ContentType, ct);
                kichThuoc = file.NoiDung.LongLength;
            }

            job.HoanTat(khoa, file.TenFile, file.ContentType, kichThuoc, dauVanTay, kq.SampleCount, _clock.GetUtcNow());

            // Moi dinh dang chi giu lan moi nhat: bo cac lan ready cu, xoa file khong con ai dung.
            List<ExportJob> cu = await _db.ExportJobs
                .Where(j => j.ProjectId == job.ProjectId && j.Format == job.Format && j.Id != job.Id && j.State == TrangThaiXuat.SanSang)
                .ToListAsync(ct);
            _db.ExportJobs.RemoveRange(cu);
            foreach (string k in cu.Where(j => j.StorageKey != null && j.StorageKey != khoa).Select(j => j.StorageKey!).Distinct())
            {
                await XoaFileAsync(k);
            }

            _logger.LogInformation(
                "Tao xong file {Format} cho du an {ProjectId}: {SoMau} mau, {Byte} byte{DungLai}",
                job.Format,
                job.ProjectId,
                kq.SampleCount,
                kichThuoc,
                truoc != null && khoa == truoc.StorageKey ? " (dung lai file cu — ket qua chua doi)" : string.Empty);
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        /// <summary>
        /// Ghi "processing" NGAY (ket noi rieng, tu commit) de giao dien thay dang tao file — thay doi cua consumer
        /// chi commit o cuoi, cung processed_events.
        /// </summary>
        private async Task DanhDauDangTaoAsync(Guid jobId, DateTimeOffset luc, CancellationToken ct)
        {
            using (IServiceScope scope = _scopes.CreateScope())
            {
                ProjectDbContext db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
                await db.ExportJobs
                    .Where(j => j.Id == jobId && j.State == TrangThaiXuat.ChoXuLy)
                    .ExecuteUpdateAsync(
                        u => u.SetProperty(j => j.State, TrangThaiXuat.DangXuLy).SetProperty(j => j.StartedAt, luc),
                        ct);
            }
        }

        private async Task XoaFileAsync(string khoa)
        {
            try
            {
                await _storage.XoaAsync(khoa, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // File cu chi ton cho; dong job da bo nen khong ai tai lai duoc.
                _logger.LogWarning(ex, "Khong xoa duoc file ket qua cu {Key}", khoa);
            }
        }

        private async Task<ExportJob> LayJobAsync(Guid projectId, Guid jobId, CancellationToken ct)
        {
            ExportJob? job = await _db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.ProjectId == projectId, ct);
            if (job == null)
            {
                throw new NotFoundException("Không tìm thấy lần tạo file này.");
            }

            return job;
        }

        private TimeSpan HanTao()
        {
            return _settings.ThoiGian(SettingKeys.ExportJobTimeout);
        }

        private static ExportJobResponse TaoResponse(ExportJob j, DateTimeOffset bayGio, TimeSpan han)
        {
            string trangThai = j.TrangThaiHienThi(bayGio, han);
            string? loi = j.Error;
            if (trangThai == TrangThaiXuat.Loi && j.DangCho())
            {
                loi = "Tạo file quá lâu chưa xong. Bấm tạo lại.";
            }

            return new ExportJobResponse
            {
                Id = j.Id,
                ProjectId = j.ProjectId,
                Format = j.Format,
                Status = trangThai,
                FileName = j.FileName,
                SizeBytes = j.SizeBytes,
                SampleCount = j.SampleCount,
                Error = loi,
                CreatedAt = j.CreatedAt,
                FinishedAt = j.FinishedAt,
                DownloadPath = trangThai == TrangThaiXuat.SanSang
                    ? "/annotations/projects/" + j.ProjectId + "/exports/" + j.Id + "/file"
                    : null,
            };
        }
    }

    /// <summary>File ket qua dang mo de tra ve client — controller Dispose luong sau khi gui xong.</summary>
    public sealed class FileXuatDangMo
    {
        public FileXuatDangMo(Stream luong, string contentType, string tenFile, long kichThuoc)
        {
            Luong = luong;
            ContentType = contentType;
            TenFile = tenFile;
            KichThuoc = kichThuoc;
        }

        public Stream Luong { get; }

        public string ContentType { get; }

        public string TenFile { get; }

        public long KichThuoc { get; }
    }
}
