using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Contracts.Project;
using Crowd.Seeding;
using Crowd.Tasking.Api.Consumers;
using Crowd.Tasking.Api.Settings;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private readonly LeaseOptions _lease;
        private readonly TimeProvider _clock;
        private readonly ILoggerFactory _loggers;
        private readonly ILogger<TaskSeeder> _logger;

        public TaskSeeder(
            TaskDbContext db,
            IOptions<LeaseOptions> lease,
            TimeProvider clock,
            ILoggerFactory loggers)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
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
            _lease = lease.Value;
            _clock = clock;
            _loggers = loggers;
            _logger = loggers.CreateLogger<TaskSeeder>();
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            Guid moc = KichBanSeed.Project("p1").Id;
            if (await _db.ProjectSnapshots.AnyAsync(p => p.ProjectId == moc, ct))
            {
                _logger.LogInformation("Seed task: da co du lieu seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            // Ca kich ban trong MOT transaction: ExecuteUpdate cua processor chay
            // ngay xuong DB, nhung van rollback duoc neu buoc sau hong.
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                await TaoTaskAsync(bayGio, ct);
                await PhatLaiEventAsync(bayGio, ct);
                int soLuot = await TaoLuotNopAsync(bayGio, ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Seed task: {SoDuAn} du an, {SoTask} task, {SoLuot} luot da nop",
                    KichBanSeed.Projects.Count,
                    KichBanSeed.Projects.Sum(p => p.Samples.Count),
                    soLuot);
            }
        }

        /// <summary>Buoc 1: ban sao "chua publish" + task redundancy 0 — dung nhu DatasetIngestedProcessor.</summary>
        private async Task TaoTaskAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            foreach (SeedProject sp in KichBanSeed.Projects)
            {
                DateTimeOffset lucTao = bayGio - sp.CreatedAgo;

                await Snapshots.LayHoacTaoAsync(_db, sp.Id, ct);

                foreach (SeedSample s in sp.Samples)
                {
                    LabelingTask t = LabelingTask.Tao(sp.Id, s.Id, s.StorageKey, 0, lucTao);
                    SeedIds.GanId(t, KichBanSeed.TaskIdCua(s.Id));
                    _db.Tasks.Add(t);
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Buoc 2: phat lai event cua project-svc, theo dung thu tu thoi gian.</summary>
        private async Task PhatLaiEventAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            MemberAddedProcessor thanhVien = new MemberAddedProcessor(_db);
            GoldSetUpdatedProcessor cauVang = new GoldSetUpdatedProcessor(_db, _loggers.CreateLogger<GoldSetUpdatedProcessor>());
            ProjectPublishedProcessor daPublish = new ProjectPublishedProcessor(_db);

            foreach (SeedProject sp in KichBanSeed.Projects)
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
        private async Task<int> TaoLuotNopAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            List<SeedSubmission> tatCa = KichBanSeed.Projects
                .SelectMany(p => p.Submissions)
                .OrderByDescending(s => s.SubmittedAgo)
                .ToList();

            foreach (SeedSubmission s in tatCa)
            {
                LabelingTask task = await _db.Tasks.FirstAsync(t => t.Id == s.TaskId, ct);

                DateTimeOffset lucNop = bayGio - s.SubmittedAgo;
                DateTimeOffset lucNhan = lucNop - TimeSpan.FromMinutes(4);

                Assignment luot = Assignment.Tao(task, s.LabelerId, lucNhan, _lease.ThoiHan);
                SeedIds.GanId(luot, s.AssignmentId);
                luot.Nop(task, new string[] { s.Label }, lucNop);

                _db.Assignments.Add(luot);
            }

            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            return tatCa.Count;
        }
    }
}
