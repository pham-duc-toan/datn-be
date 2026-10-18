using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Identity.Api.Services;

namespace Crowd.Identity.Api.Consumers
{
    // Event identity-svc nghe. Processor KHONG SaveChanges — IdempotencyGuard luu mot lan cung dau vet.

    /// <summary>
    /// project.graded (quality-svc): moi bai DAT → cong so cau da tra loi vao so nhan duoc chap nhan
    /// (docs 3.18 — khong con duyet tung nhan), tinh lai cap / huy hieu. Bai truot khong cong.
    /// </summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<ProjectGraded>
    {
        private readonly CapDoService _capDo;

        public ProjectGradedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            foreach (GradedAttempt a in envelope.Payload.Attempts)
            {
                if (a.Passed && a.AnsweredCount > 0)
                {
                    await _capDo.GhiNhanDuyetAsync(a.LabelerId, a.AnsweredCount, envelope.OccurredAt, HeThong(envelope), ct);
                }
            }
        }

        internal static Caller HeThong<T>(EventEnvelope<T> envelope)
            where T : class, IEventPayload
        {
            return Caller.HeThong(envelope.CorrelationId, envelope.EventId);
        }
    }

    /// <summary>attempt.appeal_resolved: khieu nai ca bai duoc chap nhan → bai thanh dat, cong nhu project.graded.</summary>
    public sealed class AttemptAppealResolvedProcessor : IEventProcessor<AttemptAppealResolved>
    {
        private readonly CapDoService _capDo;

        public AttemptAppealResolvedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<AttemptAppealResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptAppealResolved p = envelope.Payload;
            if (!p.Accepted || p.AnsweredCount <= 0)
            {
                return Task.CompletedTask;
            }

            return _capDo.GhiNhanDuyetAsync(p.LabelerId, p.AnsweredCount, envelope.OccurredAt, ProjectGradedProcessor.HeThong(envelope), ct);
        }
    }

    /// <summary>reputation.changed (quality-svc) → uy tin moi nhat; ban cu den tre bi bo qua.</summary>
    public sealed class ReputationChangedProcessor : IEventProcessor<ReputationChanged>
    {
        private readonly CapDoService _capDo;

        public ReputationChangedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<ReputationChanged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ReputationChanged p = envelope.Payload;
            return _capDo.GhiUyTinAsync(p.UserId, p.Reputation, envelope.OccurredAt, ProjectGradedProcessor.HeThong(envelope), ct);
        }
    }

    public sealed class FraudCaseConfirmedProcessor : IEventProcessor<FraudCaseConfirmed>
    {
        private readonly TaiKhoanAdminService _taiKhoan;

        public FraudCaseConfirmedProcessor(TaiKhoanAdminService taiKhoan)
        {
            if (taiKhoan == null)
            {
                throw new ArgumentNullException(nameof(taiKhoan));
            }

            _taiKhoan = taiKhoan;
        }

        public async Task XuLyAsync(EventEnvelope<FraudCaseConfirmed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            FraudCaseConfirmed p = envelope.Payload;
            if (!p.BlockAccounts)
            {
                return;
            }

            Caller heThong = ProjectGradedProcessor.HeThong(envelope);
            foreach (Guid userId in p.UserIds)
            {
                await _taiKhoan.KhoaTuHeThongAsync(userId, "Gian lan (ho so " + p.CaseId + "): " + p.Reason, heThong, ct);
            }
        }
    }
}
