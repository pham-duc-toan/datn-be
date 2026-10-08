using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Quality;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Consumers
{
    /// <summary>
    /// redundancy.increase_requested (quality-svc): mau tranh chap, xin them nguoi gan
    /// (VONG LAP THICH UNG, docs 3.7). task-svc la CHU so redundancy nen tu quyet:
    ///   - khong vuot tran MaxRedundancy cua du an (ky quy chi tinh toi tran);
    ///   - yeu cau cu / khong lon hon so hien tai → bo qua (giao lai hai lan van dung);
    ///   - task da du nguoi → mo lai de cap them.
    /// Doi xong phat task.redundancy_changed (vet kiem toan FQ-03).
    /// </summary>
    public sealed class RedundancyIncreaseRequestedProcessor : IEventProcessor<RedundancyIncreaseRequested>
    {
        private readonly TaskDbContext _db;
        private readonly TaskEventPublisher _events;
        private readonly ILogger<RedundancyIncreaseRequestedProcessor> _logger;

        public RedundancyIncreaseRequestedProcessor(
            TaskDbContext db,
            TaskEventPublisher events,
            ILogger<RedundancyIncreaseRequestedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _events = events;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<RedundancyIncreaseRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            RedundancyIncreaseRequested p = envelope.Payload;

            ProjectSnapshot? duAn = await _db.ProjectSnapshots.FirstOrDefaultAsync(x => x.ProjectId == p.ProjectId, ct);
            if (duAn == null || !duAn.IsConfigured || duAn.Status == SnapshotStatus.Closed)
            {
                _logger.LogInformation("Bo qua tang redundancy task {TaskId}: du an chua chay hoac da dong", p.TaskId);
                return;
            }

            List<LabelingTask> khoa = await TaskQueries.KhoaTaskAsync(_db, new Guid[] { p.TaskId }, ct);
            if (khoa.Count == 0 || khoa[0].ProjectId != p.ProjectId)
            {
                _logger.LogWarning("Yeu cau tang redundancy cho task {TaskId} khong ton tai trong du an {ProjectId}", p.TaskId, p.ProjectId);
                return;
            }

            LabelingTask task = khoa[0];
            int? cu = task.TangRedundancy(p.NewRedundancy, duAn.MaxRedundancy);

            if (!cu.HasValue)
            {
                _logger.LogInformation(
                    "Khong tang redundancy task {TaskId}: yeu cau {Moi}, hien {HienTai}, tran {Tran}, trang thai {State}",
                    p.TaskId,
                    p.NewRedundancy,
                    task.RedundancyTarget,
                    duAn.MaxRedundancy,
                    task.State);
                return;
            }

            _events.Phat(Caller.HeThong(envelope.CorrelationId, envelope.EventId), new TaskRedundancyChanged
            {
                TaskId = task.Id,
                ProjectId = task.ProjectId,
                SampleId = task.SampleId,
                OldRedundancy = cu.Value,
                NewRedundancy = task.RedundancyTarget,
            });

            _logger.LogInformation(
                "Task {TaskId}: redundancy {Cu} → {Moi} ({LyDo})",
                task.Id,
                cu.Value,
                task.RedundancyTarget,
                p.Reason);
        }
    }
}
