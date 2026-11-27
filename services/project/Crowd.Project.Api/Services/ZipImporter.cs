using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Datasets;
using Crowd.Project.Infrastructure.Media;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Giai nen MOT phieu ZIP anh dang cho (kien truc 3.41). Goi tu ZipImportWorker.
    ///
    ///   1. Khoa mot phieu queued (SKIP LOCKED — nhieu ban sao worker khong tranh nhau), dat processing, commit ngay.
    ///   2. Tai ZIP tu kho vao TEP TAM co tran dung luong (ZipArchive can luong tua duoc; doc thang luong MinIO thi no
    ///      chep ca file vao RAM).
    ///   3. Duyet tung anh (ZipImageReader: chong zip bomb, kiem magic bytes, bo rac macOS), bo trung voi mau da co,
    ///      day anh len kho tai khoa cua mau. Cu vai chuc anh ghi tien do (processed_count) de giao dien hien.
    ///   4. Ghi MOI mau + chot phieu trong MOT SaveChanges — ZIP hong giua chung thi khong mau nao duoc ghi, anh da day
    ///      bi don. Xong / hong deu xoa file ZIP khoi kho.
    ///
    /// Service tat giua chung: phieu o lai processing, worker dua ve queued luc khoi dong. Ma mau co dinh theo (phieu,
    /// vi tri anh) nen lam lai ghi de dung khoa cu, khong de file mo coi.
    /// </summary>
    public sealed class ZipImporter
    {
        /// <summary>Cu bay nhieu anh ghi tien do mot lan (mot cau UPDATE nho, tu commit).</summary>
        private const int GhiTienDoMoi = 50;

        private readonly ProjectDbContext _db;
        private readonly IObjectStorage _storage;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<ZipImporter> _logger;

        public ZipImporter(ProjectDbContext db, IObjectStorage storage, ISettings settings, TimeProvider clock, ILogger<ZipImporter> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
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
            _storage = storage;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>Lay MOT phieu dang cho va giai nen. false = khong con phieu nao.</summary>
        public async Task<bool> XuLyMotZipAsync(CancellationToken ct)
        {
            ZipImport? phieu = await NhanViecAsync(ct);
            if (phieu == null)
            {
                return false;
            }

            Guid id = phieu.Id;
            string khoaZip = phieu.StorageKey;

            try
            {
                await XuLyAsync(phieu, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Dung service giua chung: phieu o lai processing, worker dua ve queued khi khoi dong lai.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Giai nen ZIP {ZipId} that bai", id);
                await GhiLoiAsync(id, "Lỗi hệ thống khi giải nén: " + ex.Message, ct);
                await XoaFileAsync(khoaZip);
            }

            return true;
        }

        private async Task<ZipImport?> NhanViecAsync(CancellationToken ct)
        {
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                ZipImport? phieu = await _db.ZipImports
                    .FromSqlRaw("SELECT * FROM zip_imports WHERE state = 'queued' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED")
                    .FirstOrDefaultAsync(ct);

                if (phieu != null)
                {
                    phieu.BatDau(_clock.GetUtcNow());
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return phieu;
            }
        }

        private async Task XuLyAsync(ZipImport phieu, CancellationToken ct)
        {
            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(_settings);
            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == phieu.ProjectId, ct);
            if (duAn == null)
            {
                // Du an vua xoa (phieu xoa theo cascade) — khong con gi de lam.
                return;
            }

            try
            {
                duAn.KiemTraCoTheNapDuLieu();
            }
            catch (DomainException ex)
            {
                await KetThucLoiAsync(phieu, ex.Message, new List<string>(), quyDinh, ct);
                return;
            }

            string tepTam = Path.Combine(Path.GetTempPath(), "zip-" + phieu.Id.ToString("N") + ".zip");
            List<Sample> moi = new List<Sample>();
            List<string> khoaDaDay = new List<string>();
            int soTrung = 0;
            int soBoQua;

            try
            {
                string? loiTai = await TaiVeTepTamAsync(phieu.StorageKey, tepTam, ct);
                if (loiTai != null)
                {
                    await KetThucLoiAsync(phieu, loiTai, khoaDaDay, quyDinh, ct);
                    return;
                }

                // Dau van tay cac anh DA CO trong du an — bo qua anh trung ngay khi doc, khong day len kho lan nua.
                HashSet<string> daCo = await MauService.KhoaTrungDaCoAsync(_db, phieu.ProjectId, ct);
                DateTimeOffset bayGio = _clock.GetUtcNow();
                int viTri = 0;

                using (FileStream luong = new FileStream(tepTam, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
                {
                    soBoQua = await ZipImageReader.DuyetAsync(
                        luong,
                        QuyDinhTuSetting.Zip(_settings),
                        async anh =>
                        {
                            int i = viTri;
                            viTri = viTri + 1;

                            if (!daCo.Add(DongNap.KhoaTrung(anh.Sha256, null)))
                            {
                                soTrung = soTrung + 1;
                            }
                            else
                            {
                                // Kich thuoc anh — de kiem khung (bbox / polygon) nam trong anh.
                                (int Width, int Height)? kichThuoc = ImageHeader.DocKichThuoc(anh.Content);
                                SampleMetadata md = kichThuoc.HasValue
                                    ? new SampleMetadata { Width = kichThuoc.Value.Width, Height = kichThuoc.Value.Height }
                                    : SampleMetadata.Rong;

                                Sample mau = Sample.TaoAnhTrongZip(
                                    phieu.MaMauCuaAnh(i),
                                    phieu.ProjectId,
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
                                moi.Add(mau);
                            }

                            if (viTri % GhiTienDoMoi == 0)
                            {
                                await GhiTienDoAsync(phieu.Id, viTri, ct);
                            }
                        },
                        ct);
                }
            }
            catch (InvalidValueException ex)
            {
                // Khong phai ZIP, qua nhieu file, vuot tong dung luong giai nen: tu choi CA file (nhu luc con trong request).
                await KetThucLoiAsync(phieu, ex.Message, khoaDaDay, quyDinh, ct);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Loi ky thuat (kho tam sap...): don anh da day roi de XuLyMotZipAsync ghi loi phieu.
                await DonAnhAsync(khoaDaDay);
                throw;
            }
            finally
            {
                XoaTepTam(tepTam);
            }

            if (moi.Count == 0)
            {
                string lyDo = soTrung > 0
                    ? "Mọi ảnh trong ZIP đều trùng nội dung với mẫu đã có (" + soTrung + " ảnh)."
                    : "File ZIP không có ảnh hợp lệ nào (chỉ nhận jpg, png, webp).";
                await KetThucLoiAsync(phieu, lyDo, khoaDaDay, quyDinh, ct);
                return;
            }

            _db.Samples.AddRange(moi);
            phieu.HoanTat(moi.Count, soBoQua + soTrung, _clock.GetUtcNow());

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
            {
                // Mot anh trung noi dung voi mau vua nap song song (file khac xu ly cung luc): khong ghi gi ca.
                _db.ChangeTracker.Clear();
                await DonAnhAsync(khoaDaDay);
                await GhiLoiAsync(phieu.Id, "Có ảnh trùng nội dung với mẫu vừa nạp cùng lúc. Tải ZIP lên lại.", ct);
                await XoaFileAsync(phieu.StorageKey);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await DonAnhAsync(khoaDaDay);
                throw;
            }

            await XoaFileAsync(phieu.StorageKey);
            _logger.LogInformation(
                "Giai nen xong ZIP {ZipId} cua du an {ProjectId}: {SoMau} mau, bo qua {BoQua}",
                phieu.Id,
                phieu.ProjectId,
                moi.Count,
                soBoQua + soTrung);
        }

        /// <summary>
        /// Chep ZIP tu kho ra tep tam, dem byte THAT (link upload con song: file co the bi ghi de sau khi dang ky).
        /// Tra ly do loi cho nguoi dung, hoac null neu tai xong.
        /// </summary>
        private async Task<string?> TaiVeTepTamAsync(string khoa, string tepTam, CancellationToken ct)
        {
            long tran = _settings.SoLon(SettingKeys.DatasetZipMaxBytes);
            ThongTinFile? tt = await _storage.ThongTinAsync(khoa, ct);
            if (tt == null)
            {
                return "Không còn file ZIP trong kho.";
            }

            if (tt.SizeBytes > tran)
            {
                return "ZIP tối đa " + (tran / (1024 * 1024)) + " MB.";
            }

            using (Stream nguon = await _storage.MoDocAsync(khoa, ct))
            using (FileStream dich = new FileStream(tepTam, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                byte[] bo = new byte[81920];
                long daDoc = 0;
                while (true)
                {
                    int n = await nguon.ReadAsync(bo.AsMemory(), ct);
                    if (n == 0)
                    {
                        break;
                    }

                    daDoc = daDoc + n;
                    if (daDoc > tran)
                    {
                        return "ZIP tối đa " + (tran / (1024 * 1024)) + " MB.";
                    }

                    await dich.WriteAsync(bo.AsMemory(0, n), ct);
                }
            }

            return null;
        }

        private async Task KetThucLoiAsync(ZipImport phieu, string lyDo, List<string> khoaDaDay, QuyDinhDuLieu quyDinh, CancellationToken ct)
        {
            await DonAnhAsync(khoaDaDay);
            phieu.Loi(lyDo, quyDinh.DoDaiLoiToiDa, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            await XoaFileAsync(phieu.StorageKey);
            _logger.LogInformation("ZIP {ZipId} cua du an {ProjectId} khong nap duoc: {LyDo}", phieu.Id, phieu.ProjectId, lyDo);
        }

        /// <summary>Ghi loi len phieu (sau khi ChangeTracker da sach): doc lai phieu roi danh dau failed.</summary>
        private async Task GhiLoiAsync(Guid id, string lyDo, CancellationToken ct)
        {
            _db.ChangeTracker.Clear();
            ZipImport? phieu = await _db.ZipImports.FirstOrDefaultAsync(z => z.Id == id, ct);
            if (phieu == null)
            {
                return;
            }

            phieu.Loi(lyDo, QuyDinhTuSetting.DuLieu(_settings).DoDaiLoiToiDa, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Tien do cho giao dien — UPDATE rieng, tu commit (cac mau moi van nam trong change tracker cho buoc cuoi).</summary>
        private async Task GhiTienDoAsync(Guid id, int daDoc, CancellationToken ct)
        {
            await _db.ZipImports
                .Where(z => z.Id == id && z.State == TrangThaiZip.DangXuLy)
                .ExecuteUpdateAsync(u => u.SetProperty(z => z.ProcessedCount, daDoc), ct);
        }

        private async Task DonAnhAsync(List<string> khoa)
        {
            foreach (string k in khoa)
            {
                await XoaFileAsync(k);
            }
        }

        private async Task XoaFileAsync(string khoa)
        {
            try
            {
                // CancellationToken.None: dang tat / da hong, nhung don dep van phai chay xong.
                await _storage.XoaAsync(khoa, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Khong xoa duoc file {Key}", khoa);
            }
        }

        private void XoaTepTam(string duongDan)
        {
            try
            {
                if (File.Exists(duongDan))
                {
                    File.Delete(duongDan);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Khong xoa duoc tep tam {Path}", duongDan);
            }
        }
    }
}
