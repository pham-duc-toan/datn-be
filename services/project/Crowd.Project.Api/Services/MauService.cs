using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
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
    /// Nap mau vao du an (FB-11) va xem mau. KHONG co bang "lo nap" — moi mau tu mang tien trinh cua no
    /// (Sample.IngestState: queued → processing → ready | failed):
    ///
    ///   - ZIP anh        : doc + day anh len MinIO NGAY trong request → mau ready.
    ///   - Dong JSON      : text / pair / list → mau ready ngay; file (anh / am thanh / video da upload thang len
    ///                      MinIO bang link ky san) → mau "queued", SampleIngestor xu ly nen.
    ///   - Nguon ngoai    : liet ke nguon → moi file mot mau "queued" kem link tai, worker tai ve roi xu ly.
    ///
    /// Duong ZIP: thu tu doc ZIP → day tung anh len MinIO → ghi cac dong samples trong MOT SaveChanges. Anh len
    /// MinIO TRUOC khi commit: neu commit hong thi don anh da day (khong de file mo coi).
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
        // ZIP anh — xu ly ngay trong request
        // =====================================================================

        public async Task<NapMauResponse> NapZipAsync(Guid projectId, Stream zip, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            if (duAn.Modality != Modalities.Image)
            {
                throw new RuleViolationException(
                    "zip_chi_cho_anh",
                    "Upload ZIP chỉ dành cho dự án ảnh. Dữ liệu " + duAn.Modality + " dùng upload thẳng.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(_settings);

            // Dau van tay cac anh DA CO trong du an — bo qua anh trung ngay khi doc,
            // khong day len MinIO lan nua.
            List<string?> daCoList = await _db.Samples
                .Where(s => s.ProjectId == projectId && s.Sha256 != null)
                .Select(s => s.Sha256)
                .ToListAsync(ct);
            HashSet<string> daCo = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? h in daCoList)
            {
                daCo.Add(h!);
            }

            List<Sample> mauMoi = new List<Sample>();
            List<string> khoaDaDay = new List<string>();
            int soTrung = 0;
            int soBoQua = 0;

            try
            {
                soBoQua = await ZipImageReader.DuyetAsync(
                    zip,
                    QuyDinhTuSetting.Zip(_settings),
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
                            anh.OriginalName,
                            anh.ContentType,
                            anh.Extension,
                            anh.Content.Length,
                            anh.Sha256,
                            md,
                            bayGio,
                            quyDinh);

                        string khoa = mau.StorageKey!;
                        await _storage.LuuAsync(khoa, anh.Content, anh.ContentType, ct);
                        khoaDaDay.Add(khoa);
                        mauMoi.Add(mau);
                    },
                    ct);

                if (mauMoi.Count == 0)
                {
                    throw new RuleViolationException(
                        "khong_co_anh_hop_le",
                        "File ZIP không có ảnh hợp lệ nào (chỉ nhận jpg, png, webp).");
                }

                _db.Samples.AddRange(mauMoi);
                await _db.SaveChangesAsync(ct);
            }
            catch
            {
                await DonAnhMoCoiAsync(khoaDaDay);
                throw;
            }

            _logger.LogInformation("Da nap ZIP cho du an {ProjectId}: {SoMau} mau, bo qua {SoBoQua}", projectId, mauMoi.Count, soBoQua + soTrung);

            return new NapMauResponse
            {
                Received = mauMoi.Count + soBoQua + soTrung,
                Ready = mauMoi.Count,
                Queued = 0,
                Skipped = soBoQua + soTrung,
                Errors = new List<string>(),
                Accepted = new List<MauNhanResponse>(),
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
                List<string?> shaDaCo = await _db.Samples
                    .Where(s => s.ProjectId == projectId && s.Sha256 != null)
                    .Select(s => s.Sha256)
                    .ToListAsync(ct);
                foreach (string? h in shaDaCo)
                {
                    daCo.Add(h!);
                }
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
                        moi.Add(cho);
                        nhan.Add(new MauNhanResponse { Index = i, SampleId = cho.Id, Name = cho.OriginalName });
                    }
                    else
                    {
                        Sample mau = DongNap.TaoMauNoiDung(o, duAn.Modality, ten, projectId, bayGio, _settings);
                        if (!daCo.Add(mau.Sha256!))
                        {
                            throw new InvalidValueException("noi_dung_trung", "trùng nội dung với mẫu đã có.");
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

            return new TrangThaiNapResponse
            {
                Queued = Dem(TrangThaiNap.ChoXuLy),
                Processing = Dem(TrangThaiNap.DangXuLy),
                Ready = Dem(TrangThaiNap.SanSang),
                Failed = Dem(TrangThaiNap.Loi),
                PreparingVideo = video,
            };
        }

        /// <summary>Xoa cac mau nap loi (de nap lai): chi khi du an con Nhap. Tra so mau da xoa.</summary>
        public async Task<int> XoaMauLoiAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            List<Sample> loi = await _db.Samples.IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId && s.IngestState == TrangThaiNap.Loi)
                .ToListAsync(ct);
            if (loi.Count == 0)
            {
                return 0;
            }

            _db.Samples.RemoveRange(loi);
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
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new MauCanXem
            {
                SampleId = s.Id,
                ProjectId = s.ProjectId,
                StorageKey = s.StorageKey ?? throw new InvalidOperationException("Mau khong co file."),
                Modality = s.Modality,
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
    }
}
