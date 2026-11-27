using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Project.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Nap mau vao du an (FB-11) va xem mau. KHONG co bang "lo nap" — moi mau tu mang tien trinh cua no
    /// (Sample.IngestState: queued → processing → ready | failed):
    ///
    ///   - ZIP anh        : upload thang len MinIO, dang ky phieu giai nen (ZipImport) → ZipImporter giai nen nen,
    ///                      moi anh hop le thanh mau ready (kien truc 3.41).
    ///   - Dong JSON      : text / pair / list → mau ready ngay; file (anh / am thanh / video da upload thang len
    ///                      MinIO bang link ky san) → mau "queued", SampleIngestor xu ly nen.
    ///   - Nguon ngoai    : liet ke nguon → moi file mot mau "queued" kem link tai, worker tai ve roi xu ly.
    ///
    /// Cau hoi rieng cua mau (samples.question): dong JSON gui "question"; ZIP / nguon ngoai thi sua tung mau sau
    /// (PUT .../samples/{id}/question). Khong gui = mau khong co ngu canh rieng, chi co cau hoi cua tung cong cu.
    ///
    /// Khong duong nap nao doc file nang trong request: file di thang len MinIO, viec doc / giai nen chay nen.
    /// </summary>
    public sealed class MauService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly IObjectStorage _storage;
        private readonly IMediaLinks _media;
        private readonly TimeProvider _clock;
        private readonly ILogger<MauService> _logger;
        private readonly ISettings _settings;

        public MauService(
            ProjectDbContext db,
            ProjectAccessService access,
            IObjectStorage storage,
            IMediaLinks media,
            TimeProvider clock,
            ILogger<MauService> logger,
            ISettings settings)
        {
            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            _media = media;

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
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
            _storage = storage;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // ZIP anh — dang ky phieu, worker giai nen nen (kien truc 3.41)
        // =====================================================================

        /// <summary>
        /// ZIP da PUT thang len kho bang link tu POST /uploads (khoa "{projectId}/zip/..."): tao phieu giai nen "queued"
        /// roi tra ngay (202). ZipImporter giai nen, kiem tung anh, bo trung, ghi mau — thoi gian giai nen khong con nam
        /// trong request. Gui lai cung key → tra lai phieu da co (khong giai nen hai lan).
        /// </summary>
        public async Task<ZipImportResponse> DangKyZipAsync(Guid projectId, DangKyZipRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            if (duAn.Modality != Modalities.Image)
            {
                throw new RuleViolationException(
                    "zip_chi_cho_anh",
                    "Upload ZIP chỉ dành cho dự án ảnh. Dữ liệu " + duAn.Modality + " dùng upload thẳng.");
            }

            string khoa = body.Key == null ? string.Empty : body.Key.Trim();
            if (khoa.Length == 0)
            {
                throw new InvalidValueException("thieu_key", "Cần \"key\" của file ZIP (nhận được khi xin link ở POST /uploads).");
            }

            ZipImport? daCo = await _db.ZipImports.AsNoTracking().FirstOrDefaultAsync(z => z.StorageKey == khoa, ct);
            if (daCo != null)
            {
                if (daCo.ProjectId != projectId)
                {
                    throw new InvalidValueException("khoa_zip_khong_hop_le", "File ZIP không thuộc dự án này.");
                }

                return ZipResponse(daCo);
            }

            ThongTinFile? tt = await _storage.ThongTinAsync(khoa, ct);
            if (tt == null)
            {
                throw new InvalidValueException("khong_tim_thay_file", "Chưa thấy file ZIP trong kho — upload xong rồi mới đăng ký.");
            }

            long tran = _settings.SoLon(SettingKeys.DatasetZipMaxBytes);
            if (tt.SizeBytes > tran)
            {
                await XoaFileAsync(khoa);
                throw new InvalidValueException("zip_qua_lon", "ZIP tối đa " + (tran / (1024 * 1024)) + " MB.");
            }

            ZipImport phieu = ZipImport.Tao(projectId, khoa, body.Name, tt.SizeBytes, _clock.GetUtcNow(), QuyDinhTuSetting.DuLieu(_settings));
            _db.ZipImports.Add(phieu);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, TenRangBuoc.ZipTheoKhoa))
            {
                // Hai lan gui cung luc: lan kia vua dang ky.
                _db.ChangeTracker.Clear();
                ZipImport kia = await _db.ZipImports.AsNoTracking().FirstAsync(z => z.StorageKey == khoa, ct);
                return ZipResponse(kia);
            }

            _logger.LogInformation("Dang ky ZIP {ZipId} ({Byte} byte) cho du an {ProjectId}", phieu.Id, tt.SizeBytes, projectId);
            return ZipResponse(phieu);
        }

        public static ZipImportResponse ZipResponse(ZipImport z)
        {
            if (z == null)
            {
                throw new ArgumentNullException(nameof(z));
            }

            return new ZipImportResponse
            {
                Id = z.Id,
                Name = z.OriginalName,
                SizeBytes = z.SizeBytes,
                Status = z.State,
                Processed = z.ProcessedCount,
                Ready = z.ImageCount,
                Skipped = z.SkippedCount,
                Error = z.Error,
                CreatedAt = z.CreatedAt,
                FinishedAt = z.FinishedAt,
            };
        }

        // =====================================================================
        // Dong JSON (moi loai du lieu)
        // =====================================================================

        /// <summary>
        /// Them mau tu cac dong JSON. text / pair / list → mau ready ngay. File (da upload thang len MinIO) → mau "queued":
        /// worker moi doc file, do thong tin va kiem loai that. Dong sai bi bo qua kem ly do — mot dong hong khong lam
        /// hong ca lo.
        /// </summary>
        public async Task<NapMauResponse> ThemMauAsync(Guid projectId, ThemMauRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            int soDongToiDa = _settings.SoNguyen(SettingKeys.DatasetManifestInlineMaxRows);
            if (!body.Rows.HasValue || body.Rows.Value.ValueKind != JsonValueKind.Array
                || body.Rows.Value.GetArrayLength() == 0 || body.Rows.Value.GetArrayLength() > soDongToiDa)
            {
                throw new InvalidValueException(
                    "dong_khong_hop_le",
                    "Trường rows phải là mảng 1–" + soDongToiDa + " dòng. Nhiều hơn thì chia thành nhiều lần gửi.");
            }

            JsonArray dong = (JsonArray)JsonNode.Parse(body.Rows.Value.GetRawText())!;
            bool laFile = Modalities.LaFile(duAn.Modality);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(_settings);
            int toiDaLoi = _settings.SoNguyen(SettingKeys.DatasetErrorSampleCount);

            HashSet<string> daCo = new HashSet<string>(StringComparer.Ordinal);
            if (laFile)
            {
                // File da co dong (cho / dang / xong / loi) thi khong tao them: gui trung mot khoa khong nhan doi mau.
                List<string?> khoaDaCo = await _db.Samples.IgnoreQueryFilters()
                    .Where(s => s.ProjectId == projectId && s.StorageKey != null)
                    .Select(s => s.StorageKey)
                    .ToListAsync(ct);
                foreach (string? k in khoaDaCo)
                {
                    daCo.Add(k!);
                }
            }
            else
            {
                daCo = await KhoaTrungDaCoAsync(projectId, ct);
            }

            List<Sample> moi = new List<Sample>();
            List<MauNhanResponse> nhan = new List<MauNhanResponse>();
            List<string> loi = new List<string>();
            int boQua = 0;

            for (int i = 0; i < dong.Count; i++)
            {
                try
                {
                    JsonObject? o = dong[i] as JsonObject;
                    if (o == null)
                    {
                        throw new InvalidValueException("dong_khong_hop_le", "dòng phải là một object JSON.");
                    }

                    string ten = DongNap.TenDong(o, i);

                    if (laFile)
                    {
                        string khoa = DongNap.ChuoiBatBuoc(o, "file");
                        if (!daCo.Add(khoa))
                        {
                            throw new InvalidValueException("file_da_co", "file '" + khoa + "' đã được nạp.");
                        }

                        Sample cho = Sample.TaoChoNap(projectId, duAn.Modality, khoa, ten, DongNap.GoiYFile(o), bayGio, quyDinh);
                        cho.DatCauHoi(DongNap.CauHoi(o));
                        moi.Add(cho);
                        nhan.Add(new MauNhanResponse { Index = i, SampleId = cho.Id, Name = cho.OriginalName });
                    }
                    else
                    {
                        Sample mau = DongNap.TaoMauNoiDung(o, duAn.Modality, ten, projectId, bayGio, _settings);
                        mau.DatCauHoi(DongNap.CauHoi(o));
                        if (!daCo.Add(DongNap.KhoaTrung(mau.Sha256!, mau.Question)))
                        {
                            throw new InvalidValueException("noi_dung_trung", "trùng nội dung (và câu hỏi) với mẫu đã có.");
                        }

                        moi.Add(mau);
                        nhan.Add(new MauNhanResponse { Index = i, SampleId = mau.Id, Name = mau.OriginalName });
                    }
                }
                catch (InvalidValueException ex)
                {
                    boQua++;
                    DongNap.GhiLoi(loi, i, ex.Message, toiDaLoi);
                }
                catch (LabelFormatException ex)
                {
                    boQua++;
                    DongNap.GhiLoi(loi, i, ex.Message, toiDaLoi);
                }
            }

            _db.Samples.AddRange(moi);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Them {SoMau} mau ({Loai}) cho du an {ProjectId}, bo qua {BoQua}", moi.Count, duAn.Modality, projectId, boQua);

            return new NapMauResponse
            {
                Received = dong.Count,
                Ready = laFile ? 0 : moi.Count,
                Queued = laFile ? moi.Count : 0,
                Skipped = boQua,
                Errors = loi,
                Accepted = nhan,
            };
        }

        // =====================================================================
        // Nguon ngoai
        // =====================================================================

        public async Task<NapMauResponse> NhapNguonNgoaiAsync(
            Guid projectId, ImportMauRequest body, NhapNguonNgoaiService nhap, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (nhap == null)
            {
                throw new ArgumentNullException(nameof(nhap));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            KetQuaLietKe ds = await nhap.LietKeAsync(body, duAn, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(_settings);

            List<Sample> moi = new List<Sample>();
            List<MauNhanResponse> nhan = new List<MauNhanResponse>();
            List<string> loi = new List<string>(ds.Loi);
            int boQua = ds.BoQua;
            for (int i = 0; i < ds.Muc.Count; i++)
            {
                MucCanTai m = ds.Muc[i];
                if (m.Nguon.Length > 2000)
                {
                    boQua++;
                    if (loi.Count < _settings.SoNguyen(SettingKeys.DatasetErrorSampleCount))
                    {
                        loi.Add(m.Ten + ": đường dẫn tải quá dài.");
                    }

                    continue;
                }

                Sample cho = Sample.TaoChoTai(projectId, duAn.Modality, m.Ten, NhapNguonNgoaiService.DuoiAnToan(m.Ten), m.Nguon, bayGio, quyDinh);
                moi.Add(cho);
                nhan.Add(new MauNhanResponse { Index = i, SampleId = cho.Id, Name = cho.OriginalName });
            }

            if (moi.Count == 0)
            {
                throw new InvalidValueException("nguon_trong", "Không có file nào nhận được từ nguồn.");
            }

            _db.Samples.AddRange(moi);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Nhap ngoai cho du an {ProjectId}: {SoFile} file cho tai, bo qua {BoQua}", projectId, moi.Count, boQua);

            return new NapMauResponse
            {
                Received = ds.Muc.Count + ds.BoQua,
                Ready = 0,
                Queued = moi.Count,
                Skipped = boQua,
                Errors = loi,
                Accepted = nhan,
            };
        }

        // =====================================================================
        // Xem mau + tien trinh nap
        // =====================================================================

        /// <summary>
        /// Mau kem link xem file. CHI chu du an: link cho phep xem du lieu goc, va du
        /// lieu goc la tai san cua doanh nghiep (P-01).
        ///
        /// ids (tuy chon, toi da 100): chi lay cac mau nay — trang ket qua / duyet hien ten file goc va
        /// anh xem truoc cho dung cac mau dang hien, khong phai tai het du an.
        ///
        /// Thay CA mau chua nap xong / bi loi (kem ly do) de chu du an theo doi tien trinh. trangThai (tuy chon):
        /// "dang_xu_ly" (queued + processing) | "loi" | "san_sang".
        /// </summary>
        public async Task<PagedResponse<SampleResponse>> DanhSachMauAsync(
            Guid projectId, int page, int pageSize, IReadOnlyList<Guid>? ids, string? trangThai, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            // Chu du an / admin: ban goc khong watermark (du lieu cua ho) — van qua media-svc (khong lo khoa MinIO).
            Guid uid = caller.LayUserId();
            NguoiXem nguoi = caller.IsAdmin ? NguoiXem.Admin(uid) : NguoiXem.ChuDuAn(uid);

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            IQueryable<Sample> q = _db.Samples.IgnoreQueryFilters().AsNoTracking().Where(s => s.ProjectId == projectId);
            if (ids != null && ids.Count > 0)
            {
                if (ids.Count > 100)
                {
                    throw new InvalidValueException("qua_nhieu_mau", "Tối đa 100 mã mẫu mỗi lần.");
                }

                q = q.Where(s => ids.Contains(s.Id));
                page = 1;
                pageSize = 100;
            }

            if (trangThai == "dang_xu_ly")
            {
                q = q.Where(s => s.IngestState == TrangThaiNap.ChoXuLy || s.IngestState == TrangThaiNap.DangXuLy);
            }
            else if (trangThai == "loi")
            {
                q = q.Where(s => s.IngestState == TrangThaiNap.Loi);
            }
            else if (trangThai == "san_sang")
            {
                q = q.Where(s => s.IngestState == TrangThaiNap.SanSang);
            }
            else if (!string.IsNullOrEmpty(trangThai))
            {
                throw new InvalidValueException("trang_thai_khong_hop_le", "trangThai: dang_xu_ly | loi | san_sang.");
            }

            int tong = await q.CountAsync(ct);

            List<Sample> trang = await q
                .OrderByDescending(s => s.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            List<SampleResponse> items = new List<SampleResponse>();
            foreach (Sample s in trang)
            {
                bool sanSang = s.IngestState == TrangThaiNap.SanSang;
                items.Add(new SampleResponse
                {
                    Id = s.Id,
                    Modality = s.Modality,
                    OriginalName = s.OriginalName,
                    SizeBytes = s.SizeBytes,
                    FileUrl = sanSang && s.StorageKey != null ? _media.TaoLink(MauXem(s), nguoi, CheDoXem.Goc) : null,
                    Content = s.Content,
                    Question = s.Question,
                    Metadata = s.Metadata,
                    IngestState = s.IngestState,
                    IngestError = s.IngestError,
                    MediaState = s.MediaState,
                    MediaError = s.MediaError,
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

        /// <summary>
        /// Sua cau hoi RIENG cua mot mau (null / rong = bo). Chi khi du an con Nhap — doi cau
        /// hoi giua chung thi bai nop truoc va sau tra loi hai cau khac nhau. Mau dang nap cung sua duoc (vd vua
        /// upload ZIP xong). Trung (cung noi dung + cung cau hoi) voi mau khac → 409.
        /// </summary>
        public async Task<SampleQuestionResponse> DatCauHoiAsync(
            Guid projectId, Guid sampleId, DatCauHoiMauRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            Sample? mau = await _db.Samples.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == sampleId, ct);
            if (mau == null)
            {
                throw new Crowd.Project.Api.Exceptions.NotFoundException("Không tìm thấy mẫu.");
            }

            mau.DatCauHoi(body.Question);

            if (mau.Sha256 != null)
            {
                string sha = mau.Sha256;
                string? cauHoi = mau.Question;
                bool trung = await _db.Samples.IgnoreQueryFilters().AnyAsync(
                    s => s.ProjectId == projectId && s.Id != sampleId && s.Sha256 == sha && s.Question == cauHoi, ct);
                if (trung)
                {
                    throw new RuleViolationException(
                        "noi_dung_trung",
                        "Đã có mẫu khác cùng nội dung và cùng câu hỏi. Đặt câu hỏi khác để phân biệt.");
                }
            }

            await _db.SaveChangesAsync(ct);
            return new SampleQuestionResponse { SampleId = mau.Id, Question = mau.Question };
        }

        /// <summary>Khoa chong trung (noi dung + cau hoi rieng) cua cac mau da doc xong trong du an.</summary>
        private Task<HashSet<string>> KhoaTrungDaCoAsync(Guid projectId, CancellationToken ct)
        {
            return KhoaTrungDaCoAsync(_db, projectId, ct);
        }

        /// <summary>Nhu tren — ZipImporter (worker giai nen) dung chung.</summary>
        public static async Task<HashSet<string>> KhoaTrungDaCoAsync(ProjectDbContext db, Guid projectId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            var daCo = await db.Samples
                .Where(s => s.ProjectId == projectId && s.Sha256 != null)
                .Select(s => new { s.Sha256, s.Question })
                .ToListAsync(ct);
            HashSet<string> khoa = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in daCo)
            {
                khoa.Add(DongNap.KhoaTrung(m.Sha256!, m.Question));
            }

            return khoa;
        }

        /// <summary>Dem mau theo tien trinh nap — giao dien hien "da xu ly x / y" va biet khi nao xong.</summary>
        public async Task<TrangThaiNapResponse> TrangThaiNapAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            var nhom = await _db.Samples.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .GroupBy(s => s.IngestState)
                .Select(g => new { Trang = g.Key, So = g.Count() })
                .ToListAsync(ct);

            int Dem(string trang)
            {
                var m = nhom.FirstOrDefault(x => x.Trang == trang);
                return m == null ? 0 : m.So;
            }

            int video = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId && s.MediaState == TrangThaiMedia.DangChuanBi)
                .Select(s => s.StorageKey)
                .Distinct()
                .CountAsync(ct);

            // ZIP anh (3.41): moi file ZIP mot phieu — giao dien hien "dang giai nen x file", xong / loi kem ly do.
            List<ZipImport> zip = await _db.ZipImports.AsNoTracking()
                .Where(z => z.ProjectId == projectId)
                .OrderByDescending(z => z.CreatedAt)
                .Take(20)
                .ToListAsync(ct);

            return new TrangThaiNapResponse
            {
                Queued = Dem(TrangThaiNap.ChoXuLy),
                Processing = Dem(TrangThaiNap.DangXuLy),
                Ready = Dem(TrangThaiNap.SanSang),
                Failed = Dem(TrangThaiNap.Loi),
                PreparingVideo = video,
                ZipImports = zip.Select(ZipResponse).ToList(),
            };
        }

        /// <summary>
        /// Xoa cac mau nap loi (de nap lai) va phieu ZIP giai nen loi: chi khi du an con Nhap. Tra so mau da xoa.
        /// </summary>
        public async Task<int> XoaMauLoiAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            List<Sample> loi = await _db.Samples.IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId && s.IngestState == TrangThaiNap.Loi)
                .ToListAsync(ct);
            List<ZipImport> zipLoi = await _db.ZipImports
                .Where(z => z.ProjectId == projectId && z.State == TrangThaiZip.Loi)
                .ToListAsync(ct);
            if (loi.Count == 0 && zipLoi.Count == 0)
            {
                return 0;
            }

            _db.Samples.RemoveRange(loi);
            _db.ZipImports.RemoveRange(zipLoi);
            await _db.SaveChangesAsync(ct);

            // File chi con la rac neu khong mau nao khac dung chung khoa (video cat doan dung chung mot file).
            foreach (string khoa in loi.Where(s => s.StorageKey != null).Select(s => s.StorageKey!).Distinct())
            {
                bool conDung = await _db.Samples.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == projectId && s.StorageKey == khoa, ct);
                if (conDung)
                {
                    continue;
                }

                try
                {
                    await _storage.XoaAsync(khoa, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Khong xoa duoc file cua mau loi {Key}", khoa);
                }
            }

            return loi.Count;
        }

        /// <summary>Mau co file → thong tin de media-svc phat dung file (khoa nam trong token da ma hoa).</summary>
        public static MauCanXem MauXem(Sample s)
        {
            return MauXem(s, null);
        }

        /// <summary>
        /// Nhu tren, kem ten doanh nghiep (watermark noi anh / video) va doan cua mau trong file video (media-svc chi
        /// phat doan nay, moc 0 = dau mau): segmentStart / segmentEnd, khong cat thi 0 / durationSec.
        /// </summary>
        public static MauCanXem MauXem(Sample s, string? tenDoanhNghiep)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            double? batDau = null;
            double? ketThuc = null;
            if (s.Modality == Modalities.Video)
            {
                SampleMetadata md = SampleMetadata.Tu(s.Metadata);
                batDau = md.SegmentStart ?? 0;
                ketThuc = md.SegmentEnd ?? md.DurationSec;
            }

            return new MauCanXem
            {
                SampleId = s.Id,
                ProjectId = s.ProjectId,
                StorageKey = s.StorageKey ?? throw new InvalidOperationException("Mau khong co file."),
                Modality = s.Modality,
                TenDoanhNghiep = tenDoanhNghiep,
                BatDau = batDau,
                KetThuc = ketThuc,
            };
        }

        private async Task XoaFileAsync(string khoa)
        {
            try
            {
                // CancellationToken.None: request da hong / huy, nhung don dep van phai chay xong.
                await _storage.XoaAsync(khoa, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Khong xoa duoc file {Key}", khoa);
            }
        }
    }
}
