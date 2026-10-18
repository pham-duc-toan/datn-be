using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Labeling;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Attempts;
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
        /// <summary>Nhom khoa advisory cua ban sao du an (tham so dau cua pg_advisory_xact_lock).</summary>
        private const int NhomKhoaBanSao = 7001;

        /// <summary>
        /// KHOA theo du an truoc khi doc / tao: project.published, dataset.ingested,
        /// gold_set.updated... di qua CAC QUEUE KHAC NHAU nen co the xu ly cung luc. Khi ban
        /// sao chua co, ca hai cung INSERT → mot ben vi pham PK_project_snapshots, message
        /// bi tra lai hang doi (gap khi chay E2E tong the: gold_set.updated dung project.published).
        /// pg_advisory_xact_lock xep hang hai ben; ben sau doc duoc ban sao ben truoc vua
        /// commit. Khoa tu nha khi transaction cua IdempotencyGuard ket thuc.
        /// </summary>
        public static async Task<ProjectSnapshot> LayHoacTaoAsync(TaskDbContext db, Guid projectId, CancellationToken ct)
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0}, hashtext({1}))",
                new object[] { NhomKhoaBanSao, projectId.ToString() },
                ct);

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
    /// dataset.ingested: moi mau thanh mot CAU HOI cua de. Mau trung (lo giao lai) bo qua
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

            DateTimeOffset bayGio = _clock.GetUtcNow();

            foreach (IngestedSample s in p.Samples)
            {
                if (daCo.Contains(s.SampleId))
                {
                    continue;
                }

                LabelingTask t = LabelingTask.Tao(p.ProjectId, s.SampleId, s.Modality, s.StorageKey, s.Content, s.Metadata, bayGio);
                t.LoaiTruVi(vang.Contains(s.SampleId));
                _db.Tasks.Add(t);
            }
        }
    }

    /// <summary>
    /// project.published v2: nap cau hinh (thuong, so suat, nguong t, han chot) vao ban sao — du an mo nhan bai.
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

            // Tap nhan doc qua dung cua kiem cua Crowd.Labeling: phien ban hop dong
            // la hay hong thi NEM LOI → message vao DLQ cho nguoi xem, khong am tham
            // mo du an ma labeler nop gi cung bi tu choi.
            duAn.ApDungPublished(
                p.OwnerId,
                LabelSchema.Doc(p.LabelSchema),
                p.RewardVnd,
                p.Slots,
                p.PassThresholdPercent,
                p.QuestionCount,
                p.Deadline,
                p.AllowProfessional,
                p.IsPrivate,
                p.MinLevel,
                p.MinReputation,
                p.SensitiveContent == true,
                envelope.OccurredAt);

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

            // Bai dang lam KHONG bi huy: luu nhap van duoc, chi chua nop / bat dau moi duoc. Chay tiep thi
            // thoi gian tam dung duoc cong bu cho moi bai dang lam.
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
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(_db, envelope.Payload.ProjectId, ct);
            TimeSpan? daDung = duAn.TiepTuc(envelope.OccurredAt);
            if (daDung == null)
            {
                _logger.LogInformation("project.resumed {EventId} cu hon trang thai hien tai — bo qua", envelope.EventId);
                return;
            }

            // Cong bu thoi gian tam dung cho bai dang lam (khong qua han chot).
            if (daDung.Value > TimeSpan.Zero && duAn.Deadline.HasValue)
            {
                List<Attempt> dangLam = await TaskQueries.KhoaBaiDangLamAsync(_db, duAn.ProjectId, null, ct);
                foreach (Attempt a in dangLam)
                {
                    a.CongBuThoiGian(daDung.Value, duAn.Deadline.Value);
                }
            }
        }
    }

    /// <summary>Dong du an (huy hoac hoan thanh) — dung chung cho hai event.</summary>
    internal static class ProjectCloser
    {
        public static async Task DongAsync(TaskDbContext db, Guid projectId, DateTimeOffset occurredAt, DateTimeOffset bayGio, CancellationToken ct)
        {
            ProjectSnapshot duAn = await Snapshots.LayHoacTaoAsync(db, projectId, ct);
            duAn.Dong(occurredAt);

            // Bai dang lam do (du an bi huy truoc khi dong thu bai) → huy; bai da nop giu nguyen.
            List<Attempt> dangLam = await TaskQueries.KhoaBaiDangLamAsync(db, projectId, null, ct);
            foreach (Attempt a in dangLam)
            {
                a.Huy(bayGio);
            }

            DateTimeOffset? luc = bayGio;
            await db.Tasks
                .Where(t => t.ProjectId == projectId && t.State == TaskState.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TaskState.Cancelled).SetProperty(t => t.CompletedAt, luc), ct);
        }
    }

    public sealed class ProjectCancelledProcessor : IEventProcessor<ProjectCancelled>
    {
        private readonly TaskDbContext _db;
        private readonly TimeProvider _clock;

        public ProjectCancelledProcessor(TaskDbContext db, TimeProvider clock)
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

        public Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return ProjectCloser.DongAsync(_db, envelope.Payload.ProjectId, envelope.OccurredAt, _clock.GetUtcNow(), ct);
        }
    }

    public sealed class ProjectCompletedProcessor : IEventProcessor<ProjectCompleted>
    {
        private readonly TaskDbContext _db;
        private readonly TimeProvider _clock;

        public ProjectCompletedProcessor(TaskDbContext db, TimeProvider clock)
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

        public Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return ProjectCloser.DongAsync(_db, envelope.Payload.ProjectId, envelope.OccurredAt, _clock.GetUtcNow(), ct);
        }
    }

    /// <summary>
    /// project.finish_requested ("Ket thuc som" cua doanh nghiep): dong thu bai ngay — bai dang lam do bi het
    /// han, cham voi cac bai da nop.
    /// </summary>
    public sealed class ProjectFinishRequestedProcessor : IEventProcessor<ProjectFinishRequested>
    {
        private readonly TaskDbContext _db;
        private readonly ThuBaiService _thuBai;
        private readonly TimeProvider _clock;

        public ProjectFinishRequestedProcessor(TaskDbContext db, ThuBaiService thuBai, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (thuBai == null)
            {
                throw new ArgumentNullException(nameof(thuBai));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _thuBai = thuBai;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectFinishRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            await Snapshots.LayHoacTaoAsync(_db, envelope.Payload.ProjectId, ct);
            ProjectSnapshot? duAn = await TaskQueries.KhoaDuAnAsync(_db, envelope.Payload.ProjectId, ct);
            if (duAn == null || !duAn.IsConfigured)
            {
                // project.published chua toi → nem loi de broker giao lai sau.
                throw new InvalidOperationException("Chua co cau hinh du an " + envelope.Payload.ProjectId + " de dong thu bai.");
            }

            await _thuBai.DongThuBaiAsync(duAn, "ket_thuc_som", _clock.GetUtcNow(), Caller.HeThong(envelope.CorrelationId, envelope.EventId), ct);
        }
    }

    /// <summary>project.graded (quality-svc): ghi ket qua tung bai, mo han khieu nai; han 0 thi chot luon.</summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<ProjectGraded>
    {
        private readonly TaskDbContext _db;
        private readonly ThuBaiService _thuBai;
        private readonly TimeProvider _clock;

        public ProjectGradedProcessor(TaskDbContext db, ThuBaiService thuBai, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (thuBai == null)
            {
                throw new ArgumentNullException(nameof(thuBai));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _thuBai = thuBai;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectSnapshot? duAn = await TaskQueries.KhoaDuAnAsync(_db, envelope.Payload.ProjectId, ct);
            if (duAn == null)
            {
                throw new InvalidOperationException("project.graded cho du an chua co ban sao " + envelope.Payload.ProjectId + ".");
            }

            await _thuBai.GhiKetQuaChamAsync(duAn, envelope.Payload, _clock.GetUtcNow(), Caller.HeThong(envelope.CorrelationId, envelope.EventId), ct);
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
                _db.GoldSamples.Add(GoldSample.Tao(p.ProjectId, i.SampleId, mucDich, i.ExpectedPayload));
                vangMoi.Add(i.SampleId);
            }

            List<Guid> anhHuong = vangCu.Union(vangMoi).ToList();
            List<LabelingTask> tasks = await _db.Tasks.Where(t => t.ProjectId == p.ProjectId && anhHuong.Contains(t.SampleId)).ToListAsync(ct);

            foreach (LabelingTask t in tasks)
            {
                t.LoaiTruVi(vangMoi.Contains(t.SampleId));
            }
        }
    }
}
