using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Media;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Xu ly MOT lo manifest (Pending → Ingesting → Ready / Failed). Goi tu
    /// DatasetIngestWorker.
    ///
    /// Moi DONG manifest thanh mot (hoac nhieu, neu cat doan) mau, theo loai du
    /// lieu cua du an:
    ///
    ///   image  {"file":key}  → doc file, kiem LOAI THAT bang noi dung (khong tin duoi
    ///                          file), lay rong x cao, bam SHA-256.
    ///   audio  {"file":key}  → bam SHA-256 theo LUONG (khong nap ca file vao RAM),
    ///   video                  do thoi luong / kich thuoc bang ffprobe qua link ky san;
    ///                          khong co ffprobe thi lay "durationSec" trong dong.
    ///                          Tap nhan co segmentSeconds thi CAT thanh nhieu mau.
    ///   text   {"text":...}  → noi dung luu thang vao database.
    ///   pair   {"a":...,"b":...,"prompt"?:...}
    ///
    /// Dong sai (file khong ton tai, sai loai, trung noi dung...) bi BO QUA kem ly
    /// do trong ErrorSummary — mot dong hong khong lam hong ca lo.
    /// </summary>
    public sealed class DatasetIngestor
    {
        public const int SoDongToiDa = 50000;
        public const int DoDaiTextToiDa = 100000;
        public const long AnhToiDa = 50L * 1024 * 1024;
        private const int SoLoiGhiLai = 20;

        private readonly ProjectDbContext _db;
        private readonly IObjectStorage _storage;
        private readonly IMediaProbe _probe;
        private readonly ProjectEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ILogger<DatasetIngestor> _logger;

        public DatasetIngestor(
            ProjectDbContext db,
            IObjectStorage storage,
            IMediaProbe probe,
            ProjectEventPublisher events,
            TimeProvider clock,
            ILogger<DatasetIngestor> logger)
        {
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

        /// <summary>Lay MOT lo dang cho va xu ly. false = khong co lo nao.</summary>
        public async Task<bool> XuLyMotLoAsync(CancellationToken ct)
        {
            Dataset? lo = await NhanViecAsync(ct);
            if (lo == null)
            {
                return false;
            }

            try
            {
                await XuLyAsync(lo, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Dung service giua chung: lo o lai Ingesting, worker dua ve Pending khi khoi dong lai.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Xu ly lo {DatasetId} that bai", lo.Id);

                _db.ChangeTracker.Clear();
                Dataset hong = await _db.Datasets.FirstAsync(d => d.Id == lo.Id, ct);
                hong.ThatBai("Loi he thong khi xu ly: " + ex.Message, _clock.GetUtcNow());
                await _db.SaveChangesAsync(ct);
            }

            return true;
        }

        /// <summary>
        /// Khoa MOT lo Pending (SKIP LOCKED — nhieu ban sao worker khong tranh nhau)
        /// va chuyen sang Ingesting trong transaction ngan, roi commit ngay: viec
        /// doc file vai GB khong duoc giu khoa dong suot thoi gian do.
        /// </summary>
        private async Task<Dataset?> NhanViecAsync(CancellationToken ct)
        {
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                Dataset? lo = await _db.Datasets
                    .FromSqlRaw("SELECT * FROM datasets WHERE status = 'Pending' ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED")
                    .FirstOrDefaultAsync(ct);

                if (lo != null)
                {
                    lo.BatDauXuLy();
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return lo;
            }
        }

        private async Task XuLyAsync(Dataset lo, CancellationToken ct)
        {
            LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == lo.ProjectId, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();

            List<JsonNode?> dong = await DocManifestAsync(lo, ct);

            HashSet<string> daCo = new HashSet<string>(
                await _db.Samples.Where(s => s.ProjectId == lo.ProjectId).Select(s => s.Sha256).ToListAsync(ct),
                StringComparer.Ordinal);

            List<Sample> moi = new List<Sample>();
            List<string> loi = new List<string>();
            int boQua = 0;

            for (int i = 0; i < dong.Count; i++)
            {
                try
                {
                    JsonObject? o = dong[i] as JsonObject;
                    if (o == null)
                    {
                        throw new InvalidValueException("dong_khong_hop_le", "dong phai la mot object JSON.");
                    }

                    foreach (Sample s in await TaoMauAsync(o, i, duAn, lo, bayGio, ct))
                    {
                        if (!daCo.Add(s.Sha256))
                        {
                            boQua++;
                            GhiLoi(loi, i, "trung noi dung voi mau da co — bo qua.");
                            continue;
                        }

                        moi.Add(s);
                    }
                }
                catch (InvalidValueException ex)
                {
                    boQua++;
                    GhiLoi(loi, i, ex.Message);
                }
                catch (LabelFormatException ex)
                {
                    boQua++;
                    GhiLoi(loi, i, ex.Message);
                }
            }

            string? tomTat = loi.Count == 0 ? null : string.Join("\n", loi) + (boQua > loi.Count ? "\n..." : string.Empty);

            _db.Samples.AddRange(moi);
            lo.HoanTat(moi.Count, boQua, tomTat, _clock.GetUtcNow());

            if (moi.Count > 0)
            {
                DatasetEvents.PhatCacLo(_events, lo.ProjectId, lo.Id, moi, Caller.HeThong(Guid.NewGuid(), null));
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Lo {DatasetId} ({Modality}): {SoMau} mau, bo qua {BoQua}",
                lo.Id,
                duAn.Modality,
                moi.Count,
                boQua);
        }

        // =====================================================================
        // DOC MANIFEST
        // =====================================================================

        private async Task<List<JsonNode?>> DocManifestAsync(Dataset lo, CancellationToken ct)
        {
            List<JsonNode?> dong = new List<JsonNode?>();

            if (lo.Manifest != null)
            {
                foreach (JsonNode? n in (JsonArray)lo.Manifest.Node())
                {
                    dong.Add(n == null ? null : n.DeepClone());
                }

                return dong;
            }

            // Manifest trong MinIO: .jsonl (moi dong mot object) — doc tung dong,
            // khong nap ca file. Hoac .json la mot mang.
            string khoa = lo.ManifestKey!;
            using (Stream luong = await _storage.MoDocAsync(khoa, ct))
            using (StreamReader r = new StreamReader(luong, Encoding.UTF8))
            {
                if (khoa.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    JsonArray? mang = JsonNode.Parse(await r.ReadToEndAsync(ct)) as JsonArray;
                    if (mang == null)
                    {
                        throw new InvalidValueException("manifest_khong_hop_le", "File .json phai la mot mang.");
                    }

                    foreach (JsonNode? n in mang)
                    {
                        dong.Add(n == null ? null : n.DeepClone());
                    }
                }
                else
                {
                    string? line;
                    while ((line = await r.ReadLineAsync(ct)) != null)
                    {
                        if (line.Trim().Length == 0)
                        {
                            continue;
                        }

                        try
                        {
                            dong.Add(JsonNode.Parse(line));
                        }
                        catch (JsonException)
                        {
                            dong.Add(null);
                        }

                        if (dong.Count > SoDongToiDa)
                        {
                            break;
                        }
                    }
                }
            }

            if (dong.Count > SoDongToiDa)
            {
                throw new InvalidValueException("manifest_qua_dai", "Manifest toi da " + SoDongToiDa + " dong.");
            }

            return dong;
        }

        // =====================================================================
        // MOT DONG → MAU
        // =====================================================================

        private async Task<List<Sample>> TaoMauAsync(
            JsonObject o, int viTri, LabelingProject duAn, Dataset lo, DateTimeOffset bayGio, CancellationToken ct)
        {
            string ten = ChuoiTuyChon(o, "name") ?? ("dong-" + (viTri + 1).ToString(CultureInfo.InvariantCulture));

            switch (duAn.Modality)
            {
                case Modalities.Text:
                    return new List<Sample> { TaoVanBan(o, ten, duAn, lo, bayGio) };
                case Modalities.Pair:
                    return new List<Sample> { TaoCap(o, ten, duAn, lo, bayGio) };
                case Modalities.Image:
                    return new List<Sample> { await TaoAnhAsync(o, ten, duAn, lo, bayGio, ct) };
                case Modalities.Audio:
                case Modalities.Video:
                    return await TaoMediaAsync(o, ten, duAn, lo, bayGio, ct);
                default:
                    throw new InvalidValueException("loai_du_lieu_khong_hop_le", "Loai du lieu " + duAn.Modality + " chua ho tro.");
            }
        }

        private static Sample TaoVanBan(JsonObject o, string ten, LabelingProject duAn, Dataset lo, DateTimeOffset bayGio)
        {
            string text = ChuoiBatBuoc(o, "text");
            if (text.Length > DoDaiTextToiDa)
            {
                throw new InvalidValueException("text_qua_dai", "van ban toi da " + DoDaiTextToiDa + " ky tu.");
            }

            RawJson noiDung = RawJson.Tu(new JsonObject { ["text"] = text });
            SampleMetadata md = new SampleMetadata { Length = text.Length };

            return Sample.TaoTuNoiDung(duAn.Id, lo.Id, Modalities.Text, noiDung, ten, Bam(noiDung.Json), md, bayGio);
        }

        private static Sample TaoCap(JsonObject o, string ten, LabelingProject duAn, Dataset lo, DateTimeOffset bayGio)
        {
            JsonObject noiDung = new JsonObject
            {
                ["a"] = ChuoiBatBuoc(o, "a"),
                ["b"] = ChuoiBatBuoc(o, "b"),
            };

            string? cauHoi = ChuoiTuyChon(o, "prompt");
            if (cauHoi != null)
            {
                noiDung["prompt"] = cauHoi;
            }

            RawJson nd = RawJson.Tu(noiDung);
            if (nd.Json.Length > DoDaiTextToiDa)
            {
                throw new InvalidValueException("text_qua_dai", "noi dung cap toi da " + DoDaiTextToiDa + " ky tu.");
            }

            return Sample.TaoTuNoiDung(duAn.Id, lo.Id, Modalities.Pair, nd, ten, Bam(nd.Json), SampleMetadata.Rong, bayGio);
        }

        private async Task<Sample> TaoAnhAsync(
            JsonObject o, string ten, LabelingProject duAn, Dataset lo, DateTimeOffset bayGio, CancellationToken ct)
        {
            string khoa = await KhoaFileAsync(o, duAn, ct);
            ThongTinFile tt = (await _storage.ThongTinAsync(khoa, ct))!;

            if (tt.SizeBytes > AnhToiDa)
            {
                throw new InvalidValueException("anh_qua_lon", "anh toi da 50 MB.");
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
                throw new InvalidValueException("khong_phai_anh", "file khong phai anh png / jpeg / webp hop le.");
            }

            SampleMetadata md = new SampleMetadata { Width = kichThuoc.Value.Width, Height = kichThuoc.Value.Height };
            return Sample.TaoTuFile(duAn.Id, lo.Id, Modalities.Image, khoa, ten, loai, noiDung.Length, Convert.ToHexStringLower(SHA256.HashData(noiDung)), md, bayGio);
        }

        private async Task<List<Sample>> TaoMediaAsync(
            JsonObject o, string ten, LabelingProject duAn, Dataset lo, DateTimeOffset bayGio, CancellationToken ct)
        {
            string khoa = await KhoaFileAsync(o, duAn, ct);
            ThongTinFile tt = (await _storage.ThongTinAsync(khoa, ct))!;

            // Bam theo LUONG: file vai GB khong nap vao RAM.
            string bamFile;
            using (Stream luong = await _storage.MoDocAsync(khoa, ct))
            {
                bamFile = Convert.ToHexStringLower(await SHA256.HashDataAsync(luong, ct));
            }

            bool laVideo = duAn.Modality == Modalities.Video;

            // ffprobe doc qua link ky san — chi tai phan can thiet cua file.
            ThongTinMedia? media = await _probe.DocAsync(await _storage.TaoLinkXemAsync(khoa), ct);
            if (media != null)
            {
                if (laVideo && !media.CoVideo)
                {
                    throw new InvalidValueException("khong_phai_video", "file khong co hinh anh (khong phai video).");
                }

                if (!laVideo && !media.CoAudio)
                {
                    throw new InvalidValueException("khong_phai_am_thanh", "file khong co am thanh.");
                }
            }

            double? thoiLuong = media != null && media.DurationSec.HasValue ? media.DurationSec : SoTuyChon(o, "durationSec");
            int? rong = media != null && media.Width.HasValue ? media.Width : (int?)SoTuyChon(o, "width");
            int? cao = media != null && media.Height.HasValue ? media.Height : (int?)SoTuyChon(o, "height");

            if (!thoiLuong.HasValue || thoiLuong.Value <= 0)
            {
                throw new InvalidValueException(
                    "khong_doc_duoc_thoi_luong",
                    "khong doc duoc thoi luong — cai ffprobe (FFmpeg) hoac khai \"durationSec\" trong dong manifest.");
            }

            string loai = tt.ContentType != null && tt.ContentType != "application/octet-stream"
                ? tt.ContentType
                : (laVideo ? "video/" : "audio/") + Path.GetExtension(khoa).TrimStart('.');

            List<Sample> ds = new List<Sample>();
            List<(double BatDau, double KetThuc)> doan = CatDoan(thoiLuong.Value, duAn.LabelSchema == null ? null : duAn.LabelSchema.SegmentSeconds);

            if (doan.Count == 1)
            {
                SampleMetadata md = new SampleMetadata { DurationSec = thoiLuong.Value, Width = rong, Height = cao };
                ds.Add(Sample.TaoTuFile(duAn.Id, lo.Id, duAn.Modality, khoa, ten, loai, tt.SizeBytes, bamFile, md, bayGio));
                return ds;
            }

            foreach ((double bd, double kt) in doan)
            {
                SampleMetadata md = new SampleMetadata
                {
                    DurationSec = kt - bd,
                    SegmentStart = bd,
                    SegmentEnd = kt,
                    SourceDurationSec = thoiLuong.Value,
                    Width = rong,
                    Height = cao,
                };

                // Moi doan mot dau van tay rieng: bam cua (file + vi tri doan).
                string bamDoan = Bam(bamFile + "#" + bd.ToString("0.###", CultureInfo.InvariantCulture));
                string tenDoan = ten + " [" + bd.ToString("0.#", CultureInfo.InvariantCulture) + "-" + kt.ToString("0.#", CultureInfo.InvariantCulture) + "s]";

                ds.Add(Sample.TaoTuFile(duAn.Id, lo.Id, duAn.Modality, khoa, tenDoan, loai, tt.SizeBytes, bamDoan, md, bayGio));
            }

            return ds;
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

        /// <summary>Khoa file trong dong: thuoc du an nay (chong BOLA) va da ton tai trong kho.</summary>
        private async Task<string> KhoaFileAsync(JsonObject o, LabelingProject duAn, CancellationToken ct)
        {
            string khoa = ChuoiBatBuoc(o, "file");

            if (!khoa.StartsWith(duAn.Id.ToString() + "/", StringComparison.Ordinal))
            {
                throw new InvalidValueException("file_khong_thuoc_du_an", "file '" + khoa + "' khong thuoc du an nay.");
            }

            if (await _storage.ThongTinAsync(khoa, ct) == null)
            {
                throw new InvalidValueException("khong_tim_thay_file", "khong tim thay file '" + khoa + "' (da upload chua?).");
            }

            return khoa;
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

        private static string ChuoiBatBuoc(JsonObject o, string ten)
        {
            string? s = ChuoiTuyChon(o, ten);
            if (s == null || s.Trim().Length == 0)
            {
                throw new InvalidValueException("thieu_truong", "thieu truong \"" + ten + "\".");
            }

            return s;
        }

        private static string? ChuoiTuyChon(JsonObject o, string ten)
        {
            JsonValue? v = o[ten] as JsonValue;
            string? s;
            return v != null && v.TryGetValue(out s) ? s : null;
        }

        private static double? SoTuyChon(JsonObject o, string ten)
        {
            JsonValue? v = o[ten] as JsonValue;
            double d;
            return v != null && v.TryGetValue(out d) && d > 0 ? d : null;
        }

        private static string Bam(string s)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        }

        private static void GhiLoi(List<string> loi, int viTri, string thongBao)
        {
            if (loi.Count < SoLoiGhiLai)
            {
                loi.Add("dong " + (viTri + 1).ToString(CultureInfo.InvariantCulture) + ": " + thongBao);
            }
        }
    }
}
