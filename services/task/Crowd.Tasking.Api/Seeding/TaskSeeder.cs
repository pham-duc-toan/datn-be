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
using Crowd.Tasking.Domain.Assignments;
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
                int soLuot = await TaoLuotNopAsync(canTao, bayGio, ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Seed task: {SoDuAn} du an, {SoTask} task, {SoLuot} luot da nop",
                    canTao.Count,
                    canTao.Sum(p => p.Samples.Count),
                    soLuot);
            }
        }

        /// <summary>Buoc 1: ban sao "chua publish" + task redundancy 0 — dung nhu DatasetIngestedProcessor.</summary>
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
                        0,
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
            ProjectPublishedProcessor daPublish = new ProjectPublishedProcessor(_db);

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

                if (sp.Stage == SeedStage.Running)
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

            // project.published gan redundancy bang ExecuteUpdate (thang xuong DB) —
            // ban trong bo nho van la 0. Xoa bo nho de buoc sau doc lai tu DB.
            _db.ChangeTracker.Clear();
        }

        /// <summary>Buoc 3: moi luot nop = lease (Assignment.Tao) roi Nop, cu nhat truoc.</summary>
        private async Task<int> TaoLuotNopAsync(List<SeedProject> duAn, DateTimeOffset bayGio, CancellationToken ct)
        {
            List<SeedSubmission> tatCa = duAn
                .SelectMany(p => p.Submissions)
                .OrderByDescending(s => s.SubmittedAgo)
                .ToList();

            foreach (SeedSubmission s in tatCa)
            {
                LabelingTask task = await _db.Tasks.FirstAsync(t => t.Id == s.TaskId, ct);

                DateTimeOffset lucNop = bayGio - s.SubmittedAgo;
                DateTimeOffset lucNhan = lucNop - TimeSpan.FromMinutes(4);

                Assignment luot = Assignment.Tao(task, s.LabelerId, lucNhan, _settings.ThoiGian(SettingKeys.TaskLeaseDuration));
                SeedIds.GanId(luot, s.AssignmentId);
                // Kiem nhu API that: tap nhan cua du an + metadata mau.
                luot.Nop(task, SeedEvents.NhanCua(KichBanSeed.Projects.First(p => p.Id == s.ProjectId), s), lucNop);

                _db.Assignments.Add(luot);
            }

            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            return tatCa.Count;
        }
    }
}
