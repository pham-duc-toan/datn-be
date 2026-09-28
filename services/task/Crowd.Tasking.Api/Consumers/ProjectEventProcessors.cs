using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Project;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Consumers
{
    // Dung ban sao du an + sinh task tu event cua project-svc.
    //
    // GIAO KEO chung cua moi processor: KHONG SaveChanges — EventConsumer boc
    // trong IdempotencyGuard, guard luu + ghi processed_events + commit MOT lan.
    // ExecuteUpdate/ExecuteDelete chay ngay nhung van nam trong transaction cua
    // guard, nen van nguyen tu voi phan con lai.

    /// <summary>Lay ban sao du an, chua co thi tao "chua publish".</summary>
    internal static class Snapshots
    {
        public static async Task<ProjectSnapshot> LayHoacTaoAsync(TaskDbContext db, Guid projectId, CancellationToken ct)
        {
            ProjectSnapshot? s = await db.ProjectSnapshots.FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
            if (s == null)
            {
                s = ProjectSnapshot.TaoChuaPublish(projectId, DateTimeOffset.MinValue);
                db.ProjectSnapshots.Add(s);
            }

            return s;
        }
    }

    /// <summary>
    /// dataset.ingested: moi mau thanh mot task. Mau trung (lo giao lai) bo qua
    /// nho UNIQUE (project_id, sample_id) va kiem truoc; mau vang thi Excluded.
    /// </summary>
    public sealed class DatasetIngestedProcessor : IEventProcessor<DatasetIngested>
    {
        private readonly TaskDbContext _db;
        private readonly TimeProvider _clock;

        public DatasetIngestedProcessor(TaskDbContext db, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<DatasetIngested> envelope, CancellationToken ct)
        {
            DatasetIngested p = envelope.Payload;
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, p.ProjectId, ct);

            List<Guid> ids = p.Samples.Select(s => s.SampleId).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(await _db.Tasks
                .Where(t => t.ProjectId == p.ProjectId && ids.Contains(t.SampleId))
                .Select(t => t.SampleId)
                .ToListAsync(ct));
            HashSet<Guid> vang = new HashSet<Guid>(await _db.GoldSamples
                .Where(g => g.ProjectId == p.ProjectId && ids.Contains(g.SampleId))
                .Select(g => g.SampleId)
                .ToListAsync(ct));

            int redundancy = duAn.IsConfigured ? duAn.Redundancy : 0;
            DateTimeOffset bayGio = _clock.GetUtcNow();

            foreach (IngestedSample s in p.Samples)
            {
                if (daCo.Contains(s.SampleId))
                {
                    continue;
                }

                LabelingTask t = LabelingTask.Tao(p.ProjectId, s.SampleId, s.StorageKey, redundancy, bayGio);
                t.LoaiTruVi(vang.Contains(s.SampleId));
                _db.Tasks.Add(t);
            }
        }
    }

    /// <summary>
    /// project.published: nap cau hinh vao ban sao, gan redundancy cho task tao
    /// tu truoc luc publish (dataset nap khi du an con Nhap).
    /// </summary>
    public sealed class ProjectPublishedProcessor : IEventProcessor<ProjectPublished>
    {
        private readonly TaskDbContext _db;

        public ProjectPublishedProcessor(TaskDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectPublished> envelope, CancellationToken ct)
        {
            ProjectPublished p = envelope.Payload;
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, p.ProjectId, ct);

            duAn.ApDungPublished(
                p.OwnerId,
                p.LabelClasses,
                p.AllowMultipleLabels,
                p.UnitPriceVnd,
                p.Redundancy,
                p.Deadline,
                p.AllowProfessional,
                p.IsPrivate,
                p.MinLevel,
                p.MinReputation,
                envelope.OccurredAt);

            // Hang loat, mot cau UPDATE: nap 10.000 task vao bo nho chi de gan mot
            // con so la lang phi. Chi dung task chua co redundancy.
            int r = p.Redundancy;
            await _db.Tasks
                .Where(t => t.ProjectId == p.ProjectId && t.RedundancyTarget == 0)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RedundancyTarget, r), ct);
        }
    }

    public sealed class ProjectPausedProcessor : IEventProcessor<ProjectPaused>
    {
        private readonly TaskDbContext _db;
        private readonly ILogger<ProjectPausedProcessor> _logger;

        public ProjectPausedProcessor(TaskDbContext db, ILogger<ProjectPausedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectPaused> envelope, CancellationToken ct)
        {
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, envelope.Payload.ProjectId, ct);

            // Lease dang giu KHONG bi thu hoi: labeler dang lam do thi cho nop not.
            // Chi ngung CAP task moi.
            if (!duAn.TamDung(envelope.OccurredAt))
            {
                _logger.LogInformation("project.paused {EventId} cu hon trang thai hien tai — bo qua", envelope.EventId);
            }
        }
    }

    public sealed class ProjectResumedProcessor : IEventProcessor<ProjectResumed>
    {
        private readonly TaskDbContext _db;
        private readonly ILogger<ProjectResumedProcessor> _logger;

        public ProjectResumedProcessor(TaskDbContext db, ILogger<ProjectResumedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectResumed> envelope, CancellationToken ct)
        {
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, envelope.Payload.ProjectId, ct);

            if (!duAn.TiepTuc(envelope.OccurredAt))
            {
                _logger.LogInformation("project.resumed {EventId} cu hon trang thai hien tai — bo qua", envelope.EventId);
            }
        }
    }

    /// <summary>Dong du an (huy hoac hoan thanh) — dung chung cho hai event.</summary>
    internal static class ProjectCloser
    {
        public static async Task DongAsync(TaskDbContext db, LeaseRevoker revoker, Guid projectId, DateTimeOffset occurredAt, DateTimeOffset bayGio, CancellationToken ct)
        {
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(db, projectId, ct);
            duAn.Dong(occurredAt);

            // Thu hoi lease truoc (khoa assignment → task), roi dong task con mo.
            await revoker.ThuHoiAsync(projectId, null, bayGio, ct);

            DateTimeOffset? luc = bayGio;
            await db.Tasks
                .Where(t => t.ProjectId == projectId && t.State == TaskState.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TaskState.Cancelled).SetProperty(t => t.CompletedAt, luc), ct);
        }
    }

    public sealed class ProjectCancelledProcessor : IEventProcessor<ProjectCancelled>
    {
        private readonly TaskDbContext _db;
        private readonly LeaseRevoker _revoker;
        private readonly TimeProvider _clock;

        public ProjectCancelledProcessor(TaskDbContext db, LeaseRevoker revoker, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _revoker = revoker;
            _clock = clock;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            return ProjectCloser.DongAsync(_db, _revoker, envelope.Payload.ProjectId, envelope.OccurredAt, _clock.GetUtcNow(), ct);
        }
    }

    public sealed class ProjectCompletedProcessor : IEventProcessor<ProjectCompleted>
    {
        private readonly TaskDbContext _db;
        private readonly LeaseRevoker _revoker;
        private readonly TimeProvider _clock;

        public ProjectCompletedProcessor(TaskDbContext db, LeaseRevoker revoker, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _revoker = revoker;
            _clock = clock;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            return ProjectCloser.DongAsync(_db, _revoker, envelope.Payload.ProjectId, envelope.OccurredAt, _clock.GetUtcNow(), ct);
        }
    }

    /// <summary>
    /// gold_set.updated: THAY TOAN BO ban sao cau vang cua du an, roi dong bo
    /// trang thai task: mau moi thanh vang → Excluded; het la vang → Open lai.
    /// </summary>
    public sealed class GoldSetUpdatedProcessor : IEventProcessor<GoldSetUpdated>
    {
        private readonly TaskDbContext _db;
        private readonly ILogger<GoldSetUpdatedProcessor> _logger;

        public GoldSetUpdatedProcessor(TaskDbContext db, ILogger<GoldSetUpdatedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<GoldSetUpdated> envelope, CancellationToken ct)
        {
            GoldSetUpdated p = envelope.Payload;
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, p.ProjectId, ct);

            if (!duAn.NhanGoldSet(envelope.OccurredAt))
            {
                _logger.LogInformation("gold_set.updated {EventId} cu hon ban dang co — bo qua", envelope.EventId);
                return;
            }

            List<Guid> vangCu = await _db.GoldSamples.Where(g => g.ProjectId == p.ProjectId).Select(g => g.SampleId).ToListAsync(ct);
            await _db.GoldSamples.Where(g => g.ProjectId == p.ProjectId).ExecuteDeleteAsync(ct);

            HashSet<Guid> vangMoi = new HashSet<Guid>();
            foreach (GoldSetItem i in p.Items)
            {
                string mucDich = i.Purpose == GoldPurpose.EntranceTest ? "entranceTest" : "qualityCheck";
                _db.GoldSamples.Add(GoldSample.Tao(p.ProjectId, i.SampleId, mucDich, i.ExpectedLabels));
                vangMoi.Add(i.SampleId);
            }

            List<Guid> anhHuong = vangCu.Union(vangMoi).ToList();
            List<LabelingTask> tasks = await TaskQueries.KhoaTaskAsync(
                _db,
                await _db.Tasks.Where(t => t.ProjectId == p.ProjectId && anhHuong.Contains(t.SampleId)).Select(t => t.Id).ToListAsync(ct),
                ct);

            foreach (LabelingTask t in tasks)
            {
                t.LoaiTruVi(vangMoi.Contains(t.SampleId));
            }
        }
    }
}
