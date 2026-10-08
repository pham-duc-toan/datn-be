using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Projects;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Annotation.Api.Consumers
{
    /// <summary>
    /// consensus.reached (quality-svc): ghi ket qua dong thuan cua task va danh dau
    /// tung nhan khop / lech. Mac dinh CHI LA GOI Y — khong doi trang thai duyet.
    ///
    /// Setting annotation.auto_approve_agreed bat: nhan CHO DUYET khop dong thuan
    /// (task Agreed) duoc HE THONG duyet luon va phat annotation.approved (chi tien).
    ///
    /// Annotation co the chua toi (annotation.submitted va consensus.reached di hai
    /// queue khac nhau): nhan nao chua co thi bo qua, quality-svc chi tinh dong thuan
    /// tren cac nhan no da nhan nen truong hop nay chi xay ra khi giao lai bat thuong.
    /// </summary>
    public sealed class ConsensusReachedProcessor : IEventProcessor<ConsensusReached>
    {
        private readonly AnnotationDbContext _db;
        private readonly ILogger<ConsensusReachedProcessor> _logger;
        private readonly ISettings _settings;
        private readonly AnnotationEventPublisher _events;
        private readonly TimeProvider _clock;

        public ConsensusReachedProcessor(
            AnnotationDbContext db,
            ILogger<ConsensusReachedProcessor> logger,
            ISettings settings,
            AnnotationEventPublisher events,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _logger = logger;
            _settings = settings;
            _events = events;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<ConsensusReached> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ConsensusReached p = envelope.Payload;

            TaskConsensus? c = await _db.Consensus.FirstOrDefaultAsync(x => x.TaskId == p.TaskId, ct);
            if (c == null)
            {
                c = TaskConsensus.Tao(p.TaskId, p.ProjectId, p.SampleId);
                _db.Consensus.Add(c);
            }

            if (!c.ApDung(TenTrangThai(p.Status), p.Final, envelope.OccurredAt))
            {
                _logger.LogInformation("consensus.reached {EventId} cu hon ket qua dang co cua task {TaskId} — bo qua", envelope.EventId, p.TaskId);
                return;
            }

            List<Guid> ids = p.Votes.Select(v => v.AnnotationId).ToList();
            Dictionary<Guid, LabelAnnotation> nhan = await _db.Annotations
                .Where(a => ids.Contains(a.Id) && a.ProjectId == p.ProjectId)
                .ToDictionaryAsync(a => a.Id, ct);

            foreach (ConsensusVote v in p.Votes)
            {
                LabelAnnotation? a;
                if (nhan.TryGetValue(v.AnnotationId, out a))
                {
                    a.GhiDongThuan(v.Agrees, envelope.OccurredAt);
                }
            }

            if (p.Status == ConsensusStatus.Agreed && _settings.DungSai(SettingKeys.AnnotationAutoApproveAgreed))
            {
                await TuDuyetAsync(p.ProjectId, nhan.Values, envelope, ct);
            }
        }

        private async Task TuDuyetAsync(
            Guid projectId, IEnumerable<LabelAnnotation> nhan, EventEnvelope<ConsensusReached> envelope, CancellationToken ct)
        {
            List<LabelAnnotation> duocDuyet = nhan
                .Where(a => a.Status == AnnotationStatus.PendingReview && a.ConsensusAgrees == true)
                .ToList();
            if (duocDuyet.Count == 0)
            {
                return;
            }

            ProjectTerms? dieuKhoan = await _db.ProjectTerms.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (dieuKhoan == null)
            {
                // project.published chua toi — nem loi de broker giao lai sau.
                throw new InvalidOperationException("Chua co dieu khoan du an " + projectId + " de tu duyet.");
            }

            Caller heThong = Caller.HeThong(envelope.CorrelationId, envelope.EventId);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            foreach (LabelAnnotation a in duocDuyet)
            {
                a.TuDuyetTheoDongThuan(bayGio);
                _events.PhatDaDuyet(a, dieuKhoan, heThong);
            }

            _logger.LogInformation(
                "Tu duyet {So} nhan khop dong thuan cua task {TaskId} (annotation.auto_approve_agreed)",
                duocDuyet.Count, envelope.Payload.TaskId);
        }

        /// <summary>Cung chuoi camelCase cua hop dong — giu nguyen khi luu.</summary>
        private static string TenTrangThai(ConsensusStatus s)
        {
            switch (s)
            {
                case ConsensusStatus.Agreed:
                    return "agreed";
                case ConsensusStatus.Disputed:
                    return "disputed";
                case ConsensusStatus.NotApplicable:
                    return "notApplicable";
                default:
                    throw new ArgumentOutOfRangeException(nameof(s), s, "Trang thai dong thuan chua map.");
            }
        }
    }
}
