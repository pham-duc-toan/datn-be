using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Link;
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

            return _flow.GhiNhanRedundancyAsync(envelope.Payload, Nguon.Tu(envelope), ct);
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

            int? soNhan = envelope.Payload.ApprovedAnnotationCount;
            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, true, soNhan.HasValue ? soNhan.Value : 0, Nguon.Tu(envelope), ct);
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

            int? soNhan = envelope.Payload.ApprovedAnnotationCount;
            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, false, soNhan.HasValue ? soNhan.Value : 0, Nguon.Tu(envelope), ct);
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

    /// <summary>click.validated (gate-svc): tra nguoi chia se link theo luot vuot hop le.</summary>
    public sealed class ClickValidatedProcessor : IEventProcessor<ClickValidated>
    {
        private readonly MoneyFlowService _flow;

        public ClickValidatedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ClickValidated> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.ChiTraCongLinkAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }

    /// <summary>referral.registered (link-svc): ghi quan he gioi thieu de tinh hoa hong.</summary>
    public sealed class ReferralRegisteredProcessor : IEventProcessor<ReferralRegistered>
    {
        private readonly MoneyFlowService _flow;

        public ReferralRegisteredProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ReferralRegistered> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GhiGioiThieuAsync(envelope.Payload, ct);
        }
    }

    /// <summary>link.disabled (link-svc): vi pham da xac nhan → giu doanh thu dang treo cua link.</summary>
    public sealed class LinkDisabledProcessor : IEventProcessor<LinkDisabled>
    {
        private readonly MoneyFlowService _flow;

        public LinkDisabledProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<LinkDisabled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GiuDoanhThuLinkAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }
}
