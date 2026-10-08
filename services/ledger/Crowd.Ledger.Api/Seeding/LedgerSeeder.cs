using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Infrastructure.Persistence;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Ledger.Api.Seeding
{
    /// <summary>
    /// Seed so cai: KHONG ghi but toan bang tay. Moi dong tien di qua CHINH
    /// MoneyFlowService — cung ham ma consumer goi khi event that toi:
    ///
    ///   deposit.confirmed          → NapTienAsync        (nap 2.000.000d cho biz1)
    ///   project.publish_requested  → DatKyQuyAsync       (giu ky quy P1, P4, P2)
    ///   project.published          → GhiNhanRedundancy   (chan chi vuot)
    ///   annotation.approved        → ChiTraNhanAsync     (tru ky quy, treo cho labeler)
    ///   (worker giai phong treo)   → GiaiPhongTreoAsync  (treo het han → kha dung)
    ///
    /// Nho vay so cai seed van can bang, chuoi hash lien tuc, va
    /// GET /ledger/admin/reconciliation tra ve healthy = true.
    ///
    /// MoneyFlowService duoc dung voi DONG HO LUI VE QUA KHU theo kich ban: chi
    /// tra "luc 4 ngay truoc" thi khoan treo het han tu lau → giai phong ngay,
    /// labeler rut duoc luon.
    /// </summary>
    public sealed class LedgerSeeder
    {
        private readonly LedgerDbContext _db;
        private readonly LedgerWriter _writer;
        private readonly LedgerEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILoggerFactory _loggers;
        private readonly ILogger<LedgerSeeder> _logger;

        public LedgerSeeder(
            LedgerDbContext db,
            LedgerWriter writer,
            LedgerEventPublisher events,
            ISettings settings,
            TimeProvider clock,
            ILoggerFactory loggers)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (loggers == null)
            {
                throw new ArgumentNullException(nameof(loggers));
            }

            _db = db;
            _writer = writer;
            _events = events;
            _settings = settings;
            _clock = clock;
            _loggers = loggers;
            _logger = loggers.CreateLogger<LedgerSeeder>();
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Idempotent THEO TUNG DU AN: du an da co ky quy thi bo qua ca chuoi
            // (ky quy → redundancy → chi tra) cua no. DB cu da co P1-P4 thi van
            // them du an moi cua kich ban. Nap tien tu idempotent theo IntentId.
            List<Guid> ids = KichBanSeed.Projects.Select(p => p.Id).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(
                await _db.Escrows.Where(e => ids.Contains(e.ProjectId)).Select(e => e.ProjectId).ToListAsync(ct));

            if (KichBanSeed.Projects.Where(p => p.DaKyQuy).All(p => daCo.Contains(p.Id)))
            {
                _logger.LogInformation("Seed ledger: da du du an seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Caller heThong = Caller.HeThong(Guid.NewGuid(), null);

            // LedgerWriter bat buoc nam trong transaction (khoa advisory so cai).
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                // ---- 1. Nap tien ----
                foreach (SeedDeposit d in KichBanSeed.Deposits.Where(x => x.DaThanhToan).OrderByDescending(x => x.CreatedAgo))
                {
                    DateTimeOffset lucNap = bayGio - d.CreatedAgo + TimeSpan.FromMinutes(2);
                    await DongTienLuc(lucNap).NapTienAsync(SeedEvents.NapThanhCong(d), ct);
                    await LuuAsync(ct);
                }

                // ---- 2. Ky quy, theo thu tu thoi gian ----
                List<SeedProject> daKyQuy = KichBanSeed.Projects
                    .Where(p => p.DaKyQuy && !daCo.Contains(p.Id))
                    .OrderBy(p => p.LucKyQuy(bayGio))
                    .ToList();

                foreach (SeedProject sp in daKyQuy)
                {
                    await DongTienLuc(sp.LucKyQuy(bayGio)).DatKyQuyAsync(SeedEvents.YeuCauKyQuy(sp), heThong, ct);
                    await LuuAsync(ct);

                    // Thieu tien thi DatKyQuyAsync KHONG nem loi (do la ket qua nghiep
                    // vu binh thuong) ma chi phat escrow.rejected — o seed thi la kich
                    // ban sai, phai dung lai ngay.
                    if (!await _db.Escrows.AnyAsync(e => e.ProjectId == sp.Id, ct))
                    {
                        throw new InvalidOperationException("Kich ban seed: khong du tien ky quy cho du an '" + sp.Key + "'.");
                    }
                }

                // ---- 3. Du an dang chay: ghi redundancy ----
                foreach (SeedProject sp in daKyQuy.Where(p => p.Stage == SeedStage.Running))
                {
                    await DongTienLuc(sp.LucDuyet(bayGio)).GhiNhanRedundancyAsync(SeedEvents.DaPublish(sp, bayGio), ct);
                    await LuuAsync(ct);
                }

                // ---- 4. Chi tra nhan da duyet, cu nhat truoc ----
                int soChi = 0;
                foreach (SeedProject sp in daKyQuy)
                {
                    IEnumerable<SeedSubmission> daDuyet = sp.Submissions
                        .Where(s => s.Review == SeedReview.Approved && s.ReviewedAgo.HasValue)
                        .OrderByDescending(s => s.ReviewedAgo);

                    foreach (SeedSubmission s in daDuyet)
                    {
                        DateTimeOffset lucDuyet = bayGio - s.ReviewedAgo.GetValueOrDefault();
                        await DongTienLuc(lucDuyet).ChiTraNhanAsync(SeedEvents.DaDuyet(s, sp), heThong, ct);
                        await LuuAsync(ct);
                        soChi = soChi + 1;
                    }
                }

                // ---- 5. Giai phong cac khoan treo da het han (viec cua HoldReleaseWorker) ----
                int soGiaiPhong = 0;
                List<FundsHold> treo = await _db.Holds.Where(h => h.State == HoldState.Held).ToListAsync(ct);
                foreach (FundsHold h in treo)
                {
                    if (h.DenHan(bayGio))
                    {
                        await DongTienLuc(bayGio).GiaiPhongTreoAsync(h, heThong, ct);
                        await LuuAsync(ct);
                        soGiaiPhong = soGiaiPhong + 1;
                    }
                }

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Seed ledger: {SoKyQuy} ky quy, {SoChi} lan chi tra, {SoGiaiPhong} khoan treo da giai phong",
                    daKyQuy.Count,
                    soChi,
                    soGiaiPhong);
            }
        }

        /// <summary>MoneyFlowService that, chi thay dong ho bang dong ho dung yen tai thoi diem cua kich ban.</summary>
        private MoneyFlowService DongTienLuc(DateTimeOffset luc)
        {
            return new MoneyFlowService(
                _db,
                _writer,
                _events,
                _settings,
                new DongHoCoDinh(luc),
                _loggers.CreateLogger<MoneyFlowService>());
        }

        /// <summary>Bo event vua phat (service khac tu seed phan cua minh), roi luu.</summary>
        private async Task LuuAsync(CancellationToken ct)
        {
            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);
        }
    }
}
