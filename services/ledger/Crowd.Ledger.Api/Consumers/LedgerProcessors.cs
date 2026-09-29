using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Consumers
{
    // Moi processor chi chuyen tiep sang MoneyFlowService — logic tien nam MOT cho.
    // Chay ben trong transaction cua IdempotencyGuard: but toan, khoan treo va
    // event phat sinh commit CUNG processed_events (VD-D-02). Event phat sinh mang
    // causationId = eventId goc: tra loi duoc "vi sao labeler nay nhan tien".

    internal static class Nguon
    {
        public static Caller Tu<T>(EventEnvelope<T> e)
            where T : class, IEventPayload
        {
            return Caller.HeThong(e.CorrelationId, e.EventId);
        }
    }

    public sealed class ProjectPublishRequestedProcessor : IEventProcessor<ProjectPublishRequested>
    {
        private readonly MoneyFlowService _flow;

        public ProjectPublishRequestedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectPublishRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.DatKyQuyAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class ProjectPublishedProcessor : IEventProcessor<ProjectPublished>
    {
        private readonly MoneyFlowService _flow;

        public ProjectPublishedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectPublished> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GhiNhanRedundancyAsync(envelope.Payload, ct);
        }
    }

    public sealed class AnnotationApprovedProcessor : IEventProcessor<AnnotationApproved>
    {
        private readonly MoneyFlowService _flow;

        public AnnotationApprovedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<AnnotationApproved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.ChiTraNhanAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class ProjectCancelledProcessor : IEventProcessor<ProjectCancelled>
    {
        private readonly MoneyFlowService _flow;

        public ProjectCancelledProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, true, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class ProjectCompletedProcessor : IEventProcessor<ProjectCompleted>
    {
        private readonly MoneyFlowService _flow;

        public ProjectCompletedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, false, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class DepositConfirmedProcessor : IEventProcessor<DepositConfirmed>
    {
        private readonly MoneyFlowService _flow;

        public DepositConfirmedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<DepositConfirmed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.NapTienAsync(envelope.Payload, ct);
        }
    }

    public sealed class PayoutCompletedProcessor : IEventProcessor<PayoutCompleted>
    {
        private readonly MoneyFlowService _flow;

        public PayoutCompletedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<PayoutCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.HoanTatRutAsync(envelope.Payload, ct);
        }
    }

    public sealed class PayoutFailedProcessor : IEventProcessor<PayoutFailed>
    {
        private readonly MoneyFlowService _flow;

        public PayoutFailedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<PayoutFailed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.ThatBaiRutAsync(envelope.Payload, ct);
        }
    }

    /// <summary>user.blocked (identity): chan RUT tien. Tien van nguyen trong so.</summary>
    public sealed class UserBlockedProcessor : IEventProcessor<UserBlocked>
    {
        private readonly LedgerDbContext _db;
        private readonly TimeProvider _clock;

        public UserBlockedProcessor(LedgerDbContext db, TimeProvider clock)
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

        public async Task XuLyAsync(EventEnvelope<UserBlocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            Guid uid = envelope.Payload.UserId;
            if (!await _db.BlockedUsers.AnyAsync(b => b.UserId == uid, ct))
            {
                _db.BlockedUsers.Add(BlockedUser.Tao(uid, _clock.GetUtcNow()));
            }
        }
    }
}
