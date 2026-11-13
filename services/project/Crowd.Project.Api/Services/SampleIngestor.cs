using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Import;
using Crowd.Project.Infrastructure.Media;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Xu ly MOT mau dang cho (queued → processing → ready | failed). Goi tu SampleIngestWorker.
    ///
    /// Mau co the den tu hai noi (hai cach tao Sample dang "queued"):
    ///   - File client DA upload thang len MinIO bang link ky san (MauService.ThemMauAsync).
    ///   - File o nguon ngoai (URL / Drive / S3): Sample.IngestSource la link tai — buoc dau tien la tai file ve
    ///     StorageKey (NhapNguonNgoaiService.TaiVeAsync).
    ///
    /// Sau do theo loai du lieu cua du an:
    ///
    ///   image  → doc file, kiem LOAI THAT bang noi dung (khong tin duoi file), lay rong x cao, bam SHA-256.
    ///   audio  → bam SHA-256 theo LUONG (khong nap ca file vao RAM), do thoi luong bang ffprobe qua link ky
    ///   video    san; khong co ffprobe thi lay "durationSec" client khai. Tap nhan co segmentSeconds thi CAT
    ///            thanh nhieu mau (mau cho giu doan dau, cac doan sau them moi).
    ///
    /// File hong / sai loai / trung noi dung → mau failed kem ly do (chu du an thay trong danh sach mau); mot file
    /// hong khong lam hong cac file khac.
    /// </summary>
    public sealed class SampleIngestor
    {
        private readonly ProjectDbContext _db;
        private readonly IObjectStorage _storage;
        private readonly IMediaProbe _probe;
        private readonly ProjectEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ILogger<SampleIngestor> _logger;
        private readonly ISettings _settings;
        private readonly NhapNguonNgoaiService _nhapNgoai;

        public SampleIngestor(
            ProjectDbContext db,
            IObjectStorage storage,
            IMediaProbe probe,
            ProjectEventPublisher events,
            TimeProvider clock,
            ILogger<SampleIngestor> logger,
            ISettings settings,
            NhapNguonNgoaiService nhapNgoai)
        {
            if (nhapNgoai == null)
            {
                throw new ArgumentNullException(nameof(nhapNgoai));
            }

            _nhapNgoai = nhapNgoai;

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
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
            _probe = probe;
            _events = events;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>Lay MOT mau dang cho va xu ly. false = khong con mau nao.</summary>
        public async Task<bool> XuLyMotMauAsync(CancellationToken ct)
        {
            Sample? mau = await NhanViecAsync(ct);
            if (mau == null)
            {
                return false;
            }

            Guid projectId = mau.ProjectId;
            Guid sampleId = mau.Id;

            try
            {
                await XuLyAsync(mau, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Dung service giua chung: mau o lai processing, worker dua ve queued khi khoi dong lai.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Xu ly mau {SampleId} that bai", sampleId);
                await GhiLoiAsync(projectId, sampleId, "Lỗi hệ thống khi xử lý: " + ex.Message, ct);
            }

            return true;
        }

        /// <summary>
        /// Khoa MOT mau queued (SKIP LOCKED — nhieu ban sao worker khong tranh nhau) va chuyen sang processing
        /// trong transaction ngan, roi commit ngay: tai / doc file vai GB khong duoc giu khoa dong suot thoi gian do.
        /// </summary>
        private async Task<Sample?> NhanViecAsync(CancellationToken ct)
        {
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                Sample? mau = await _db.Samples
                    .FromSqlRaw("SELECT * FROM samples WHERE ingest_state = 'queued' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED")
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ct);

                if (mau != null)
                {
                    mau.BatDauNap();
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return mau;
            }
        }

        private async Task XuLyAsync(Sample mau, CancellationToken ct)
        {
            LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == mau.ProjectId, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(_settings);
            int doDaiLoi = quyDinh.DoDaiLoiToiDa;
            List<Sample> doanThem = new List<Sample>();

            try
            {
                if (mau.IngestSource != null)
                {
                    await _nhapNgoai.TaiVeAsync(mau, ct);
                }

                if (mau.Modality == Modalities.Image)
                {
                    await DocAnhAsync(mau, ct);
                }
                else
                {
                    doanThem = await DocMediaAsync(mau, duAn, bayGio, quyDinh, ct);
                }
            }
            catch (InvalidValueException ex)
            {
                mau.NapLoi(ex.Message, doDaiLoi);
            }
            catch (LabelFormatException ex)
            {
                mau.NapLoi(ex.Message, doDaiLoi);
            }
            catch (NguonNgoaiException ex)
            {
                mau.NapLoi("nguồn: " + ex.Message, doDaiLoi);
            }

            if (mau.IngestState == TrangThaiNap.SanSang)
            {
                // Trung noi dung VA cau hoi rieng voi mau da san sang → khong nhan doi cau hoi
                // (UNIQUE project_id + sha256 + question). Cung file hoi cau khac nhau la hai mau.
                string? cauHoi = mau.Question;
                bool trung = await _db.Samples.AnyAsync(
                    s => s.ProjectId == mau.ProjectId && s.Sha256 == mau.Sha256 && s.Question == cauHoi && s.Id != mau.Id, ct);
                if (trung)
                {
                    mau.NapLoi("Trùng nội dung (và câu hỏi) với một mẫu đã có trong dự án.", doDaiLoi);
                    doanThem.Clear();
                }
            }

            if (mau.IngestState == TrangThaiNap.SanSang && doanThem.Count > 0)
            {
                _db.Samples.AddRange(doanThem);
            }

            if (mau.IngestState == TrangThaiNap.SanSang && mau.Modality == Modalities.Video)
            {
                // Chi video moi can nghe: media-svc chuan hoa ban chuan (watermark chi nhung khung dau + cuoi).
                List<IngestedSample> trongLo = new List<IngestedSample> { ChoMedia(mau) };
                foreach (Sample s in doanThem)
                {
                    trongLo.Add(ChoMedia(s));
                }

                _events.Phat(Caller.HeThong(Guid.NewGuid(), null), new SamplesIngested { ProjectId = mau.ProjectId, Samples = trongLo });
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (LaTrungDauVanTay(ex))
            {
                // Hai mau cung noi dung xu ly song song: mau ket thuc sau thua o rang buoc UNIQUE.
                _db.ChangeTracker.Clear();
                await GhiLoiAsync(mau.ProjectId, mau.Id, "Trùng nội dung với một mẫu đã có trong dự án.", ct);
                return;
            }

            _logger.LogInformation(
                "Mau {SampleId} ({Modality}) cua du an {ProjectId}: {TrangThai}{Them}",
                mau.Id,
                mau.Modality,
                mau.ProjectId,
                mau.IngestState,
                doanThem.Count > 0 ? " + " + doanThem.Count.ToString(CultureInfo.InvariantCulture) + " doan cat" : string.Empty);
        }

        // =====================================================================
        // ANH
        // =====================================================================

        private async Task DocAnhAsync(Sample mau, CancellationToken ct)
        {
            string khoa = mau.StorageKey!;
            ThongTinFile? tt = await _storage.ThongTinAsync(khoa, ct);
            if (tt == null)
            {
                throw new InvalidValueException("khong_tim_thay_file", "không tìm thấy file (đã upload xong chưa?).");
            }

            long toiDa = _settings.SoLon(SettingKeys.DatasetImageMaxBytes);
            if (tt.SizeBytes > toiDa)
            {
                throw new InvalidValueException("anh_qua_lon", "ảnh tối đa " + (toiDa / (1024 * 1024)) + " MB.");
            }

            byte[] noiDung;
            using (Stream luong = await _storage.MoDocAsync(khoa, ct))
            using (MemoryStream ms = new MemoryStream())
            {
                await luong.CopyToAsync(ms, ct);
                noiDung = ms.ToArray();
            }

            // Loai THAT theo noi dung — doi "virus.exe" thanh "anh.png" van bi loai.
            (int Width, int Height)? kichThuoc = ImageHeader.DocKichThuoc(noiDung);
            string? loai = LoaiAnh(noiDung);
            if (!kichThuoc.HasValue || loai == null)
            {
                throw new InvalidValueException("khong_phai_anh", "file không phải ảnh png / jpeg / webp hợp lệ.");
            }

            SampleMetadata md = new SampleMetadata { Width = kichThuoc.Value.Width, Height = kichThuoc.Value.Height };
            mau.HoanTatNap(Convert.ToHexStringLower(SHA256.HashData(noiDung)), noiDung.Length, loai, md);
        }

        // =====================================================================
        // AM THANH / VIDEO
        // =====================================================================

        /// <summary>Doc file am thanh / video; tra cac mau THEM (doan cat thu 2 tro di) — mau goc giu doan dau.</summary>
        private async Task<List<Sample>> DocMediaAsync(Sample mau, LabelingProject duAn, DateTimeOffset bayGio, QuyDinhDuLieu quyDinh, CancellationToken ct)
        {
            string khoa = mau.StorageKey!;
            ThongTinFile? tt = await _storage.ThongTinAsync(khoa, ct);
            if (tt == null)
            {
                throw new InvalidValueException("khong_tim_thay_file", "không tìm thấy file (đã upload xong chưa?).");
            }

            // Bam theo LUONG: file vai GB khong nap vao RAM.
            string bamFile;
            using (Stream luong = await _storage.MoDocAsync(khoa, ct))
            {
                bamFile = Convert.ToHexStringLower(await SHA256.HashDataAsync(luong, ct));
            }

            bool laVideo = duAn.Modality == Modalities.Video;
            SampleMetadata goiY = SampleMetadata.Tu(mau.Metadata);

            // ffprobe doc qua link ky san NOI BO (dia chi service, khong phai PublicUrl) — chi tai
            // phan can thiet cua file.
            ThongTinMedia? media = await _probe.DocAsync(await _storage.TaoLinkNoiBoAsync(khoa), _settings.ThoiGian(SettingKeys.MediaFfprobeTimeout), ct);
            if (media != null)
            {
                if (laVideo && !media.CoVideo)
                {
                    throw new InvalidValueException("khong_phai_video", "file không có hình ảnh (không phải video).");
                }

                if (!laVideo && !media.CoAudio)
                {
                    throw new InvalidValueException("khong_phai_am_thanh", "file không có âm thanh.");
                }
            }

            double? thoiLuong = media != null && media.DurationSec.HasValue ? media.DurationSec : goiY.DurationSec;
            int? rong = media != null && media.Width.HasValue ? media.Width : goiY.Width;
            int? cao = media != null && media.Height.HasValue ? media.Height : goiY.Height;
            double? fps = laVideo ? (media != null && media.Fps.HasValue ? media.Fps : goiY.Fps) : null;

            if (!thoiLuong.HasValue || thoiLuong.Value <= 0)
            {
                throw new InvalidValueException(
                    "khong_doc_duoc_thoi_luong",
                    "không đọc được thời lượng — cài ffprobe (FFmpeg) hoặc khai \"durationSec\" trong dòng dữ liệu.");
            }

            string loai = tt.ContentType != null && tt.ContentType != "application/octet-stream"
                ? tt.ContentType
                : (laVideo ? "video/" : "audio/") + Path.GetExtension(khoa).TrimStart('.');

            List<(double BatDau, double KetThuc)> doan = CatDoan(thoiLuong.Value, duAn.LabelSchema == null ? null : duAn.LabelSchema.SegmentSeconds);
            List<Sample> them = new List<Sample>();

            if (doan.Count == 1)
            {
                mau.HoanTatNap(bamFile, tt.SizeBytes, loai, new SampleMetadata { DurationSec = thoiLuong.Value, Width = rong, Height = cao, Fps = fps });
                return them;
            }

            // Doan dau giu o chinh mau nay; moi doan co dau van tay rieng: bam cua (file + vi tri doan).
            string ten = mau.OriginalName;
            for (int i = 0; i < doan.Count; i++)
            {
                (double bd, double kt) = doan[i];
                SampleMetadata md = new SampleMetadata
                {
                    DurationSec = kt - bd,
                    SegmentStart = bd,
                    SegmentEnd = kt,
                    SourceDurationSec = thoiLuong.Value,
                    Width = rong,
                    Height = cao,
                    Fps = fps,
                };
                string bamDoan = DongNap.Bam(bamFile + "#" + bd.ToString("0.###", CultureInfo.InvariantCulture));
                string tenDoan = ten + " [" + bd.ToString("0.#", CultureInfo.InvariantCulture) + "-" + kt.ToString("0.#", CultureInfo.InvariantCulture) + "s]";

                if (i == 0)
                {
                    mau.DatTen(tenDoan, quyDinh);
                    mau.HoanTatNap(bamDoan, tt.SizeBytes, loai, md);
                }
                else
                {
                    Sample doanMoi = Sample.TaoTuFile(duAn.Id, duAn.Modality, khoa, tenDoan, loai, tt.SizeBytes, bamDoan, md, bayGio, quyDinh);
                    // Doan cat ra hoi cung cau voi file goc.
                    doanMoi.DatCauHoi(mau.Question);
                    them.Add(doanMoi);
                }
            }

            return them;
        }

        /// <summary>
        /// Cat [0, thoiLuong] thanh cac doan segmentSeconds giay. Khong cat neu tap
        /// nhan khong khai segmentSeconds hoac file ngan hon mot doan. Doan cuoi
        /// ngan hon 1 giay thi gop vao doan truoc (khong ai gan nhan duoc 0,3 giay).
        /// </summary>
        public static List<(double BatDau, double KetThuc)> CatDoan(double thoiLuong, int? segmentSeconds)
        {
            List<(double, double)> doan = new List<(double, double)>();

            if (!segmentSeconds.HasValue || thoiLuong <= segmentSeconds.Value + 0.5)
            {
                doan.Add((0, thoiLuong));
                return doan;
            }

            int seg = segmentSeconds.Value;
            for (double bd = 0; bd < thoiLuong - 0.001; bd += seg)
            {
                doan.Add((bd, Math.Min(bd + seg, thoiLuong)));
            }

            int cuoi = doan.Count - 1;
            if (doan.Count >= 2 && doan[cuoi].Item2 - doan[cuoi].Item1 < 1)
            {
                doan[cuoi - 1] = (doan[cuoi - 1].Item1, doan[cuoi].Item2);
                doan.RemoveAt(cuoi);
            }

            return doan;
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        private static IngestedSample ChoMedia(Sample s)
        {
            return new IngestedSample
            {
                SampleId = s.Id,
                Modality = s.Modality,
                StorageKey = s.StorageKey,
                Content = s.Content,
                Metadata = s.Metadata,
            };
        }

        /// <summary>Ghi loi len mau (sau khi ChangeTracker da sach): doc lai mau tu database roi danh dau failed.</summary>
        private async Task GhiLoiAsync(Guid projectId, Guid sampleId, string lyDo, CancellationToken ct)
        {
            _db.ChangeTracker.Clear();
            Sample hong = await _db.Samples.IgnoreQueryFilters().FirstAsync(s => s.ProjectId == projectId && s.Id == sampleId, ct);
            hong.NapLoi(lyDo, QuyDinhTuSetting.DuLieu(_settings).DoDaiLoiToiDa);
            await _db.SaveChangesAsync(ct);
        }

        private static bool LaTrungDauVanTay(DbUpdateException ex)
        {
            Exception? e = ex;
            while (e != null)
            {
                if (e.Message.Contains("ux_samples_project_sha256", StringComparison.Ordinal))
                {
                    return true;
                }

                e = e.InnerException;
            }

            return false;
        }

        private static string? LoaiAnh(byte[] b)
        {
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                return "image/png";
            }

            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            {
                return "image/webp";
            }

            return null;
        }
    }
}
