using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Project;
using Crowd.Contracts.Tasking;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// task-svc dong thu bai (du suat / het han / ket thuc som): Dang chay / Tam dung → Dang cham
    /// (docs 3.18). Labeler khong vao lam duoc nua; quality-svc cham.
    ///
    /// KHONG SaveChanges: EventConsumer boc processor trong IdempotencyGuard.
    /// </summary>
    public sealed class CollectionFinishedProcessor : IEventProcessor<ProjectCollectionFinished>
    {
        private readonly ProjectDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<CollectionFinishedProcessor> _logger;

        public CollectionFinishedProcessor(ProjectDbContext db, TimeProvider clock, ILogger<CollectionFinishedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _clock = clock;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectCollectionFinished> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == envelope.Payload.ProjectId, ct);
            if (duAn == null)
            {
                _logger.LogWarning("project.collection_finished {EventId} cho du an khong ton tai {ProjectId} — bo qua",
                    envelope.EventId, envelope.Payload.ProjectId);
                return;
            }

            if (duAn.BatDauCham(envelope.Payload.FinishedAt))
            {
                _logger.LogInformation(
                    "Du an {ProjectId} dong thu bai ({Reason}, {Count} bai) → Dang cham",
                    duAn.Id, envelope.Payload.Reason, envelope.Payload.Attempts.Count);
            }
        }
    }

    /// <summary>
    /// Het han khieu nai, khong con khieu nai mo (task-svc phat project.settled): Dang cham → Hoan
    /// thanh, phat project.completed {PassedAttemptCount} — ledger hoan ky quy con lai SAU KHI da
    /// tra du so nguoi dat (bai hoc NC-B-01).
    /// </summary>
    public sealed class ProjectSettledProcessor : IEventProcessor<ProjectSettled>
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectEventPublisher _events;
        private readonly ILogger<ProjectSettledProcessor> _logger;

        public ProjectSettledProcessor(ProjectDbContext db, ProjectEventPublisher events, ILogger<ProjectSettledProcessor> logger)
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

        public async Task XuLyAsync(EventEnvelope<ProjectSettled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == envelope.Payload.ProjectId, ct);
            if (duAn == null)
            {
                _logger.LogWarning("project.settled {EventId} cho du an khong ton tai {ProjectId} — bo qua",
                    envelope.EventId, envelope.Payload.ProjectId);
                return;
            }

            if (duAn.Status != ProjectStatus.Grading && duAn.Status != ProjectStatus.Completed)
            {
                // collection_finished va settled di hai queue khac nhau: settled toi truoc → nem loi de
                // broker giao lai sau (khi du an da sang Dang cham).
                throw new InvalidOperationException(
                    "project.settled toi khi du an " + duAn.Id + " dang " + duAn.Status + " — cho project.collection_finished.");
            }

            if (!duAn.HoanThanh(envelope.OccurredAt))
            {
                return;
            }

            _events.Phat(Caller.HeThong(envelope.CorrelationId, envelope.EventId), new ProjectCompleted
            {
                ProjectId = duAn.Id,
                OwnerId = duAn.OwnerId,
                PassedAttemptCount = envelope.Payload.PassedAttemptCount,
            });

            _logger.LogInformation(
                "Du an {ProjectId} hoan thanh: {Passed} nguoi dat (da chot sau han khieu nai)",
                duAn.Id, envelope.Payload.PassedAttemptCount);
        }
    }
}
