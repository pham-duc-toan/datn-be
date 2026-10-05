using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Storage;
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
    /// Seed DU AN cua kich ban: du an, thanh vien, dataset + anh mau (len MinIO
    /// that), cau hoi vang.
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
            Guid moc = KichBanSeed.Project("p1").Id;
            if (await _db.Projects.AnyAsync(p => p.Id == moc, ct))
            {
                _logger.LogInformation("Seed project: da co du lieu seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            int soAnh = 0;

            foreach (SeedProject sp in KichBanSeed.Projects)
            {
                soAnh = soAnh + await TaoDuAnAsync(sp, bayGio, ct);
            }

            // Mot SaveChanges cho ca kich ban: hong o dau thi khong luu gi ca.
            // Anh da day len MinIO van nam do — khoa tat dinh nen lan seed sau ghi de.
            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Seed project: tao {SoDuAn} du an, {SoAnh} anh mau",
                KichBanSeed.Projects.Count,
                soAnh);
        }

        private async Task<int> TaoDuAnAsync(SeedProject sp, DateTimeOffset bayGio, CancellationToken ct)
        {
            DateTimeOffset lucTao = bayGio - sp.CreatedAgo;

            // ---- 1. Du an Nhap + cau hinh day du ----
            LabelingProject duAn = LabelingProject.Tao(
                sp.OwnerId,
                sp.Name,
                sp.Description,
                TaskType.ImageClassification,
                sp.IsPrivate ? ProjectVisibility.Private : ProjectVisibility.Public,
                lucTao);
            SeedIds.GanId(duAn, sp.Id);

            LabelSchema tapNhan = LabelSchema.TaoPhanLoai(sp.Classes, false);
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

            // ---- 2. Dataset + anh mau ----
            Dataset lo = Dataset.Tao(sp.Id, "Lo anh mau (seed)", lucTao);
            SeedIds.GanId(lo, sp.DatasetId);

            foreach (SeedSample s in sp.Samples)
            {
                byte[] png = AnhMauPng.Tao(s.R, s.G, s.B, KichThuocAnh);
                string sha = Convert.ToHexStringLower(SHA256.HashData(png));

                Sample mau = Sample.Tao(sp.Id, sp.DatasetId, s.FileName, AnhMauPng.ContentType, ".png", png.Length, sha, lucTao);

                // Sample.Tao tinh StorageKey tu ID ngau nhien cua no — doi ID thi
                // phai doi khoa theo, cho khop khoa ma task-svc / annotation-svc seed.
                SeedIds.GanId(mau, s.Id);
                SeedIds.GanThuocTinh(mau, "StorageKey", s.StorageKey);

                await _storage.LuuAsync(s.StorageKey, png, AnhMauPng.ContentType, ct);
                _db.Samples.Add(mau);
            }

            lo.GhiNhanKetQuaNap(sp.Samples.Count, 0);
            _db.Datasets.Add(lo);

            // ---- 3. Cau hoi vang ----
            foreach (SeedGold g in sp.Gold)
            {
                GoldItem vang = GoldItem.Tao(
                    sp.Id,
                    g.SampleId,
                    new string[] { g.Label },
                    g.ForEntranceTest ? GoldPurpose.EntranceTest : GoldPurpose.QualityCheck,
                    tapNhan,
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
    }
}
