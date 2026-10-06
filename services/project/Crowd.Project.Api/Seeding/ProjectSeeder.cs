using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Seeding
{
    /// <summary>
    /// Seed DU AN cua kich ban: du an, thanh vien, dataset + mau (anh PNG / am
    /// thanh WAV len MinIO that; van ban / cap luu noi dung), cau hoi vang.
    ///
    /// Moi buoc di qua PHUONG THUC DOMAIN that (Tao, DatCauHinhGia, YeuCauPublish,
    /// XacNhanDaKyQuy, Duyet...) — kich ban sai luat (vd ngan sach khong du ky
    /// quy) thi seed NEM LOI ngay, khong the tao ra du an o trang thai vo ly.
    ///
    /// Thoi gian "lui ve qua khu" theo kich ban: P1 tao 7 ngay truoc, ky quy 6
    /// ngay truoc, duyet 5 ngay 23 gio truoc...
    /// </summary>
    public sealed class ProjectSeeder
    {
        /// <summary>Anh mau 256 x 256 — du nhin ro tren giao dien, chi vai KB.</summary>
        private const int KichThuocAnh = 256;

        private readonly ProjectDbContext _db;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;
        private readonly ILogger<ProjectSeeder> _logger;

        public ProjectSeeder(ProjectDbContext db, IObjectStorage storage, TimeProvider clock, ILogger<ProjectSeeder> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _storage = storage;
            _clock = clock;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Idempotent THEO TUNG DU AN: DB cu da co P1-P4 thi van them P5-P7 khi
            // kich ban moi bo sung du an.
            List<Guid> ids = KichBanSeed.Projects.Select(p => p.Id).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(
                await _db.Projects.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct));

            DateTimeOffset bayGio = _clock.GetUtcNow();
            int soDuAn = 0;
            int soMau = 0;
            HashSet<string> fileDaDay = new HashSet<string>(StringComparer.Ordinal);

            foreach (SeedProject sp in KichBanSeed.Projects)
            {
                if (daCo.Contains(sp.Id))
                {
                    continue;
                }

                soMau = soMau + await TaoDuAnAsync(sp, bayGio, fileDaDay, ct);
                soDuAn++;
            }

            if (soDuAn == 0)
            {
                _logger.LogInformation("Seed project: da du du an seed — bo qua");
                return;
            }

            // Mot SaveChanges cho ca kich ban: hong o dau thi khong luu gi ca.
            // File da day len MinIO van nam do — khoa tat dinh nen lan seed sau ghi de.
            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Seed project: tao {SoDuAn} du an, {SoMau} mau", soDuAn, soMau);
        }

        private async Task<int> TaoDuAnAsync(SeedProject sp, DateTimeOffset bayGio, HashSet<string> fileDaDay, CancellationToken ct)
        {
            DateTimeOffset lucTao = bayGio - sp.CreatedAgo;

            // ---- 1. Du an Nhap + cau hinh day du ----
            LabelingProject duAn = LabelingProject.Tao(
                sp.OwnerId,
                sp.Name,
                sp.Description,
                sp.Modality,
                sp.IsPrivate ? ProjectVisibility.Private : ProjectVisibility.Public,
                lucTao);
            SeedIds.GanId(duAn, sp.Id);

            LabelSchema tapNhan = sp.TapNhan();
            duAn.DatLabelSchema(tapNhan, lucTao);
            duAn.DatHuongDan(Guideline.Tao(sp.GuidelineMarkdown, null), lucTao);
            duAn.DatCauHinhGia(sp.UnitPriceVnd, sp.Redundancy, sp.BudgetVnd, sp.Deadline(bayGio), lucTao);
            duAn.DatKenhPhanPhoi(true, false, false, lucTao);
            duAn.DatDieuKienThamGia(
                null,
                null,
                sp.RequireEntranceTest,
                sp.EntranceQuestionCount,
                sp.EntrancePassPercent,
                lucTao);

            _db.Projects.Add(duAn);

            // ---- 2. Dataset + mau ----
            Dataset lo = Dataset.Tao(sp.Id, "Lo du lieu mau (seed)", lucTao);
            SeedIds.GanId(lo, sp.DatasetId);

            foreach (SeedSample s in sp.Samples)
            {
                Sample mau = await TaoMauAsync(sp, s, lucTao, fileDaDay, ct);
                SeedIds.GanId(mau, s.Id);

                // Khoa file do kich ban dat — khop khoa ma task-svc / annotation-svc seed.
                if (s.StorageKey != null)
                {
                    SeedIds.GanThuocTinh(mau, "StorageKey", s.StorageKey);
                }

                _db.Samples.Add(mau);
            }

            lo.GhiNhanKetQuaNap(sp.Samples.Count, 0);
            _db.Datasets.Add(lo);

            // ---- 3. Cau hoi vang — kiem bang tap nhan + metadata nhu API that ----
            foreach (SeedGold g in sp.Gold)
            {
                LabelPayload dapAn = LabelPayload.Tao(tapNhan, g.PayloadJson, sp.Mau(g.SampleId).Metadata);

                GoldItem vang = GoldItem.Tao(
                    sp.Id,
                    g.SampleId,
                    dapAn,
                    g.ForEntranceTest ? GoldPurpose.EntranceTest : GoldPurpose.QualityCheck,
                    lucTao);
                SeedIds.GanId(vang, g.Id);
                _db.GoldItems.Add(vang);
            }

            // ---- 4. Vong doi: publish → ky quy → duyet ----
            if (sp.DaKyQuy)
            {
                DateTimeOffset lucKyQuy = sp.LucKyQuy(bayGio);
                int soCauTest = sp.Gold.Count(g => g.ForEntranceTest);

                duAn.YeuCauPublish(sp.Samples.Count, soCauTest, KichBanSeed.PhanTramPhi, lucKyQuy);
                duAn.XacNhanDaKyQuy(lucKyQuy);
            }

            DateTimeOffset lucVaoDuAn = lucTao;
            if (sp.Stage == SeedStage.Running)
            {
                DateTimeOffset lucDuyet = sp.LucDuyet(bayGio);
                duAn.Duyet(lucDuyet);

                // Labeler chi vao duoc du an dang chay.
                lucVaoDuAn = lucDuyet + TimeSpan.FromHours(1);
            }

            // ---- 5. Thanh vien ----
            _db.ProjectMembers.Add(ProjectMember.TaoChuSoHuu(sp.Id, sp.OwnerId, lucTao));

            foreach (SeedMember m in sp.Members)
            {
                MemberRole vaiTro = m.Role == SeedMemberRole.Reviewer ? MemberRole.Reviewer : MemberRole.Labeler;
                _db.ProjectMembers.Add(ProjectMember.TaoThanhVien(sp.Id, m.UserId, vaiTro, lucVaoDuAn));
            }

            return sp.Samples.Count;
        }

        private async Task<Sample> TaoMauAsync(
            SeedProject sp, SeedSample s, DateTimeOffset luc, HashSet<string> fileDaDay, CancellationToken ct)
        {
            switch (s.Modality)
            {
                case Modalities.Image:
                {
                    byte[] png = AnhMauPng.Tao(s.R, s.G, s.B, KichThuocAnh);
                    await _storage.LuuAsync(s.StorageKey!, png, AnhMauPng.ContentType, ct);

                    return Sample.TaoAnhTrongZip(
                        sp.Id, sp.DatasetId, s.FileName, AnhMauPng.ContentType, ".png", png.Length, Bam(png), s.Metadata, luc);
                }

                case Modalities.Audio:
                {
                    // Cac doan cat tu MOT file dung chung khoa — chi day file mot lan.
                    byte[] wav = AmThanhMau.TaoWav(s.ToneHz, s.FileSeconds);
                    if (fileDaDay.Add(s.StorageKey!))
                    {
                        await _storage.LuuAsync(s.StorageKey!, wav, AmThanhMau.ContentType, ct);
                    }

                    // Dau van tay doan = bam(file + "#" + diem bat dau) — cung cong thuc DatasetIngestor.
                    string sha = Bam(wav);
                    if (s.Metadata.SegmentStart.HasValue)
                    {
                        sha = Bam(Encoding.UTF8.GetBytes(sha + "#" + s.Metadata.SegmentStart.Value.ToString("0.###", CultureInfo.InvariantCulture)));
                    }

                    return Sample.TaoTuFile(
                        sp.Id, sp.DatasetId, Modalities.Audio, s.StorageKey!, s.FileName, AmThanhMau.ContentType, wav.Length, sha, s.Metadata, luc);
                }

                default:
                {
                    string noiDung = s.ContentJson!;
                    return Sample.TaoTuNoiDung(
                        sp.Id, sp.DatasetId, s.Modality, RawJson.Tu(noiDung), s.FileName, Bam(Encoding.UTF8.GetBytes(noiDung)), s.Metadata, luc);
                }
            }
        }

        private static string Bam(byte[] b)
        {
            return Convert.ToHexStringLower(SHA256.HashData(b));
        }
    }
}
