using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Seeding;
using Crowd.Tasking.Api.Consumers;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Seeding
{
    /// <summary>
    /// Seed task-svc: task cho moi anh, ban sao du an / thanh vien / cau vang,
    /// va cac luot lease DA NOP cua kich ban.
    ///
    /// Ba buoc, dung thu tu du lieu that sinh ra:
    ///   1. Task tu anh (nhu dataset.ingested luc du an con Nhap) — tu tao vi
    ///      TaskId phai TAT DINH de annotation-svc khop.
    ///   2. PHAT LAI member.added / gold_set.updated / project.published vao
    ///      CHINH cac processor cua task-svc — ban sao dung bang code duong that.
    ///   3. Luot nop: Assignment.Tao (lease) roi Nop — dung luat domain, nen
    ///      bo dem ActiveLease/Submitted va trang thai Completed tu dung.
    /// </summary>
    public sealed class TaskSeeder
    {
        private readonly TaskDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILoggerFactory _loggers;
        private readonly ILogger<TaskSeeder> _logger;

        public TaskSeeder(
            TaskDbContext db,
            ISettings settings,
            TimeProvider clock,
            ILoggerFactory loggers)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _settings = settings;
            _clock = clock;
            _loggers = loggers;
            _logger = loggers.CreateLogger<TaskSeeder>();
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Idempotent THEO TUNG DU AN: DB cu da co P1-P4 thi van them du an moi
            // cua kich ban (P5-P7...).
            List<Guid> ids = KichBanSeed.Projects.Select(p => p.Id).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(
                await _db.ProjectSnapshots.Where(p => ids.Contains(p.ProjectId)).Select(p => p.ProjectId).ToListAsync(ct));

            List<SeedProject> canTao = KichBanSeed.Projects.Where(p => !daCo.Contains(p.Id)).ToList();
            if (canTao.Count == 0)
            {
                _logger.LogInformation("Seed task: da du du an seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            // Ca kich ban trong MOT transaction: ExecuteUpdate cua processor chay
            // ngay xuong DB, nhung van rollback duoc neu buoc sau hong.
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                await TaoTaskAsync(canTao, bayGio, ct);
                await PhatLaiEventAsync(canTao, bayGio, ct);
                int soBai = await TaoBaiLamAsync(canTao, bayGio, ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Seed task: {SoDuAn} du an, {SoCau} cau hoi, {SoBai} bai lam",
                    canTao.Count,
                    canTao.Sum(p => p.Samples.Count),
                    soBai);
            }
        }

        /// <summary>Buoc 1: ban sao "chua publish" + cau hoi — dung nhu DatasetIngestedProcessor.</summary>
        private async Task TaoTaskAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            foreach (SeedProject sp in duAn)
            {
                DateTimeOffset lucTao = bayGio - sp.CreatedAgo;

                await Snapshots.LayHoacTaoAsync(_db, sp.Id, ct);

                foreach (SeedSample s in sp.Samples)
                {
                    LabelingTask t = LabelingTask.Tao(
                        sp.Id,
                        s.Id,
                        s.Modality,
                        s.StorageKey,
                        s.ContentJson == null ? null : RawJson.Tu(s.ContentJson),
                        s.Metadata.ToRawJson(),
                        lucTao);
                    SeedIds.GanId(t, KichBanSeed.TaskIdCua(s.Id));
                    _db.Tasks.Add(t);
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Buoc 2: phat lai event cua project-svc, theo dung thu tu thoi gian.</summary>
        private async Task PhatLaiEventAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            MemberAddedProcessor thanhVien = new MemberAddedProcessor(_db);
            GoldSetUpdatedProcessor cauVang = new GoldSetUpdatedProcessor(_db, _loggers.CreateLogger<GoldSetUpdatedProcessor>());
            ProjectPublishedProcessor daPublish = new ProjectPublishedProcessor(_db, _settings);

            foreach (SeedProject sp in duAn)
            {
                DateTimeOffset lucTao = bayGio - sp.CreatedAgo;
                List<MemberAdded> dsThanhVien = SeedEvents.ThanhVien(sp);

                // Chu du an vao cung luc tao du an.
                await thanhVien.XuLyAsync(SeedEvents.Boc(dsThanhVien[0], lucTao), ct);

                GoldSetUpdated? vang = SeedEvents.CauVang(sp);
                if (vang != null)
                {
                    await cauVang.XuLyAsync(SeedEvents.Boc(vang, lucTao), ct);
                }

                if (sp.DaChay)
                {
                    DateTimeOffset lucDuyet = sp.LucDuyet(bayGio);
                    await daPublish.XuLyAsync(SeedEvents.Boc(SeedEvents.DaPublish(sp, bayGio), lucDuyet), ct);

                    // Thanh vien khac vao SAU khi du an chay.
                    for (int i = 1; i < dsThanhVien.Count; i++)
                    {
                        await thanhVien.XuLyAsync(SeedEvents.Boc(dsThanhVien[i], lucDuyet + TimeSpan.FromHours(1)), ct);
                    }
                }

                SeedOutbox.BoEventChuaGui(_db);
                await _db.SaveChangesAsync(ct);
            }

            // Processor co ExecuteUpdate (thang xuong DB) — xoa bo nho de buoc sau doc lai tu DB.
            _db.ChangeTracker.Clear();
        }

        /// <summary>
        /// Buoc 3: bai lam — dung CHINH domain (Attempt.BatDau / Nop / GhiKetQua) nhu API that. Du an Completed
        /// thi dong thu bai, cham, chot, dong theo dung moc thoi gian cua kich ban.
        /// </summary>
        private async Task<int> TaoBaiLamAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            int n = 0;
            foreach (SeedProject sp in duAn)
            {
                if (sp.Attempts.Count == 0)
                {
                    continue;
                }

                ProjectSnapshot snap = await _db.ProjectSnapshots.FirstAsync(p => p.ProjectId == sp.Id, ct);

                // De = moi mau that (Open) + cau vang kiem tra, theo thu tu mau cua kich ban (seed tat dinh).
                List<Guid> de = new List<Guid>();
                foreach (SeedSample m in sp.Samples)
                {
                    bool vangTest = false;
                    foreach (SeedGold g in sp.Gold)
                    {
                        if (g.SampleId == m.Id && g.ForEntranceTest)
                        {
                            vangTest = true;
                        }
                    }

                    if (!vangTest)
                    {
                        de.Add(m.Id);
                    }
                }

                foreach (SeedAttempt b in sp.Attempts.OrderByDescending(x => x.StartedAgo))
                {
                    DateTimeOffset batDau = bayGio - b.StartedAgo;
                    DateTimeOffset hetHan = batDau + _settings.ThoiGian(SettingKeys.TaskAttemptDuration);
                    if (snap.Deadline.HasValue && snap.Deadline.Value < hetHan)
                    {
                        hetHan = snap.Deadline.Value;
                    }

                    Attempt a = Attempt.BatDau(sp.Id, b.LabelerId, de, batDau, hetHan);
                    SeedIds.GanId(a, b.AttemptId);

                    foreach (SeedAnswer tl in b.Answers)
                    {
                        // Kiem nhu khi nop that (tap nhan + metadata mau); luu dang JSON tho nhu ban nhap.
                        SeedEvents.NhanCua(sp, tl);
                        _db.AttemptAnswers.Add(AttemptAnswer.Tao(a.Id, tl.SampleId, tl.PayloadJson, batDau + TimeSpan.FromMinutes(5)));
                    }

                    if (b.State == SeedAttemptState.Submitted)
                    {
                        a.Nop(b.GoldTotal, b.GoldCorrect, null, null, bayGio - b.SubmittedAgo!.Value);
                    }

                    if (b.Result != null)
                    {
                        a.GhiKetQua(b.Result.ComparableCount, b.Result.MatchedCount, b.Result.AgreementPercent, b.Result.Passed, b.Result.Passed, bayGio - sp.GradedAgo!.Value);
                    }

                    _db.Attempts.Add(a);
                    n++;
                }

                if (sp.Stage == SeedStage.Completed)
                {
                    DateTimeOffset lucCham = bayGio - sp.GradedAgo!.Value;
                    DateTimeOffset lucXong = bayGio - sp.CompletedAgo!.Value;
                    snap.DongThuBai("du_suat", lucCham);
                    snap.GhiDaCham(lucCham, lucXong - lucCham);
                    snap.Chot(SeedEvents.SoNguoiDat(sp), lucXong);
                    snap.Dong(lucXong);
                }
            }

            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);
            return n;
        }
    }
}
